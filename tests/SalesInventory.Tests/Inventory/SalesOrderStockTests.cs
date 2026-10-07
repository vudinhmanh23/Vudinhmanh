using FluentValidation;
using SalesInventory.Application.Exceptions;
using SalesInventory.Domain.Enums;
using SalesInventory.Tests.Support;

namespace SalesInventory.Tests.Inventory;

// Sales orders and stock: a sale takes exactly what was sold out of the stock, and stock can never go below zero.
public class SalesOrderStockTests
{
    private const int StartingStock = 100;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(99)]
    public async Task CreateOrderAsync_SaleOfMUnits_DecreasesStockByExactlyM(int m)
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddCustomer();
        var product = harness.AddProduct(1, StartingStock);

        // Act
        await harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, m)));

        // Assert
        Assert.Equal(StartingStock - m, product.StockQuantity);
    }

    [Fact]
    public async Task CreateOrderAsync_ValidSale_WritesOneSaleRowWithNegativeQuantityAndTheStockAfter()
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddCustomer();
        harness.AddProduct(1, StartingStock);

        // Act
        var result = await harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, 30)));

        // Assert
        var movement = Assert.Single(harness.Movements);
        Assert.Equal(StockMovementType.Sale, movement.MovementType);
        Assert.Equal(-30, movement.Quantity);
        Assert.Equal(70, movement.StockAfter);
        Assert.Equal(result.Order.OrderNumber, movement.Reference);
        Assert.Equal(1, harness.UnitOfWork.Committed);
        Assert.Equal(0, harness.UnitOfWork.RolledBack);
    }

    [Fact]
    public async Task CreateOrderAsync_QuantityExceedsStock_ThrowsInsufficientStockAndKeepsTheStock()
    {
        // Arrange: 5 in stock, a customer wants 6
        var harness = new InventoryHarness();
        harness.AddCustomer();
        var product = harness.AddProduct(1, 5);

        // Act
        var act = () => harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, 6)));

        // Assert: refused with the numbers, and nothing was sold, written or committed
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(act);
        var shortage = Assert.Single(ex.Shortages);
        Assert.Equal((1, 6, 5, 1), (shortage.ProductId, shortage.Requested, shortage.Available, shortage.Missing));
        Assert.Equal(5, product.StockQuantity);
        Assert.True(product.StockQuantity >= 0);
        Assert.Empty(harness.Movements);
        Assert.Empty(harness.SalesOrders);
        Assert.Equal(0, harness.UnitOfWork.Committed);
        Assert.Equal(1, harness.UnitOfWork.RolledBack);
    }

    [Fact]
    public async Task CreateOrderAsync_SaleOfAllTheStock_LeavesZeroAndNotNegative()
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddCustomer();
        var product = harness.AddProduct(1, 5);

        // Act
        await harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, 5)));

        // Assert
        Assert.Equal(0, product.StockQuantity);
    }

    [Fact]
    public async Task CreateOrderAsync_ProductAlreadySoldOut_ThrowsInsufficientStockAndStaysAtZero()
    {
        // Arrange: sell everything, then try to sell one more
        var harness = new InventoryHarness();
        harness.AddCustomer();
        var product = harness.AddProduct(1, 5);
        await harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, 5)));

        // Act
        var act = () => harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, 1)));

        // Assert
        await Assert.ThrowsAsync<InsufficientStockException>(act);
        Assert.Equal(0, product.StockQuantity);
        Assert.Single(harness.SalesOrders);
    }

    [Fact]
    public async Task CreateOrderAsync_TwoLinesOfTheSameProductTogetherExceedStock_ThrowsAndKeepsTheStock()
    {
        // Arrange: each line (3) fits in the 5 in stock, but together (6) they do not
        var harness = new InventoryHarness();
        harness.AddCustomer();
        var product = harness.AddProduct(1, 5);

        // Act
        var act = () => harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, 3), (1, 3)));

        // Assert
        await Assert.ThrowsAsync<InsufficientStockException>(act);
        Assert.Equal(5, product.StockQuantity);
        Assert.Empty(harness.Movements);
    }

    [Fact]
    public async Task CreateOrderAsync_OneLineOversellsInAMultiLineOrder_NoProductIsReduced()
    {
        // Arrange: product 1 could be sold (4 of 10), product 2 cannot (3 of 2)
        var harness = new InventoryHarness();
        harness.AddCustomer();
        var fine = harness.AddProduct(1, 10);
        var short_ = harness.AddProduct(2, 2);

        // Act
        var act = () => harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, 4), (2, 3)));

        // Assert: all or nothing
        await Assert.ThrowsAsync<InsufficientStockException>(act);
        Assert.Equal(10, fine.StockQuantity);
        Assert.Equal(2, short_.StockQuantity);
        Assert.Empty(harness.Movements);
        Assert.Empty(harness.SalesOrders);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task CreateOrderAsync_QuantityNotPositive_ThrowsValidationAndDoesNotTouchTheStock(int quantity)
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddCustomer();
        var product = harness.AddProduct(1, StartingStock);

        // Act
        var act = () => harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, quantity)));

        // Assert: a negative quantity must never turn a sale into a stock increase
        await Assert.ThrowsAsync<ValidationException>(act);
        Assert.Equal(StartingStock, product.StockQuantity);
        Assert.Equal(0, harness.UnitOfWork.Begun);
    }

    [Fact]
    public async Task CreateOrderAsync_UnknownProduct_ThrowsNotFoundAndKeepsTheOtherStock()
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddCustomer();
        var product = harness.AddProduct(1, StartingStock);

        // Act
        var act = () => harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, 2), (99, 1)));

        // Assert
        await Assert.ThrowsAsync<NotFoundException>(act);
        Assert.Equal(StartingStock, product.StockQuantity);
        Assert.Empty(harness.SalesOrders);
    }

    [Fact]
    public async Task CreateOrderAsync_UnknownCustomer_ThrowsNotFoundAndKeepsTheStock()
    {
        // Arrange: no customer 1
        var harness = new InventoryHarness();
        var product = harness.AddProduct(1, StartingStock);

        // Act
        var act = () => harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, 2)));

        // Assert
        await Assert.ThrowsAsync<NotFoundException>(act);
        Assert.Equal(StartingStock, product.StockQuantity);
    }

    [Fact]
    public async Task CreateOrderAsync_SaleThatReachesTheThreshold_ReportsALowStockWarning()
    {
        // Arrange: threshold 5, selling 96 of 100 leaves 4
        var harness = new InventoryHarness();
        harness.AddCustomer();
        harness.AddProduct(1, StartingStock, name: "Máy giặt ABC", lowStockThreshold: 5);

        // Act
        var result = await harness.SalesOrderService.CreateOrderAsync(InventoryHarness.SalesOrderOf(1, (1, 96)));

        // Assert: the sale still goes through, with a warning
        var low = Assert.Single(result.LowStockProducts);
        Assert.Equal((1, 4), (low.ProductId, low.StockQuantity));
        Assert.Single(result.Warnings);
    }
}
