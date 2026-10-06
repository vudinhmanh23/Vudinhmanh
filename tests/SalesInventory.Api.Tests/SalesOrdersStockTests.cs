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
        var (customerId, productId) = Seed(stock: 50);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, discount: 5000m, (productId, 4, 12500.5m)));

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
        var (customerId, productId) = Seed(stock: 50);
        var client = await AdminClientAsync();
        await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 4, 100m)));
        await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 6, 100m)));

        var response = await client.GetAsync($"/api/stock-movements?productId={productId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movements = await response.Content.ReadFromJsonAsync<List<StockMovementDto>>();
        Assert.Equal(2, movements!.Count);
        Assert.Equal(new[] { -6, -4 }, movements.Select(m => m.Quantity)); // newest first
        Assert.Equal(new[] { 40, 46 }, movements.Select(m => m.StockAfter));
    }

    [Fact]
    public async Task GetStockMovements_UnknownProduct_Returns404_AndMissingProductId_Returns400()
    {
        var client = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/stock-movements?productId=999999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stock-movements")).StatusCode);
    }

    [Fact]
    public async Task Create_WithNote_ReturnsNoteInResponse_AndOnGet()
    {
        var (customerId, productId) = Seed(stock: 10);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders", new
        {
            orderDate = DateTime.UtcNow,
            customerId,
            discountAmount = 0m,
            note = "Deliver after 5pm",
            items = new[] { new { productId, quantity = 1, unitPrice = 100m } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal("Deliver after 5pm", created!.Note);

        var fetched = await client.GetFromJsonAsync<OrderDto>($"/api/sales-orders/{created.Id}");
        Assert.Equal("Deliver after 5pm", fetched!.Note);
    }

    [Fact]
    public async Task Create_QuantityAboveStock_Returns409ProblemDetails_AndChangesNothing()
    {
        var (customerId, productId) = Seed(stock: 3);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 4, 100m)));

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
        var (customerId, okProduct) = Seed(stock: 10);
        var (_, shortProduct) = Seed(stock: 1);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders",
            Order(customerId, 0m, (okProduct, 2, 100m), (shortProduct, 2, 100m)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(10, StockOf(okProduct));
        Assert.Equal(1, StockOf(shortProduct));
        Assert.Empty(Movements(okProduct));
    }

    [Fact]
    public async Task Create_SameProductOnTwoLines_IsCheckedAgainstTheSum()
    {
        var (customerId, productId) = Seed(stock: 5);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders",
            Order(customerId, 0m, (productId, 3, 100m), (productId, 3, 100m)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(5, StockOf(productId));
    }

    [Fact]
    public async Task Create_LeavingStockBelowThreshold_AddsWarning_ButStillSucceeds()
    {
        var (customerId, productId) = Seed(stock: 8);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 4, 100m)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Single(order!.Warnings);
        Assert.Equal(4, StockOf(productId));
    }

    [Fact]
    public async Task Create_StockExactlyAtThreshold_AddsWarning_WithNameAndRemaining()
    {
        var (customerId, productId) = Seed(stock: 8);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 3, 100m)));

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
        var (customerId, productId) = Seed(stock: 8);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 2, 100m)));

        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Empty(order!.Warnings); // 6 left is above 5
        Assert.Empty(order.LowStockProducts);
    }

    [Fact]
    public async Task Create_DiscountLargerThanLines_Returns400_AndChangesNothing()
    {
        var (customerId, productId) = Seed(stock: 10);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 1000m, (productId, 1, 100m)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10, StockOf(productId));
        Assert.Empty(Movements(productId));
    }

    [Fact]
    public async Task Create_UnknownCustomer_Returns404()
    {
        var (_, productId) = Seed(stock: 10);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(987654, 0m, (productId, 1, 100m)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(10, StockOf(productId));
    }

    [Fact]
    public async Task Create_EmptyItems_Returns400ProblemDetails_WithVietnameseMessage_AndCreatesNothing()
    {
        var (customerId, _) = Seed(stock: 5);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders", new
        {
            orderDate = DateTime.UtcNow,
            customerId,
            discountAmount = 0m,
            items = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Đơn hàng phải có ít nhất một mặt hàng", await response.Content.ReadAsStringAsync());
        Assert.Empty(OrdersOf(customerId));
    }

    [Fact]
    public async Task Create_ZeroQuantity_Returns400()
    {
        var (customerId, productId) = Seed(stock: 10);
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sales-orders", Order(customerId, 0m, (productId, 0, 100m)));

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
