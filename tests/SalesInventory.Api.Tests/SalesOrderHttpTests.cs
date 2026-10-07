using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Api.Tests;

// Black-box integration tests of POST /api/sales-orders. The whole API runs in-process on the TestServer (real routing, JWT
// authentication, validation, services and EF Core on a real SQL Server in Docker, see TestDatabase), and every step goes over HttpClient:
// sign in, sell, and read the stock back with GET /api/products/{id}. No service or DbContext is touched by a test.
//
// Status codes of this API: a sale that is accepted answers 201 Created (it creates an order), and a sale of more than is in
// stock answers 409 Conflict with a ProblemDetails body that names the shortage (the request itself is valid; it conflicts with
// the current stock).
public class SalesOrderHttpTests : IClassFixture<SalesOrderHttpTests.SampleDataFactory>
{
    // Sample data seeded once with fixed ids. Each test uses its own product (and the customer with id = product id - 100),
    // so the tests share one running API without disturbing each other.
    private const int WashingMachine = 201;   // stock 10
    private const int Fridge = 202;           // stock 10
    private const int Fan = 203;              // stock 10
    private const int Kettle = 204;           // stock 10
    private const int Heater = 205;           // stock 3
    private const int Toaster = 206;          // stock 2
    private const int Blender = 207;          // stock 10
    private const int Iron = 208;             // stock 10

    private readonly SampleDataFactory _factory;

    public SalesOrderHttpTests(SampleDataFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(WashingMachine, 1)]
    [InlineData(Fridge, 3)]
    [InlineData(Fan, 10)]
    public async Task PostSalesOrder_QuantityWithinStock_Returns201AndStockDropsByExactlyThatQuantity(int productId, int quantity)
    {
        // Arrange
        var client = await SalesClientAsync();
        var stockBefore = await StockOfAsync(client, productId);

        // Act
        var response = await SellAsync(client, productId, quantity);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(10, stockBefore);
        Assert.Equal(stockBefore - quantity, await StockOfAsync(client, productId));
    }

    [Fact]
    public async Task PostSalesOrder_QuantityWithinStock_ReturnsTheOrderAndItShowsUpInTheCustomersOrders()
    {
        // Arrange
        var client = await SalesClientAsync();

        // Act
        var response = await SellAsync(client, Kettle, 4);

        // Assert: the body describes the order that was just made...
        var created = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith("SO-", created!.OrderNumber);
        Assert.Equal(4 * UnitPrice, created.TotalAmount);

        // ...and the same order can be read back over HTTP
        var orders = await client.GetFromJsonAsync<List<OrderDto>>($"/api/customers/{CustomerOf(Kettle)}/orders");
        var listed = Assert.Single(orders!);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(4, Assert.Single(listed.Items).Quantity);
    }

    [Fact]
    public async Task PostSalesOrder_QuantityAboveStock_Returns409WithTheShortageAndStockIsUnchanged()
    {
        // Arrange: 3 in stock, the customer wants 4
        var client = await SalesClientAsync();

        // Act
        var response = await SellAsync(client, Heater, 4);

        // Assert: refused, with the numbers in a ProblemDetails body
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("cần 4, còn 3, thiếu 1", await response.Content.ReadAsStringAsync());

        // Nothing was sold: the stock is untouched, never negative, and no order exists
        Assert.Equal(3, await StockOfAsync(client, Heater));
        Assert.Empty((await client.GetFromJsonAsync<List<OrderDto>>($"/api/customers/{CustomerOf(Heater)}/orders"))!);
    }

    [Fact]
    public async Task PostSalesOrder_SellingTheLastUnitsThenOneMore_SecondSaleIsRejectedAndStockStaysAtZero()
    {
        // Arrange: 2 in stock
        var client = await SalesClientAsync();

        // Act
        var first = await SellAsync(client, Toaster, 2);
        var second = await SellAsync(client, Toaster, 1);

        // Assert
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(0, await StockOfAsync(client, Toaster));
    }

    [Fact]
    public async Task PostSalesOrder_SeveralSalesInARow_EachOneLowersTheStockFurtherUntilItWouldGoNegative()
    {
        // Arrange: 10 in stock
        var client = await SalesClientAsync();

        // Act and assert, step by step
        Assert.Equal(HttpStatusCode.Created, (await SellAsync(client, Blender, 4)).StatusCode);
        Assert.Equal(6, await StockOfAsync(client, Blender));

        Assert.Equal(HttpStatusCode.Created, (await SellAsync(client, Blender, 5)).StatusCode);
        Assert.Equal(1, await StockOfAsync(client, Blender));

        Assert.Equal(HttpStatusCode.Conflict, (await SellAsync(client, Blender, 2)).StatusCode);
        Assert.Equal(1, await StockOfAsync(client, Blender));
    }

    [Fact]
    public async Task PostSalesOrder_NegativeQuantity_Returns400AndCannotBeUsedToAddStock()
    {
        // Arrange
        var client = await SalesClientAsync();

        // Act
        var response = await SellAsync(client, Iron, -5);

        // Assert: a malformed request is a 400, and it must not raise the stock
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10, await StockOfAsync(client, Iron));
    }

    [Fact]
    public async Task PostSalesOrder_WithoutAToken_Returns401AndNothingIsSold()
    {
        // Arrange: a client that never signed in
        var anonymous = _factory.CreateClient();
        var signedIn = await SalesClientAsync();
        var stockBefore = await StockOfAsync(signedIn, Iron);

        // Act
        var response = await SellAsync(anonymous, Iron, 1);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(stockBefore, await StockOfAsync(signedIn, Iron));
    }

    // ---- helpers: everything goes over HTTP ----

    private const decimal UnitPrice = 25000m;

    private static int CustomerOf(int productId) => productId - 100;

    private async Task<HttpClient> SalesClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> SellAsync(HttpClient client, int productId, int quantity)
    {
        return client.PostAsJsonAsync("/api/sales-orders", new
        {
            orderDate = DateTime.UtcNow,
            customerId = CustomerOf(productId),
            discountAmount = 0m,
            items = new[] { new { productId, quantity, unitPrice = UnitPrice } }
        });
    }

    // The stock the API reports for a product (the "quantity" of GET /api/products/{id})
    private static async Task<int> StockOfAsync(HttpClient client, int productId)
    {
        var product = await client.GetFromJsonAsync<ProductDto>($"/api/products/{productId}");
        return product!.Quantity;
    }

    // The standard test API plus a few sample products and customers with known stock, seeded before the first request
    public sealed class SampleDataFactory : CustomWebApplicationFactory
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);

            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var samples = new (int Id, string Name, int Stock)[]
            {
                (WashingMachine, "Máy giặt ABC", 10), (Fridge, "Tủ lạnh XYZ", 10), (Fan, "Quạt đứng", 10), (Kettle, "Ấm siêu tốc", 10),
                (Heater, "Máy sưởi", 3), (Toaster, "Máy nướng bánh", 2), (Blender, "Máy xay sinh tố", 10), (Iron, "Bàn ủi", 10)
            };

            foreach (var (id, name, stock) in samples)
            {
                TestDatabase.AddWithKey(db, new Customer { Id = CustomerOf(id), Name = $"Khách hàng {id}" });
                TestDatabase.AddWithKey(db, new Product
                {
                    Id = id,
                    Name = name,
                    Sku = $"SAMPLE-{id}",
                    Price = UnitPrice,
                    SalePrice = UnitPrice,
                    StockQuantity = stock,
                    CategoryId = 1,
                    SupplierId = 1,
                    CreatedAt = DateTime.UtcNow
                });
            }

            return host;
        }
    }
}
