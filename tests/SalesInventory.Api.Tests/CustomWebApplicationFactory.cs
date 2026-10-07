using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SalesInventory.Infrastructure.Persistence;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Api.Tests;

// Boots the real API pipeline (Identity, JWT auth, [Authorize] policies) against an isolated database, so tests don't touch dev data.
// The database is a real SQL Server in a Docker container created by the real migrations (see TestDatabase); setting the
// environment variable SALESINVENTORY_TESTS_DB=InMemory falls back to the EF Core InMemory provider when Docker is not available.
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"IntegrationTests_{Guid.NewGuid():N}";

    // Where the log files of this host are written (a test can point it at its own folder and read the files back)
    protected virtual string LogDirectory => Path.Combine(Path.GetTempPath(), "salesinventory-tests", "logs");

    // Hook for subclasses to supply extra settings (e.g. SeedAdmin) before the host starts
    protected virtual void ConfigureExtraSettings(IWebHostBuilder builder)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ConfigureExtraSettings(builder);

        // Log files of the test hosts go to the temp folder, not next to the test binaries (index 1 = the File sink of appsettings.json)
        // A JSON source, not UseSetting: UseSetting("Serilog:WriteTo:1:...") leaves an EMPTY value on the parent key "Serilog:WriteTo",
        // which Serilog reads as a sink with no name and then configures none of the sinks
        var logOverride = System.Text.Json.JsonSerializer.Serialize(new
        {
            Serilog = new { WriteTo = new Dictionary<string, object> { ["1"] = new { Args = new { path = Path.Combine(LogDirectory, "test-.log") } } } }
        });
        builder.ConfigureAppConfiguration((_, config) => config.AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(logOverride))));

        // Throwaway key so tests don't depend on the developer's user-secrets (e.g. on CI)
        builder.UseSetting("Jwt:Key", "integration-tests-only-key-0123456789abcdef");

        // The database has to exist, with its schema, before the host starts: Program.cs seeds the roles during start-up
        var connectionString = TestDatabase.UseInMemory ? null : TestDatabase.Create(_databaseName);

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            if (connectionString is not null)
            {
                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
            }
            else
            {
                // The InMemory provider has no real transactions; ignore its warning so services that
                // BeginTransaction still run (atomic rollback is only verifiable against SQL Server)
                services.AddDbContext<AppDbContext>(options => options
                    .UseInMemoryDatabase(_databaseName)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (TestDatabase.UseInMemory)
        {
            db.Database.EnsureCreated();
        }
        else
        {
            // The migrations also insert demo data (categories, suppliers, ten products). The tests own their data, so start
            // from empty tables; roles and the schema stay as the migrations made them.
            db.Database.ExecuteSqlRaw("DELETE FROM [Products]; DELETE FROM [Suppliers]; DELETE FROM [Categories];");
        }

        // Seeded explicitly: one category, one supplier and one product with known ids and stock
        if (!db.Categories.Any(c => c.Id == 1))
        {
            TestDatabase.AddWithKey(db, new Category { Id = 1, Name = "Test Category", Description = "Seeded for integration tests" });
        }

        if (!db.Suppliers.Any(s => s.Id == 1))
        {
            TestDatabase.AddWithKey(db, new Supplier { Id = 1, Code = "SUP-001", Name = "Test Supplier", Phone = "0900000000" });
        }

        if (!db.Products.Any(p => p.Id == 1))
        {
            TestDatabase.AddWithKey(db, new Product
            {
                Id = 1,
                Name = "Seed Product",
                Sku = "SEED-0001",
                Price = 1000,
                StockQuantity = 100,
                CategoryId = 1,
                SupplierId = 1,
                CreatedAt = DateTime.UtcNow
            });
        }

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing); // stops the host first, which closes its connections

        if (disposing && !TestDatabase.UseInMemory)
        {
            TestDatabase.Drop(_databaseName);
        }
    }
}
