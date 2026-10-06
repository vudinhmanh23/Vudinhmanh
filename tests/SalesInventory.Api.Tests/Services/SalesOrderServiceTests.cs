using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Services;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;
using SalesInventory.Infrastructure.Repositories;

namespace SalesInventory.Api.Tests.Services;

// SalesOrderService against SQLite in-memory: a real relational engine with real transactions,
// so "nothing changed after the failure" is proven by re-reading the database, not the tracked entities
public sealed class SalesOrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly SalesOrderService _service;
    private readonly int _customerId;

    public SalesOrderServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        var customer = new Customer { Name = "Test customer" };
        _db.Customers.Add(customer);
        _db.SaveChanges();
        _customerId = customer.Id;

        // Seeded products 1 and 2 (AppDbContext.HasData) start with stock 50 and 100
        _service = new SalesOrderService(
            new SalesOrderRepository(_db),
            new Repository<Customer>(_db),
            new Repository<Product>(_db),
            new Repository<StockMovement>(_db),
            new UnitOfWork(_db),
            NullLogger<SalesOrderService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private SalesOrder Order(params (int ProductId, int Quantity)[] lines) => new()
    {
        CustomerId = _customerId,
        OrderDate = DateTime.UtcNow,
        Items = lines.Select(l => new SalesOrderItem { ProductId = l.ProductId, Quantity = l.Quantity, UnitPrice = 100m }).ToList()
    };

    // Always a fresh query without tracking, so a stale in-memory entity cannot hide a persisted change
    private async Task<int> StockOfAsync(int productId) =>
        (await _db.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).StockQuantity;

    private async Task AssertNothingPersistedAsync()
    {
        Assert.Empty(await _db.SalesOrders.AsNoTracking().ToListAsync());
        Assert.Empty(await _db.SalesOrderItems.AsNoTracking().ToListAsync());
        Assert.Empty(await _db.StockMovements.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateOrderAsync_QuantityAboveStock_ThrowsInsufficientStock_AndStockIsUnchanged()
    {
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => _service.CreateOrderAsync(Order((1, 51))));

        var shortage = Assert.Single(ex.Shortages);
        Assert.Equal(1, shortage.ProductId);
        Assert.Equal(51, shortage.Requested);
        Assert.Equal(50, shortage.Available);
        Assert.Contains(shortage.ProductName, ex.Message);

        // The state AFTER the failure: stock still 50, no order, no items, no ledger rows
        Assert.Equal(50, await StockOfAsync(1));
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_OneLineFineOneLineOversold_ThrowsAndNeitherProductChanges()
    {
        await Assert.ThrowsAsync<InsufficientStockException>(() => _service.CreateOrderAsync(Order((1, 10), (2, 101))));

        Assert.Equal(50, await StockOfAsync(1));
        Assert.Equal(100, await StockOfAsync(2));
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_SameProductOnTwoLines_IsCheckedAgainstTheSum()
    {
        // 30 + 30 = 60 > 50: each line alone would pass
        await Assert.ThrowsAsync<InsufficientStockException>(() => _service.CreateOrderAsync(Order((1, 30), (1, 30))));

        Assert.Equal(50, await StockOfAsync(1));
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_UnknownProductOnSecondLine_ThrowsNotFound_AndFirstProductIsNotDeducted()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateOrderAsync(Order((1, 5), (9999, 1))));

        Assert.Equal(50, await StockOfAsync(1));
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_FailureRightBeforeCommit_RollsBackOrderStockAndMovements()
    {
        // Everything has been written inside the transaction (order, deduction, movement) when the commit blows up
        var service = new SalesOrderService(
            new SalesOrderRepository(_db),
            new Repository<Customer>(_db),
            new Repository<Product>(_db),
            new Repository<StockMovement>(_db),
            new CommitFailsUnitOfWork(new UnitOfWork(_db)),
            NullLogger<SalesOrderService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(Order((1, 5))));

        Assert.Equal(50, await StockOfAsync(1));
        await AssertNothingPersistedAsync();
    }

    // Test double: behaves like the real unit of work except that CommitAsync always fails
    private sealed class CommitFailsUnitOfWork : SalesInventory.Application.Interfaces.IUnitOfWork
    {
        private readonly SalesInventory.Application.Interfaces.IUnitOfWork _inner;

        public CommitFailsUnitOfWork(SalesInventory.Application.Interfaces.IUnitOfWork inner) => _inner = inner;

        public void ResetTracking() => _inner.ResetTracking();

        public async Task<SalesInventory.Application.Interfaces.IUnitOfWorkTransaction> BeginTransactionAsync() =>
            new FailingTransaction(await _inner.BeginTransactionAsync());

        private sealed class FailingTransaction : SalesInventory.Application.Interfaces.IUnitOfWorkTransaction
        {
            private readonly SalesInventory.Application.Interfaces.IUnitOfWorkTransaction _inner;

            public FailingTransaction(SalesInventory.Application.Interfaces.IUnitOfWorkTransaction inner) => _inner = inner;

            public Task CommitAsync() => throw new InvalidOperationException("test rollback");

            public Task RollbackAsync() => _inner.RollbackAsync();

            public ValueTask DisposeAsync() => _inner.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateOrderAsync_ExactlyAllStock_Succeeds_AndLeavesZeroNotNegative()
    {
        await _service.CreateOrderAsync(Order((1, 50)));

        Assert.Equal(0, await StockOfAsync(1));
        var movement = Assert.Single(await _db.StockMovements.AsNoTracking().ToListAsync());
        Assert.Equal(-50, movement.Quantity);
        Assert.Equal(0, movement.StockAfter);
    }
}
