using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Core;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Api.Tests;

// Real environment variables are process-wide, so the tests that set them must not run at the same time as other tests
[CollectionDefinition(Name, DisableParallelization = true)]
public class ProcessEnvironmentCollection
{
    public const string Name = "Process environment variables";
}

// The production configuration as a deployment gives it: the settings files that ship with the app plus REAL environment variables
// (not the test host's UseSetting shortcut). Proves that ConnectionStrings__DefaultConnection is the connection string the app uses.
[Collection(ProcessEnvironmentCollection.Name)]
public class ProductionEnvironmentVariableTests
{
    // The second name is what Azure App Service creates for an entry of type "SQLAzure" under "Connection strings"
    // (the configuration system maps the SQLAZURECONNSTR_ prefix to ConnectionStrings:)
    [Theory]
    [InlineData("ConnectionStrings__DefaultConnection")]
    [InlineData("SQLAZURECONNSTR_DefaultConnection")]
    public async Task ConnectionString_SetAsAnEnvironmentVariable_IsTheOneTheApplicationUsesAndNeverAppearsInTheLog(string variableName)
    {
        if (TestDatabase.UseInMemory)
        {
            return; // there is no real database to point the variable at (SALESINVENTORY_TESTS_DB=InMemory)
        }

        // Arrange: a database whose name can be told apart, and the variables a Production deployment must provide
        var databaseName = $"EnvVarTest_{Guid.NewGuid():N}";
        var connectionString = TestDatabase.Create(databaseName);
        var password = new SqlConnectionStringBuilder(connectionString).Password;
        var jwtKey = $"env-var-test-key-{Guid.NewGuid():N}{Guid.NewGuid():N}";
        var logLines = new ConcurrentQueue<string>();

        using var variables = new EnvironmentVariables(
            (variableName, connectionString),
            ("Jwt__Key", jwtKey));

        try
        {
            // The host reads appsettings.json + appsettings.Production.json + the environment, exactly like a deployment.
            // Only the log file location is redirected (JSON source, as in CustomWebApplicationFactory), so the test leaves no logs/ folder.
            var logFile = Path.Combine(Path.GetTempPath(), "salesinventory-tests", "logs", "envvar-.log");
            var logOverride = JsonSerializer.Serialize(new
            {
                Serilog = new { WriteTo = new Dictionary<string, object> { ["1"] = new { Args = new { path = logFile } } } }
            });

            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.ConfigureAppConfiguration((_, config) => config.AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(logOverride))));
                builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(new CaptureSink(logLines)));
            });

            // Act
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://api.example.test"), AllowAutoRedirect = false });
            var health = await client.GetAsync("/api/health/db");

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var usedConnectionString = db.Database.GetConnectionString();
            var usedDatabase = db.Database.SqlQueryRaw<string>("SELECT DB_NAME() AS [Value]").AsEnumerable().Single();

            // Assert: the string from the variable, and the database it names (a different one would be a different name)
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.Equal("connected", (await health.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("database").GetString());
            Assert.Equal(connectionString, usedConnectionString);
            Assert.Equal(databaseName, usedDatabase);

            // And no secret in the log: not the database password, not the JWT key, not the whole connection string
            Assert.NotEmpty(logLines);
            Assert.DoesNotContain(logLines, line => line.Contains(password));
            Assert.DoesNotContain(logLines, line => line.Contains(jwtKey));
            Assert.DoesNotContain(logLines, line => line.Contains(connectionString));
        }
        finally
        {
            TestDatabase.Drop(databaseName);
        }
    }

    // Sets process environment variables and puts the previous values back when disposed
    private sealed class EnvironmentVariables : IDisposable
    {
        private readonly List<(string Name, string? Previous)> _saved = new();

        public EnvironmentVariables(params (string Name, string Value)[] variables)
        {
            foreach (var (name, value) in variables)
            {
                _saved.Add((name, Environment.GetEnvironmentVariable(name)));
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _saved)
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }
    }
}
