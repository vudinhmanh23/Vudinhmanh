using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Services;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;
using SalesInventory.Infrastructure.Repositories;

namespace SalesInventory.Api.Tests.Services;

// Low-stock report rules against SQLite in-memory (a real relational engine running the real repository query)
public sealed class LowStockReportTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ProductService _service;

    public LowStockReportTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _service = new ProductService(new Repository<Product>(_db), new Repository<Category>(_db), new ProductRepository(_db), new Repository<StockMovement>(_db), new UnitOfWork(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private int AddProduct(int stock, int reorderLevel, bool isActive = true)
    {
        var product = new Product
        {
            Name = $"Product {Guid.NewGuid():N}"[..20],
            Sku = $"LS-{Guid.NewGuid():N}"[..20],
            StockQuantity = stock,
            ReorderLevel = reorderLevel,
            IsActive = isActive,
            CategoryId = 1,
            CreatedAt = DateTime.UtcNow
        };
        _db.Products.Add(product);
        _db.SaveChanges();
        return product.Id;
    }

    private async Task<List<LowStockItemDto>> LowStockAsync(bool includeInactive = false) =>
        (await _service.GetLowStockProductsAsync(includeInactive)).Select(LowStockItemDto.From).ToList();

    [Fact]
    public async Task StockBelowReorderLevel_IsListed_WithShortageEqualToTheGap()
    {
        var id = AddProduct(stock: 5, reorderLevel: 10);

        var item = Assert.Single(await LowStockAsync(), i => i.Id == id);

        Assert.Equal(5, item.StockQuantity);
        Assert.Equal(10, item.ReorderLevel);
        Assert.Equal(5, item.Shortage); // 10 - 5
    }

    [Fact]
    public async Task StockAboveReorderLevel_IsNotListed()
    {
        var id = AddProduct(stock: 30, reorderLevel: 10);

        Assert.DoesNotContain(await LowStockAsync(), i => i.Id == id);
    }

    [Fact]
    public async Task StockExactlyAtReorderLevel_IsListed_WithShortageZero()
    {
        var id = AddProduct(stock: 10, reorderLevel: 10);

        var item = Assert.Single(await LowStockAsync(), i => i.Id == id);
        Assert.Equal(0, item.Shortage);
    }

    [Fact]
    public async Task ReorderLevelZero_IsNeverListed_EvenWhenStockIsZero()
    {
        var id = AddProduct(stock: 0, reorderLevel: 0);

        Assert.DoesNotContain(await LowStockAsync(), i => i.Id == id);
    }

    [Fact]
    public async Task Results_AreOrderedByShortageDescending()
    {
        var small = AddProduct(stock: 8, reorderLevel: 10);   // 2
        var big = AddProduct(stock: 0, reorderLevel: 10);     // 10
        var medium = AddProduct(stock: 4, reorderLevel: 10);  // 6

        var ids = (await LowStockAsync()).Select(i => i.Id).ToList();

        Assert.Equal(new[] { big, medium, small }, ids);
    }

    [Fact]
    public async Task InactiveProducts_AreHiddenByDefault_AndShownWithIncludeInactive()
    {
        var active = AddProduct(stock: 1, reorderLevel: 10);
        var inactive = AddProduct(stock: 1, reorderLevel: 10, isActive: false);

        var byDefault = await LowStockAsync();
        Assert.Contains(byDefault, i => i.Id == active);
        Assert.DoesNotContain(byDefault, i => i.Id == inactive);

        var withInactive = await LowStockAsync(includeInactive: true);
        Assert.Contains(withInactive, i => i.Id == active);
        var row = Assert.Single(withInactive, i => i.Id == inactive);
        Assert.False(row.IsActive);
    }

    [Fact]
    public async Task IncludeInactive_DoesNotBypassTheOtherRules()
    {
        var inactiveButHealthy = AddProduct(stock: 30, reorderLevel: 10, isActive: false);
        var inactiveUntracked = AddProduct(stock: 0, reorderLevel: 0, isActive: false);

        var ids = (await LowStockAsync(includeInactive: true)).Select(i => i.Id).ToList();

        Assert.DoesNotContain(inactiveButHealthy, ids);
        Assert.DoesNotContain(inactiveUntracked, ids);
    }
}
