using FluentValidation;
using SalesInventory.Application.Exceptions;
using SalesInventory.Domain.Enums;
using SalesInventory.Tests.Support;

namespace SalesInventory.Tests.Inventory;

// The stock service itself: manual corrections (stocktake) with the same rule as sales, stock never below zero.
public class StockMovementServiceTests
{
    [Fact]
    public async Task AdjustStockAsync_PositiveDelta_IncreasesStockAndWritesAnAdjustmentRow()
    {
        // Arrange
        var harness = new InventoryHarness();
        var product = harness.AddProduct(1, 10);

        // Act
        var result = await harness.StockMovementService.AdjustStockAsync(1, 4, "Found 4 on the shelf");

        // Assert
        Assert.Equal(14, product.StockQuantity);
        Assert.Equal((10, 14), (result.PreviousQuantity, result.NewQuantity));
        var movement = Assert.Single(harness.Movements);
        Assert.Equal(StockMovementType.Adjustment, movement.MovementType);
        Assert.Equal((4, 14), (movement.Quantity, movement.StockAfter));
    }

    [Fact]
    public async Task AdjustStockAsync_NegativeDeltaWithinStock_DecreasesStockByExactlyThatAmount()
    {
        // Arrange
        var harness = new InventoryHarness();
        var product = harness.AddProduct(1, 10);

        // Act
        await harness.StockMovementService.AdjustStockAsync(1, -10, "Damaged goods written off");

        // Assert: the whole stock can be written off, down to exactly zero
        Assert.Equal(0, product.StockQuantity);
        Assert.Equal(-10, Assert.Single(harness.Movements).Quantity);
    }

    [Theory]
    [InlineData(-11)]
    [InlineData(-1000)]
    [InlineData(int.MinValue)]
    public async Task AdjustStockAsync_DeltaBelowTheStock_ThrowsConflictAndKeepsTheStockNonNegative(int delta)
    {
        // Arrange
        var harness = new InventoryHarness();
        var product = harness.AddProduct(1, 10);

        // Act
        var act = () => harness.StockMovementService.AdjustStockAsync(1, delta, "Too much");

        // Assert
        await Assert.ThrowsAsync<ConflictException>(act);
        Assert.Equal(10, product.StockQuantity);
        Assert.Empty(harness.Movements);
        Assert.Equal(1, harness.UnitOfWork.RolledBack);
        Assert.Equal(0, harness.UnitOfWork.Committed);
    }

    [Fact]
    public async Task AdjustStockAsync_ZeroDelta_ThrowsValidationAndWritesNothing()
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddProduct(1, 10);

        // Act
        var act = () => harness.StockMovementService.AdjustStockAsync(1, 0, "Nothing");

        // Assert
        await Assert.ThrowsAsync<ValidationException>(act);
        Assert.Empty(harness.Movements);
    }

    [Fact]
    public async Task AdjustStockAsync_BlankReason_ThrowsValidationAndKeepsTheStock()
    {
        // Arrange
        var harness = new InventoryHarness();
        var product = harness.AddProduct(1, 10);

        // Act
        var act = () => harness.StockMovementService.AdjustStockAsync(1, 3, "   ");

        // Assert: every correction must say why
        await Assert.ThrowsAsync<ValidationException>(act);
        Assert.Equal(10, product.StockQuantity);
    }

    [Fact]
    public async Task AdjustStockAsync_UnknownProduct_ThrowsNotFound()
    {
        // Arrange
        var harness = new InventoryHarness();

        // Act
        var act = () => harness.StockMovementService.AdjustStockAsync(99, 5, "Whatever");

        // Assert
        await Assert.ThrowsAsync<NotFoundException>(act);
        Assert.Empty(harness.Movements);
    }

    [Fact]
    public async Task SetStockAsync_ValidQuantity_SetsExactlyThatQuantityAndRecordsTheDifference()
    {
        // Arrange: a stocktake finds 7 where the system says 10
        var harness = new InventoryHarness();
        var product = harness.AddProduct(1, 10);

        // Act
        var result = await harness.StockMovementService.SetStockAsync(1, 7, "Stocktake");

        // Assert
        Assert.Equal(7, product.StockQuantity);
        Assert.Equal(-3, result.Movement.Quantity);
        Assert.Equal(7, result.Movement.StockAfter);
    }

    [Fact]
    public async Task SetStockAsync_NegativeQuantity_ThrowsValidationAndKeepsTheStock()
    {
        // Arrange
        var harness = new InventoryHarness();
        var product = harness.AddProduct(1, 10);

        // Act
        var act = () => harness.StockMovementService.SetStockAsync(1, -1, "Stocktake");

        // Assert
        await Assert.ThrowsAsync<ValidationException>(act);
        Assert.Equal(10, product.StockQuantity);
        Assert.Empty(harness.Movements);
    }
}
