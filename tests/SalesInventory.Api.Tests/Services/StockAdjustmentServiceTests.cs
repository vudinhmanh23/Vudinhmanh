using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Services;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;
using SalesInventory.Infrastructure.Persistence;
using SalesInventory.Infrastructure.Repositories;

namespace SalesInventory.Api.Tests.Services;

// Manual stock adjustments against SQLite in-memory (real relational behaviour, not the EF InMemory provider)
public sealed class StockAdjustmentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly StockMovementService _service;

    public StockAdjustmentServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _service = new StockMovementService(new StockMovementRepository(_db), new Repository<Product>(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // Seeded product 1 (from AppDbContext.HasData) starts with stock 50
    private async Task<int> StockOfAsync(int productId) =>
        (await _db.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).StockQuantity;

    [Fact]
    public async Task AdjustStockAsync_DeltaWouldMakeStockNegative_ThrowsConflict_AndStockAndLedgerAreUntouched()
    {
        // Act + Assert: removing 51 from a stock of 50 must be refused
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.AdjustStockAsync(1, -51, "Stocktake: 51 missing"));
        Assert.Contains("negative", ex.Message);

        // The stock is still 50 and no ledger row was written (re-read from the database, not from the tracked entity)
        Assert.Equal(50, await StockOfAsync(1));
        Assert.Empty(await _db.StockMovements.AsNoTracking().Where(m => m.ProductId == 1).ToListAsync());
    }

    [Fact]
    public async Task AdjustStockAsync_DeltaTakesStockExactlyToZero_IsAllowed()
    {
        // Boundary: 50 - 50 = 0 is not negative, so it must pass
        var result = await _service.AdjustStockAsync(1, -50, "Write-off");

        Assert.Equal(50, result.PreviousQuantity);
        Assert.Equal(0, result.NewQuantity);
        Assert.Equal(0, await StockOfAsync(1));
    }

    [Fact]
    public async Task AdjustStockAsync_ValidDelta_UpdatesStock_AndLogsAdjustmentMovementWithReason()
    {
        await _service.AdjustStockAsync(1, -7, "  Stocktake: 7 damaged  ");

        Assert.Equal(43, await StockOfAsync(1));
        var movement = Assert.Single(await _db.StockMovements.AsNoTracking().Where(m => m.ProductId == 1).ToListAsync());
        Assert.Equal(StockMovementType.Adjustment, movement.MovementType);
        Assert.Equal(-7, movement.Quantity);
        Assert.Equal("Stocktake: 7 damaged", movement.Note);
    }

    [Theory]
    [InlineData(0, "reason")]
    [InlineData(5, "   ")]
    public async Task AdjustStockAsync_ZeroDeltaOrBlankReason_IsRejected_AndNothingChanges(int delta, string reason)
    {
        await Assert.ThrowsAsync<ValidationException>(() => _service.AdjustStockAsync(1, delta, reason));

        Assert.Equal(50, await StockOfAsync(1));
        Assert.Empty(await _db.StockMovements.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AdjustStockAsync_UnknownProduct_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.AdjustStockAsync(999999, 1, "reason"));
    }
}
