using SalesInventory.Application.Exceptions;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;
using SalesInventory.Tests.Support;

namespace SalesInventory.Tests.Inventory;

// Boundaries of purchase orders: quantities at and beyond the limits, duplicated lines and duplicated codes. Each [Theory] runs
// many cases through one test body.
//
// About "duplicate lots": this system has no lot or batch number. The duplicates it can have are (1) the same product on two
// lines of one order, which is allowed and summed, and (2) two orders getting the same code, which must never happen.
public class PurchaseOrderBoundaryTests
{
    // ---- quantities that must be refused ----

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public async Task CreatePurchaseOrderAsync_QuantityZeroOrNegative_ThrowsBusinessRuleAndSavesNothing(int quantity)
    {
        // Arrange: a known supplier and product, and an order whose only line has the bad quantity
        var harness = new InventoryHarness();
        harness.AddSupplier();
        harness.AddProduct(1, stock: 20);
        var order = InventoryHarness.PurchaseOrderOf(1, (1, quantity));

        // Act
        var act = () => harness.PurchaseOrderService.CreatePurchaseOrderAsync(order);

        // Assert: refused, nothing saved, so nothing can ever be approved into the stock
        await Assert.ThrowsAsync<BusinessRuleException>(act);
        Assert.Empty(harness.PurchaseOrders);
        Assert.Empty(harness.Movements);
    }

    [Theory]
    [InlineData(5, 0, false)]
    [InlineData(5, 0, true)]
    [InlineData(5, -1, false)]
    [InlineData(5, -1, true)]
    [InlineData(1000, int.MinValue, false)]
    public async Task CreatePurchaseOrderAsync_OneBadLineAmongGoodOnes_ThrowsAndSavesNothing(int goodQuantity, int badQuantity, bool badLineFirst)
    {
        // Arrange: product 1 gets a good line, product 2 a bad one, in either order. The good line must not sneak through.
        var harness = new InventoryHarness();
        harness.AddSupplier();
        harness.AddProduct(1, stock: 20);
        harness.AddProduct(2, stock: 20);
        var order = badLineFirst
            ? InventoryHarness.PurchaseOrderOf(1, (2, badQuantity), (1, goodQuantity))
            : InventoryHarness.PurchaseOrderOf(1, (1, goodQuantity), (2, badQuantity));

        // Act
        var act = () => harness.PurchaseOrderService.CreatePurchaseOrderAsync(order);

        // Assert
        await Assert.ThrowsAsync<BusinessRuleException>(act);
        Assert.Empty(harness.PurchaseOrders);
    }

