using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Services;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;
using SalesInventory.Infrastructure.Persistence;
using SalesInventory.Infrastructure.Repositories;

namespace SalesInventory.Api.Tests.Services;

// Service-level tests against SQLite in-memory: unlike the EF InMemory provider it has real transactions
public sealed class PurchaseOrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly PurchaseOrderService _service;

    public PurchaseOrderServiceTests()
    {
        // The in-memory database lives only as long as this connection stays open
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _service = new PurchaseOrderService(
            new PurchaseOrderRepository(_db),
            new Repository<Supplier>(_db),
            new ProductRepository(_db),
            new Repository<StockMovement>(_db),
            new UnitOfWork(_db),
            NullLogger<PurchaseOrderService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // Seeded supplier 1 and products 1-2 come from AppDbContext.HasData (stock 50 and 100)
    private static PurchaseOrder NewDraft() => new()
    {
        SupplierId = 1,
        OrderDate = DateTime.UtcNow,
        PurchaseOrderItems =
        {
            new PurchaseOrderItem { ProductId = 1, Quantity = 10, UnitPrice = 20000m },
            new PurchaseOrderItem { ProductId = 2, Quantity = 5, UnitPrice = 15000m }
        }
    };

    private async Task<int> StockOfAsync(int productId) =>
        (await _db.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).StockQuantity;

    [Fact]
    public async Task ApprovePurchaseOrderAsync_CalledTwice_SecondCallThrowsAndStockIsNotAddedAgain()
    {
        // Arrange: a Draft does not touch stock
        var order = await _service.CreatePurchaseOrderAsync(NewDraft());
        Assert.Equal(50, await StockOfAsync(1));
        Assert.Equal(100, await StockOfAsync(2));

        // Act: the first approval succeeds and adds stock once
        await _service.ApprovePurchaseOrderAsync(order.Id);
        Assert.Equal(60, await StockOfAsync(1));
        Assert.Equal(105, await StockOfAsync(2));

        // Act + Assert: the second approval is rejected with a conflict
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.ApprovePurchaseOrderAsync(order.Id));
        Assert.Contains("Đã duyệt", ex.Message);

        // Stock and the movement ledger are exactly as after the first approval
        Assert.Equal(60, await StockOfAsync(1));
        Assert.Equal(105, await StockOfAsync(2));
        var movements = await _db.StockMovements.AsNoTracking().Where(m => m.RefId == order.Id).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m => Assert.Equal(StockMovementType.Import, m.MovementType));
        Assert.Equal(PurchaseOrderStatus.Approved, (await _db.PurchaseOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status);
    }
}
