using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SalesInventory.Api.Data;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Api.Tests;

// Boots the real API pipeline (Identity, JWT auth, [Authorize] policies) against an isolated
// in-memory database instead of the real SQL Server instance, so tests don't touch dev data.
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"IntegrationTests_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        // Seeded explicitly (rather than relying on OnModelCreating's HasData + EnsureCreated) because
        // Program.cs's own role-seeding touches the in-memory store first, which makes EF Core's
        // InMemory provider treat the database as already created and skip HasData seeding.
        if (!db.Categories.Any(c => c.Id == 1))
        {
            db.Categories.Add(new Category { Id = 1, Name = "Test Category", Description = "Seeded for integration tests" });
        }

        if (!db.Suppliers.Any(s => s.Id == 1))
        {
            db.Suppliers.Add(new Supplier { Id = 1, Name = "Test Supplier", Phone = "0900000000" });
        }

        if (!db.Products.Any(p => p.Id == 1))
        {
            db.Products.Add(new Product
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

        db.SaveChanges();

        return host;
    }
}
