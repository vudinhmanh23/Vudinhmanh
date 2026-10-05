using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Api.Tests;

// Customer order history, low-stock listing and manual stock adjustment endpoints
public class InventoryEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public InventoryEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ---- GET /api/customers/{id}/orders ----

    [Fact]
    public async Task CustomerOrders_ReturnsOnlyThatCustomersOrders_NewestFirst_WithTotals()
    {
        var customerId = SeedCustomer();
        var otherCustomerId = SeedCustomer();
        var productId = SeedProduct(stock: 100);
        var client = await ClientAsync("Admin");

        await PostOrderAsync(client, customerId, productId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, 1000m);
        await PostOrderAsync(client, customerId, productId, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), 2, 1000m);
        await PostOrderAsync(client, otherCustomerId, productId, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 1, 1000m);

        var orders = await client.GetFromJsonAsync<List<OrderDto>>($"/api/customers/{customerId}/orders");

        Assert.Equal(2, orders!.Count);
        Assert.All(orders, o => Assert.Equal(customerId, o.CustomerId));
        Assert.Equal(new[] { 2000m, 1000m }, orders.Select(o => o.TotalAmount)); // March order first
    }

    [Fact]
    public async Task CustomerOrders_CustomerWithoutOrders_ReturnsEmptyList()
    {
        var client = await ClientAsync("Admin");

        var response = await client.GetAsync($"/api/customers/{SeedCustomer()}/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<OrderDto>>())!);
    }

    [Fact]
    public async Task CustomerOrders_UnknownCustomer_Returns404()
    {
        var client = await ClientAsync("Admin");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/customers/987654/orders")).StatusCode);
    }

    // ---- GET /api/products/low-stock ----

    [Fact]
    public async Task LowStock_ListsOnlyProductsBelowThreshold_AndAProductDroppingBelowItAppears()
    {
        var customerId = SeedCustomer();
        var below = SeedProduct(stock: 3);
        var atThreshold = SeedProduct(stock: 5);   // default threshold is 5: "< 5", so 5 is not low
        var willDrop = SeedProduct(stock: 9);
        var client = await ClientAsync("BanHang");

        var before = await client.GetFromJsonAsync<List<ProductDto>>("/api/products/low-stock");
        Assert.Contains(before!, p => p.Id == below);
        Assert.DoesNotContain(before!, p => p.Id == atThreshold);
        Assert.DoesNotContain(before!, p => p.Id == willDrop);

        // Sell 6 of 9 -> 3 left, now below the threshold
        var admin = await ClientAsync("Admin");
        await PostOrderAsync(admin, customerId, willDrop, DateTime.UtcNow, 6, 100m);

        var after = await client.GetFromJsonAsync<List<ProductDto>>("/api/products/low-stock");
        Assert.Contains(after!, p => p.Id == willDrop && p.Quantity == 3);
        Assert.All(after!, p => Assert.True(p.Quantity < 5));
    }

    [Fact]
    public async Task LowStock_ThresholdComesFromConfiguration()
    {
        var productId = SeedProduct(stock: 15);
        // Same app and database, but Inventory:LowStockThreshold overridden to 20 instead of the default 5
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Inventory:LowStockThreshold", "20"));
        var client = factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var low = await client.GetFromJsonAsync<List<ProductDto>>("/api/products/low-stock");

        // 15 is not low under the default (5) but is under 20, so seeing it proves the configured value is used
        Assert.Contains(low!, p => p.Id == productId);
    }

    // ---- POST /api/products/{id}/adjust-stock ----

    [Fact]
    public async Task AdjustStock_Increase_UpdatesStock_AndLogsAdjustmentMovement()
    {
        var productId = SeedProduct(stock: 10);
        var client = await ClientAsync("Kho");

        var response = await client.PostAsJsonAsync($"/api/products/{productId}/adjust-stock", new { delta = 4, reason = "Stocktake: found 4 extra" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StockAdjustmentDto>();
        Assert.Equal(10, result!.PreviousQuantity);
        Assert.Equal(14, result.NewQuantity);
        Assert.Equal("Adjustment", result.Movement.MovementType);
        Assert.Equal("Stocktake: found 4 extra", result.Movement.Note);
        Assert.Equal(14, StockOf(productId));
    }

    [Fact]
    public async Task AdjustStock_BelowZero_Returns409ProblemDetails_AndStockIsUnchanged()
    {
        var productId = SeedProduct(stock: 10);
        var client = await ClientAsync("Admin");

        var response = await client.PostAsJsonAsync($"/api/products/{productId}/adjust-stock", new { delta = -11, reason = "Too many" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(10, StockOf(productId));
    }

    [Theory]
    [InlineData(0, "reason")]
    [InlineData(3, "")]
    public async Task AdjustStock_ZeroDeltaOrMissingReason_Returns400(int delta, string reason)
    {
        var productId = SeedProduct(stock: 10);
        var client = await ClientAsync("Admin");

        var response = await client.PostAsJsonAsync($"/api/products/{productId}/adjust-stock", new { delta, reason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10, StockOf(productId));
    }

    [Fact]
    public async Task AdjustStock_AsBanHang_Returns403()
    {
        var productId = SeedProduct(stock: 10);
        var client = await ClientAsync("BanHang");

        var response = await client.PostAsJsonAsync($"/api/products/{productId}/adjust-stock", new { delta = 1, reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- helpers ----

    private static async Task PostOrderAsync(HttpClient client, int customerId, int productId, DateTime date, int quantity, decimal unitPrice)
    {
        var response = await client.PostAsJsonAsync("/api/sales-orders", new
        {
            orderDate = date,
            customerId,
            items = new[] { new { productId, quantity, unitPrice } }
        });
        response.EnsureSuccessStatusCode();
    }

    private int SeedCustomer()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer { Name = "Buyer" };
        db.Customers.Add(customer);
        db.SaveChanges();
        return customer.Id;
    }

    private int SeedProduct(int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product
        {
            Name = "Inventory Product",
            Sku = $"INV-{Guid.NewGuid():N}"[..20],
            Price = 100,
            StockQuantity = stock,
            CategoryId = 1,
            SupplierId = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.Products.Add(product);
        db.SaveChanges();
        return product.Id;
    }

    private int StockOf(int productId)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Products.Single(p => p.Id == productId).StockQuantity;
    }

    private async Task<HttpClient> ClientAsync(string role)
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