    [Theory]
    [InlineData(3, -3)]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    public async Task CreatePurchaseOrderAsync_LinesThatAddUpToZeroOrLess_ThrowsBusinessRule(int first, int second)
    {
        // Arrange: two lines of the same product whose quantities cancel out or are both bad
        var harness = new InventoryHarness();
        harness.AddSupplier();
        harness.AddProduct(1, stock: 20);
        var order = InventoryHarness.PurchaseOrderOf(1, (1, first), (1, second));

        // Act
        var act = () => harness.PurchaseOrderService.CreatePurchaseOrderAsync(order);

        // Assert
        await Assert.ThrowsAsync<BusinessRuleException>(act);
        Assert.Empty(harness.PurchaseOrders);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_NoLines_ThrowsBusinessRuleAndSavesNothing()
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddSupplier();

        // Act
        var act = () => harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1));

        // Assert
        await Assert.ThrowsAsync<BusinessRuleException>(act);
        Assert.Empty(harness.PurchaseOrders);
    }

    [Fact]
    public async Task ApprovePurchaseOrderAsync_QuantitiesThatOverflowAnIntWhenAddedUp_AreHandledWithoutAnOverflowException()
    {
        // Arrange: two DIFFERENT products with int.MaxValue each. The total (about 4.3 billion) does not fit in an int, but each
        // product's own stock does, so this order is legitimate and must not crash while the lines are added up.
        var harness = new InventoryHarness();
        harness.AddSupplier();
        var first = harness.AddProduct(1, stock: 0);
        var second = harness.AddProduct(2, stock: 0);
        var order = await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, int.MaxValue), (2, int.MaxValue)));

        // Act
        await harness.PurchaseOrderService.ApprovePurchaseOrderAsync(order.Id);

        // Assert
        Assert.Equal(int.MaxValue, first.StockQuantity);
        Assert.Equal(int.MaxValue, second.StockQuantity);
    }

    [Fact]
    public async Task ApprovePurchaseOrderAsync_SameProductTwiceWithAnOverflowingTotal_ThrowsBusinessRuleAndChangesNothing()
    {
        // Arrange: ONE product on two lines of int.MaxValue each; together they do not fit
        var harness = new InventoryHarness();
        harness.AddSupplier();
        var product = harness.AddProduct(1, stock: 0);
        var order = await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, int.MaxValue), (1, int.MaxValue)));

        // Act
        var act = () => harness.PurchaseOrderService.ApprovePurchaseOrderAsync(order.Id);

        // Assert: the first line was not applied either (all or nothing), and the order is still a Draft
        await Assert.ThrowsAsync<BusinessRuleException>(act);
        Assert.Equal(0, product.StockQuantity);
        Assert.Empty(harness.Movements);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
    }

    // ---- the stock must never overflow into a negative number ----

    [Theory]
    [InlineData(1, int.MaxValue)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(100, int.MaxValue - 50)]
    public async Task ApprovePurchaseOrderAsync_StockWouldPassTheLimitOfAnInt_ThrowsBusinessRuleAndKeepsTheStock(int stock, int quantity)
    {
        // Arrange: stock + quantity is more than an int can hold; unchecked, it would wrap around to a NEGATIVE stock
        var harness = new InventoryHarness();
        harness.AddSupplier();
        var product = harness.AddProduct(1, stock);
        var order = await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, quantity)));

        // Act
        var act = () => harness.PurchaseOrderService.ApprovePurchaseOrderAsync(order.Id);

        // Assert: refused, the stock is as before and not negative, and the order stays a Draft
        await Assert.ThrowsAsync<BusinessRuleException>(act);
        Assert.Equal(stock, product.StockQuantity);
        Assert.True(product.StockQuantity >= 0);
        Assert.Empty(harness.Movements);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
    }

    [Fact]
    public async Task ApprovePurchaseOrderAsync_StockReachesExactlyTheLimitOfAnInt_IsAcceptedAsTheLastValidValue()
    {
        // Arrange: 1 in stock plus int.MaxValue - 1 is exactly int.MaxValue, the largest value that still fits
        var harness = new InventoryHarness();
        harness.AddSupplier();
        var product = harness.AddProduct(1, 1);
        var order = await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, int.MaxValue - 1)));

        // Act
        await harness.PurchaseOrderService.ApprovePurchaseOrderAsync(order.Id);

        // Assert
        Assert.Equal(int.MaxValue, product.StockQuantity);
    }

    // ---- duplicates ----

    [Theory]
    [InlineData(3, 4, 7)]
    [InlineData(1, 1, 2)]
    [InlineData(100, 1, 101)]
    public async Task ApprovePurchaseOrderAsync_SameProductOnTwoLines_AddsTheSumOnceEachLineWithItsOwnRow(int first, int second, int expectedIncrease)
    {
        // Arrange: the same product appears on two lines of one order (allowed; this is not a "duplicate lot")
        var harness = new InventoryHarness();
        harness.AddSupplier();
        var product = harness.AddProduct(1, 10);
        var order = await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, first), (1, second)));

        // Act
        await harness.PurchaseOrderService.ApprovePurchaseOrderAsync(order.Id);

        // Assert: both lines count, no more and no less; the ledger shows each line and the running stock
        Assert.Equal(10 + expectedIncrease, product.StockQuantity);
        Assert.Equal(new[] { first, second }, harness.Movements.Select(m => m.Quantity));
        Assert.Equal(new[] { 10 + first, 10 + expectedIncrease }, harness.Movements.Select(m => m.StockAfter));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public async Task CreatePurchaseOrderAsync_SeveralOrdersTheSameDay_NeverShareACode(int count)
    {
        // Arrange
        var harness = new InventoryHarness();
        harness.AddSupplier();
        harness.AddProduct(1, 0);

        // Act: create the orders one after another
        for (var i = 0; i < count; i++)
        {
            await harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(1, (1, 1)));
        }

        // Assert: every order has its own code, counting up
        var codes = harness.PurchaseOrders.Select(o => o.Code).ToList();
        Assert.Equal(count, codes.Distinct().Count());
        Assert.Equal(codes.OrderBy(c => c), codes);
        Assert.EndsWith("-" + count.ToString("000"), codes.Last()); // codes look like PO-yyyyMMdd-001
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_UnknownSupplier_ThrowsNotFoundAndSavesNothing()
    {
        // Arrange: no supplier with id 7
        var harness = new InventoryHarness();
        harness.AddProduct(1, 20);

        // Act
        var act = () => harness.PurchaseOrderService.CreatePurchaseOrderAsync(InventoryHarness.PurchaseOrderOf(7, (1, 5)));

        // Assert
        await Assert.ThrowsAsync<NotFoundException>(act);
        Assert.Empty(harness.PurchaseOrders);
    }
}
