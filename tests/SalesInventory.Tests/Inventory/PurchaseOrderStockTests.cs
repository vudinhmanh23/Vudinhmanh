using SalesInventory.Application.Exceptions;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;
using SalesInventory.Tests.Support;

namespace SalesInventory.Tests.Inventory;

// Purchase orders and stock. Receiving goods is a two-step process: creating the order only writes a Draft, and the stock
// goes up when the order is approved. So "an order of N units raises the stock by N" is tested as create + approve.
public class PurchaseOrderStockTests
{
    private const int StartingStock = 20;

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(250)]
    public async Task ApprovePurchaseOrderAsync_OrderOfNUnits_IncreasesStockByExactlyN(int n)
    {
        // Arrange: a product with 20 in stock and a draft purchase order for n units of it
        var harness = new InventoryHarness();
        harness.AddSupplier();
        var product = harness.AddProduct(1, StartingStock);
        var order = await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, n)));

        // Act
        await harness.PurchaseOrderService.ApprovePurchaseOrderAsync(order.Id);

        // Assert
        Assert.Equal(StartingStock + n, product.StockQuantity);
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_ValidOrder_SavesADraftAndLeavesStockUntouched()
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddSupplier();
        var product = harness.AddProduct(1, StartingStock);

        // Act
        var order = await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, 15)));

        // Assert: the goods have not arrived yet
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.Equal(StartingStock, product.StockQuantity);
        Assert.Empty(harness.Movements);
        Assert.Single(harness.PurchaseOrders);
    }

    [Fact]
    public async Task ApprovePurchaseOrderAsync_OrderWithSeveralLines_IncreasesEachProductByItsOwnQuantity()
    {
        // Arrange: product 1 has 5 in stock, product 2 has none
        var harness = new InventoryHarness();
        harness.AddSupplier();
        var first = harness.AddProduct(1, 5);
        var second = harness.AddProduct(2, 0);
        var order = await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, 3), (2, 7)));

        // Act
        await harness.PurchaseOrderService.ApprovePurchaseOrderAsync(order.Id);

        // Assert
        Assert.Equal(8, first.StockQuantity);
        Assert.Equal(7, second.StockQuantity);
    }

    [Fact]
    public async Task ApprovePurchaseOrderAsync_Approved_WritesOneImportRowPerLineWithTheStockAfter()
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddSupplier();
        harness.AddProduct(1, StartingStock);
        var order = await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, 12)));

        // Act
        await harness.PurchaseOrderService.ApprovePurchaseOrderAsync(order.Id);

        // Assert
        var movement = Assert.Single(harness.Movements);
        Assert.Equal(StockMovementType.Import, movement.MovementType);
        Assert.Equal(12, movement.Quantity);
        Assert.Equal(StartingStock + 12, movement.StockAfter);
        Assert.Equal(order.Code, movement.Reference);
        Assert.Equal(1, harness.UnitOfWork.Committed);
    }

    [Fact]
    public async Task ApprovePurchaseOrderAsync_AlreadyApproved_ThrowsConflictAndDoesNotAddTheStockTwice()
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddSupplier();
        var product = harness.AddProduct(1, StartingStock);
        var order = await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, 10)));
        await harness.PurchaseOrderService.ApprovePurchaseOrderAsync(order.Id);

        // Act
        var act = () => harness.PurchaseOrderService.ApprovePurchaseOrderAsync(order.Id);

        // Assert
        await Assert.ThrowsAsync<ConflictException>(act);
        Assert.Equal(StartingStock + 10, product.StockQuantity);
        Assert.Single(harness.Movements);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_UnknownProduct_ThrowsNotFoundAndSavesNothing()
    {
        // Arrange: the order names product 99, which does not exist
        var harness = new InventoryHarness();
        harness.AddSupplier();
        harness.AddProduct(1, StartingStock);

        // Act
        var act = () => harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, 5), (99, 5)));

        // Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(act);
        Assert.Contains("99", ex.Message);
        Assert.Empty(harness.PurchaseOrders);
    }
}
