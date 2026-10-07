using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Api.Tests;

// Sales order creation: totals, stock deduction, Sale movements, 400 on oversell, low-stock warnings
public class SalesOrdersStockTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SalesOrdersStockTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_ValidOrder_DeductsStock_LogsSaleMovement_AndComputesTotal()
    {
        // Arrange: a product with 50 in stock and a signed-in admin
        var (customerId, productId) = Seed(stock: 50);
        var client = await AdminClientAsync();

        // Act: sell 4 units at 12,500.50 with a 5,000 discount
        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, discount: 5000m, (productId, 4, 12500.5m)));

        // Assert: created; the server computed the totals; stock fell to 46; exactly one Sale row in the ledger
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        // 4 * 12500.50 = 50002.00, minus 5000 discount
        Assert.Equal(50002.00m, order!.Items.Single().LineTotal);
        Assert.Equal(45002.00m, order.TotalAmount);
        Assert.Empty(order.Warnings);

        Assert.Equal(46, StockOf(productId));
        var movement = Assert.Single(Movements(productId));
        Assert.Equal(StockMovementType.Sale, movement.MovementType);
        Assert.Equal(-4, movement.Quantity);
        Assert.Equal(order.Id, movement.RefId);
        Assert.Equal(46, movement.StockAfter);
        Assert.Equal(order.OrderNumber, movement.Reference);
    }

    [Fact]
    public async Task GetStockMovements_ByProductId_ReturnsHistoryNewestFirst()
    {
        // Arrange: a product with 50 in stock, sold twice (4, then 6 units)
        var (customerId, productId) = Seed(stock: 50);
        var client = await AdminClientAsync();
        await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 4, 100m)));
        await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 6, 100m)));

        // Act: ask for the stock history of the product
        var response = await client.GetAsync($"/api/stock-movements?productId={productId}");

        // Assert: 200 OK; two rows, newest first, each with the stock left after it
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movements = await response.Content.ReadFromJsonAsync<List<StockMovementDto>>();
        Assert.Equal(2, movements!.Count);
        Assert.Equal(new[] { -6, -4 }, movements.Select(m => m.Quantity)); // newest first
        Assert.Equal(new[] { 40, 46 }, movements.Select(m => m.StockAfter));
    }

    [Fact]
    public async Task GetStockMovements_UnknownProduct_Returns404_AndMissingProductId_Returns400()
    {
        // Arrange: a signed-in admin (no data needed)
        var client = await AdminClientAsync();

        // Act and assert: an unknown product is 404 and a missing productId is 400
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/stock-movements?productId=999999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stock-movements")).StatusCode);
    }

    [Fact]
    public async Task Create_WithNote_ReturnsNoteInResponse_AndOnGet()
    {
        // Arrange: a product with 10 in stock and an order that carries a note
        var (customerId, productId) = Seed(stock: 10);
        var client = await AdminClientAsync();

        // Act: create the order
        var response = await client.PostAsJsonAsync("/api/sales-orders", new
        {
            orderDate = DateTime.UtcNow,
            customerId,
            discountAmount = 0m,
            note = "Deliver after 5pm",
            items = new[] { new { productId, quantity = 1, unitPrice = 100m } }
        });

        // Assert: created, and the note comes back in the response
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal("Deliver after 5pm", created!.Note);

        // ...and read the order back with a second request to check the note was saved
        var fetched = await client.GetFromJsonAsync<OrderDto>($"/api/sales-orders/{created.Id}");
        Assert.Equal("Deliver after 5pm", fetched!.Note);
    }

    [Fact]
    public async Task Create_QuantityAboveStock_Returns409ProblemDetails_AndChangesNothing()
    {
        // Arrange: 3 in stock, a customer who wants 4
        var (customerId, productId) = Seed(stock: 3);
        var client = await AdminClientAsync();

        // Act: try to sell 4
        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 4, 100m)));

        // Assert: 409 with a ProblemDetails body naming the shortage; stock, ledger and orders are all untouched
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Không đủ tồn kho cho sản phẩm", body);
        Assert.Contains("cần 4, còn 3, thiếu 1", body);
        Assert.Equal(3, StockOf(productId));
        Assert.Empty(Movements(productId));
        Assert.Empty(OrdersOf(customerId));
    }

    [Fact]
    public async Task Create_OneLineFine_OneLineOversold_Returns409_AndNeitherProductChanges()
    {
        // Arrange: one product with enough stock and one that is short
        var (customerId, okProduct) = Seed(stock: 10);
        var (_, shortProduct) = Seed(stock: 1);
        var client = await AdminClientAsync();

        // Act: send one order with a line for each
        var response = await client.PostAsJsonAsync("/api/sales-orders",
            Order(customerId, 0m, (okProduct, 2, 100m), (shortProduct, 2, 100m)));

        // Assert: the whole order is refused (409), so even the product that was fine keeps its stock
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(10, StockOf(okProduct));
        Assert.Equal(1, StockOf(shortProduct));
        Assert.Empty(Movements(okProduct));
    }

    [Fact]
    public async Task Create_SameProductOnTwoLines_IsCheckedAgainstTheSum()
    {
        // Arrange: 5 in stock; two lines of 3 each fit alone but not together
        var (customerId, productId) = Seed(stock: 5);
        var client = await AdminClientAsync();

        // Act: send the order with the product on both lines
        var response = await client.PostAsJsonAsync("/api/sales-orders",
            Order(customerId, 0m, (productId, 3, 100m), (productId, 3, 100m)));

        // Assert: refused (409): the lines are summed (6 > 5); stock stays at 5
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(5, StockOf(productId));
    }

    [Fact]
    public async Task Create_LeavingStockBelowThreshold_AddsWarning_ButStillSucceeds()
    {
        // Arrange: 8 in stock; selling 4 leaves 4, below the default threshold of 5
        var (customerId, productId) = Seed(stock: 8);
        var client = await AdminClientAsync();

        // Act: sell 4
        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 4, 100m)));

        // Assert: the sale succeeds, with one low-stock warning; stock is 4
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Single(order!.Warnings);
        Assert.Equal(4, StockOf(productId));
    }

    [Fact]
    public async Task Create_StockExactlyAtThreshold_AddsWarning_WithNameAndRemaining()
    {
        // Arrange: 8 in stock; selling 3 leaves exactly 5, the threshold
        var (customerId, productId) = Seed(stock: 8);
        var client = await AdminClientAsync();

        // Act: sell 3
        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 3, 100m)));

        // Assert: created, with a warning that names the product and what is left (the boundary counts as low)
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        var low = Assert.Single(order!.LowStockProducts); // 5 left is <= the default threshold of 5
        Assert.Equal(productId, low.ProductId);
        Assert.Equal(5, low.StockQuantity);
        Assert.Equal(5, low.LowStockThreshold);
        Assert.False(string.IsNullOrEmpty(low.ProductName));
    }

    [Fact]
    public async Task Create_StockAboveThreshold_NoWarning()
    {
        // Arrange: 8 in stock; selling 2 leaves 6, above the threshold
        var (customerId, productId) = Seed(stock: 8);
        var client = await AdminClientAsync();

        // Act: sell 2
        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 2, 100m)));

        // Assert: no warning and no low-stock products
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Empty(order!.Warnings); // 6 left is above 5
        Assert.Empty(order.LowStockProducts);
    }

    [Fact]
    public async Task Create_DiscountLargerThanLines_Returns400_AndChangesNothing()
    {
        // Arrange: a discount (1,000) larger than the order lines (100)
        var (customerId, productId) = Seed(stock: 10);
        var client = await AdminClientAsync();

        // Act: send the order
        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 1000m, (productId, 1, 100m)));

        // Assert: 400 Bad Request; the stock and the ledger are untouched
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10, StockOf(productId));
        Assert.Empty(Movements(productId));
    }

    [Fact]
    public async Task Create_UnknownCustomer_Returns404()
    {
        // Arrange: a product with stock, but a customer id that does not exist
        var (_, productId) = Seed(stock: 10);
        var client = await AdminClientAsync();

        // Act: send the order
        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(987654, 0m, (productId, 1, 100m)));

        // Assert: 404 Not Found; the stock is untouched
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(10, StockOf(productId));
    }

    [Fact]
    public async Task Create_EmptyItems_Returns400ProblemDetails_WithVietnameseMessage_AndCreatesNothing()
    {
        // Arrange: an order with no lines at all
        var (customerId, _) = Seed(stock: 5);
        var client = await AdminClientAsync();

        // Act: send the order
        var response = await client.PostAsJsonAsync("/api/sales-orders", new
        {
            orderDate = DateTime.UtcNow,
            customerId,
            discountAmount = 0m,
            items = Array.Empty<object>()
        });

        // Assert: 400 with the Vietnamese message; no order was created
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Đơn hàng phải có ít nhất một mặt hàng", await response.Content.ReadAsStringAsync());
        Assert.Empty(OrdersOf(customerId));
    }

    [Fact]
    public async Task Create_ZeroQuantity_Returns400()
    {
        // Arrange: an order line with quantity 0
        var (customerId, productId) = Seed(stock: 10);
        var client = await AdminClientAsync();

        // Act: send the order
        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 0, 100m)));

        // Assert: 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static object Order(int customerId, decimal discount, params (int ProductId, int Quantity, decimal UnitPrice)[] lines) => new
    {
        orderDate = DateTime.UtcNow,
        customerId,
        discountAmount = discount,
        items = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity, unitPrice = l.UnitPrice }).ToArray()
    };

    // Each test gets its own customer and product so tests never interfere with each other
    private (int CustomerId, int ProductId) Seed(int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer { Name = "Buyer" };
        var product = new Product
        {
            Name = "Sale Product",
            Sku = $"SALE-{Guid.NewGuid():N}"[..20],
            Price = 100,
            StockQuantity = stock,
            CategoryId = 1,
            SupplierId = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        db.Products.Add(product);
        db.SaveChanges();
        return (customer.Id, product.Id);
    }

    private int StockOf(int productId)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Products.Single(p => p.Id == productId).StockQuantity;
    }

    private List<StockMovement> Movements(int productId)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().StockMovements.Where(m => m.ProductId == productId).ToList();
    }

    private List<SalesOrder> OrdersOf(int customerId)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().SalesOrders.Where(o => o.CustomerId == customerId).ToList();
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
