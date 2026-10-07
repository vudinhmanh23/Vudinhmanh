using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace SalesInventory.Api.Tests;

// The database of the integration tests: a REAL SQL Server running in a Docker container (Testcontainers), the same engine as
// production. One container is started for the whole test run (starting it takes a few seconds, so it is not done per test) and
// every test host gets its own empty database on it, created by the real EF Core migrations and dropped when the host is disposed.
//
// Why not the EF Core InMemory provider: it is a dictionary in memory, not a database. It has no unique indexes, foreign keys or
// CHECK constraints, no rowversion, no real transactions, no SQL translation (any LINQ "works") and no collation rules, so a test
// can pass while production fails. On SQL Server the same tests run against the same constraints, the same SQL and the same
// migrations that production uses.
//
// Needs Docker running. On a machine without Docker, set the environment variable SALESINVENTORY_TESTS_DB=InMemory to fall back
// to the in-memory provider (a faster, less faithful run).
internal static class TestDatabase
{
    private static readonly Lazy<MsSqlContainer> Server = new(StartServer);

    // Test hosts are created in parallel and each one blocks while the container starts. Starting the container is itself async
    // and needs pool threads to continue; with every thread blocked it would wait for a new thread to be injected (about one per
    // second) and time out. A high minimum makes the pool create threads at once instead.
    static TestDatabase()
    {
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.SetMinThreads(Math.Max(workers, 128), Math.Max(io, 128));
    }

    public static bool UseInMemory =>
        string.Equals(Environment.GetEnvironmentVariable("SALESINVENTORY_TESTS_DB"), "InMemory", StringComparison.OrdinalIgnoreCase);

    // Why the SQL Server tests cannot run here (null = they can): the fallback mode is on, or Docker is not running
    public static string? UnavailableReason =>
        UseInMemory ? "SALESINVENTORY_TESTS_DB=InMemory: the tests that need a real SQL Server are skipped."
        : DockerRunning.Value ? null
        : "Docker is not running (start Docker Desktop, or set SALESINVENTORY_TESTS_DB=InMemory).";

    private static readonly Lazy<bool> DockerRunning = new(ProbeDocker);

    // `docker info` only succeeds when the Docker engine answers
    private static bool ProbeDocker()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null || !process.WaitForExit(20_000))
            {
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false; // the docker command is not installed
        }
    }

    private static MsSqlContainer StartServer()
    {
        if (!DockerRunning.Value)
        {
            throw new InvalidOperationException(
                "The integration tests need Docker (they run against a real SQL Server in a container), and the Docker engine is not " +
                "running. Start Docker Desktop, or set SALESINVENTORY_TESTS_DB=InMemory to use the in-memory fallback.");
        }

        // A busy machine can stall one start-up; a second attempt on a fresh container almost always works
        for (var attempt = 1; ; attempt++)
        {
            // The official SQL Server 2022 image; the SA password is throwaway and the container is deleted afterwards
            var container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            try
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                container.StartAsync(limit.Token).GetAwaiter().GetResult();
                return container;
            }
            catch (Exception) when (attempt < 2)
            {
                container.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    // The connection string of a database on the container, without creating it (for tests that create their own schema)
    public static string ConnectionStringFor(string databaseName) =>
        new SqlConnectionStringBuilder(Server.Value.GetConnectionString()) { InitialCatalog = databaseName }.ConnectionString;

    // Creates an empty database with every migration applied and returns its connection string
    public static string Create(string databaseName)
    {
        // Creating a database and applying every migration is heavy; dozens of test hosts starting at once would overload the server
        CreateGate.Wait();
        try
        {
            return CreateCore(databaseName);
        }
        finally
        {
            CreateGate.Release();
        }
    }

    private static readonly SemaphoreSlim CreateGate = new(4);

    private static string CreateCore(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(Server.Value.GetConnectionString()) { InitialCatalog = databaseName };
        var connectionString = builder.ConnectionString;

        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
        db.Database.Migrate();
        return connectionString;
    }

    public static void Drop(string databaseName)
    {
        if (!Server.IsValueCreated)
        {
            return;
        }

        try
        {
            var master = new SqlConnectionStringBuilder(Server.Value.GetConnectionString()) { InitialCatalog = "master", ConnectTimeout = 15 }.ConnectionString;
            using var connection = new SqlConnection(master);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandTimeout = 20; // never wait for long: a stuck cleanup must not hold up the test run
            // Pooled connections of the finished host may still be open; kick them out before dropping
            command.CommandText =
                $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END";
            command.ExecuteNonQuery();
        }
        catch (SqlException)
        {
            // Best effort: the whole container is removed when the run ends
        }
    }

    // The connection string of the server itself (database master), for tests that create their own databases
    public static string MasterConnectionString =>
        new SqlConnectionStringBuilder(Server.Value.GetConnectionString()) { InitialCatalog = "master" }.ConnectionString;

    // Adds an entity with a key the test chose. On SQL Server an identity column refuses that unless IDENTITY_INSERT is on for
    // the table, which is only for the duration of this insert. (The InMemory provider accepts it silently.)
    public static void AddWithKey<T>(AppDbContext db, T entity) where T : class
    {
        if (UseInMemory)
        {
            db.Add(entity);
            db.SaveChanges();
            return;
        }

        // The table name comes from the EF model (our own entity), never from input, so building the statement from it is safe
#pragma warning disable EF1002
        var table = db.Model.FindEntityType(typeof(T))!.GetTableName();
        using var transaction = db.Database.BeginTransaction();
        db.Database.ExecuteSqlRaw($"SET IDENTITY_INSERT [{table}] ON");
        db.Add(entity);
        db.SaveChanges();
        db.Database.ExecuteSqlRaw($"SET IDENTITY_INSERT [{table}] OFF");
        transaction.Commit();
#pragma warning restore EF1002
    }
}
