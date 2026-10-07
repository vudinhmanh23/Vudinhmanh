using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Api.Tests;

// Skipped when the tests do not run on a real SQL Server (Docker missing or SALESINVENTORY_TESTS_DB=InMemory)
public sealed class RealSqlServerFactAttribute : FactAttribute
{
    public RealSqlServerFactAttribute()
    {
        Skip = TestDatabase.UnavailableReason;
    }
}

// What a real SQL Server enforces and the EF Core InMemory provider does not. These are the reasons the integration tests run on a
// SQL Server container: with InMemory, each of the "should be refused" cases below would be silently ACCEPTED, so a test of
// the business rules could be green while production (which has these constraints) would fail or, worse, behave differently.
public class RealSqlServerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RealSqlServerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [RealSqlServerFact]
    public async Task Database_IsARealSqlServerEngineInTheContainer()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Act
        var version = await db.Database.SqlQueryRaw<string>("SELECT CAST(@@VERSION AS nvarchar(300)) AS [Value]").SingleAsync();

        // Assert: the provider is SQL Server, the engine is the real one (Linux container), and it has the schema of the migrations
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        Assert.Contains("Microsoft SQL Server 2022", version);
        Assert.Contains("Linux", version);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [RealSqlServerFact]
    public void Products_NegativeStock_IsRefusedByTheDatabaseItself()
    {
        // Arrange: a product with 100 in stock
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = db.Products.Single(p => p.Id == 1);

        // Act: even code that forgets the business rule cannot store a negative stock
        product.StockQuantity = -1;
        Action act = () => db.SaveChanges();

        // Assert: the CHECK constraint CK_Products_StockQuantity_NonNegative says no
        var ex = Assert.Throws<DbUpdateException>(act);
        var sql = Assert.IsType<SqlException>(ex.InnerException);
        Assert.Contains("CK_Products_StockQuantity_NonNegative", sql.Message);
    }

    [RealSqlServerFact]
    public void Products_DuplicateSku_IsRefusedByTheUniqueIndex()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Products.Add(NewProduct("SEED-0001")); // the sku of the seeded product

        // Act
        Action act = () => db.SaveChanges();

        // Assert
        var ex = Assert.Throws<DbUpdateException>(act);
        Assert.IsType<SqlException>(ex.InnerException);
    }

    [RealSqlServerFact]
    public void Products_UnknownCategory_IsRefusedByTheForeignKey()
    {
        // Arrange: a product that points at a category that does not exist
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = NewProduct($"FK-{Guid.NewGuid():N}"[..16]);
        product.CategoryId = 424242;
        db.Products.Add(product);

        // Act
        Action act = () => db.SaveChanges();

        // Assert
        var ex = Assert.Throws<DbUpdateException>(act);
        Assert.Contains("FOREIGN KEY", Assert.IsType<SqlException>(ex.InnerException).Message);
    }

    [RealSqlServerFact]
    public void Products_RowVersion_ChangesWithEveryUpdate_SoTwoBuyersCannotBothWin()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = db.Products.Single(p => p.Id == 1);
        var before = product.RowVersion!.ToArray();

        // Act
        product.StockQuantity -= 1;
        db.SaveChanges();

        // Assert: the database stamped a new version; a second writer holding the old one would get a concurrency error
        Assert.NotNull(product.RowVersion);
        Assert.NotEqual(before, product.RowVersion);
    }

    // The contrast, in code: the same three mistakes against the InMemory provider are all accepted without a murmur
    [Fact]
    public void InMemoryProvider_AcceptsNegativeStockDuplicateSkuAndUnknownCategory()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"contrast_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var db = new AppDbContext(options);

        // Act: negative stock, two products with the same SKU, and a category that does not exist
        var first = NewProduct("SAME-SKU");
        first.StockQuantity = -5;
        var second = NewProduct("SAME-SKU");
        second.CategoryId = 424242;
        db.Products.AddRange(first, second);
        var saved = db.SaveChanges();

        // Assert: nothing was refused, although production would refuse all three
        Assert.True(saved >= 2);
        Assert.Equal(2, db.Products.Count(p => p.Sku == "SAME-SKU"));
    }

    private static Product NewProduct(string sku) => new()
    {
        Name = "Constraint test",
        Sku = sku,
        Price = 1000,
        StockQuantity = 1,
        CategoryId = 1,
        SupplierId = 1,
        CreatedAt = DateTime.UtcNow
    };
}
