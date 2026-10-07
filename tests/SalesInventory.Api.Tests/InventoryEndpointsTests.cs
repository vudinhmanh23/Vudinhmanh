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
    public async Task LowStock_ListsOnlyActiveProductsAtOrBelowReorderLevel_BiggestShortageFirst()
    {
        var small = SeedProduct(stock: 8, reorderLevel: 10);       // shortage 2
        var big = SeedProduct(stock: 0, reorderLevel: 10);         // shortage 10
        var exactly = SeedProduct(stock: 10, reorderLevel: 10);    // at the level counts, shortage 0
        var above = SeedProduct(stock: 11, reorderLevel: 10);      // above the level: not listed
        var noLevel = SeedProduct(stock: 0, reorderLevel: 0);      // reorder level 0 means "not tracked": not listed
        var inactive = SeedProduct(stock: 1, reorderLevel: 10, isActive: false);
        var client = await ClientAsync("BanHang");

        var response = await client.GetAsync("/api/products/low-stock");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<List<LowStockItemDto>>())!;
        var ours = items.Where(i => new[] { small, big, exactly, above, noLevel, inactive }.Contains(i.Id)).ToList();

        Assert.Equal(new[] { big, small, exactly }, ours.Select(i => i.Id)); // shortage 10, 2, 0
        Assert.Equal(new[] { 10, 2, 0 }, ours.Select(i => i.Shortage));
        Assert.All(ours, i => Assert.Equal(10, i.ReorderLevel));
        Assert.Equal(0, ours.Single(i => i.Id == big).StockQuantity);
        Assert.False(string.IsNullOrEmpty(ours[0].Name));
        Assert.False(string.IsNullOrEmpty(ours[0].Sku));
        // Whatever else is in the shared database must obey the same rules and the same ordering
        Assert.All(items, i => Assert.True(i.ReorderLevel > 0 && i.StockQuantity <= i.ReorderLevel));
        Assert.Equal(items.OrderByDescending(i => i.Shortage).Select(i => i.Shortage), items.Select(i => i.Shortage));
    }

    [Fact]
    public async Task LowStock_IncludeInactiveQuery_DefaultsToFalse_AndTrueAddsDiscontinuedProducts()
    {
        var active = SeedProduct(stock: 1, reorderLevel: 10);
        var inactive = SeedProduct(stock: 1, reorderLevel: 10, isActive: false);
        var client = await ClientAsync("Admin");

        var byDefault = (await client.GetFromJsonAsync<List<LowStockItemDto>>("/api/products/low-stock"))!;
        var explicitFalse = (await client.GetFromJsonAsync<List<LowStockItemDto>>("/api/products/low-stock?includeInactive=false"))!;
        var withInactive = (await client.GetFromJsonAsync<List<LowStockItemDto>>("/api/products/low-stock?includeInactive=true"))!;

        Assert.DoesNotContain(byDefault, i => i.Id == inactive);
        Assert.DoesNotContain(explicitFalse, i => i.Id == inactive);
        Assert.Contains(byDefault, i => i.Id == active);
        Assert.Contains(withInactive, i => i.Id == active);
        Assert.False(Assert.Single(withInactive, i => i.Id == inactive).IsActive);
        Assert.All(byDefault, i => Assert.True(i.IsActive));
    }

    [Fact]
    public async Task LowStock_ProductSoldDownToItsReorderLevel_AppearsInTheList()
    {
        var customerId = SeedCustomer();
        var productId = SeedProduct(stock: 9, reorderLevel: 5);
        var client = await ClientAsync("Admin");

        Assert.DoesNotContain((await client.GetFromJsonAsync<List<LowStockItemDto>>("/api/products/low-stock"))!, i => i.Id == productId);

        await PostOrderAsync(client, customerId, productId, DateTime.UtcNow, 6, 100m); // 3 left, below 5

        var item = Assert.Single((await client.GetFromJsonAsync<List<LowStockItemDto>>("/api/products/low-stock"))!, i => i.Id == productId);
        Assert.Equal(3, item.StockQuantity);
        Assert.Equal(2, item.Shortage);
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

    private int SeedProduct(int stock, int reorderLevel = 0, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product
        {
            Name = "Inventory Product",
            Sku = $"INV-{Guid.NewGuid():N}"[..20],
            Price = 100,
            StockQuantity = stock,
            ReorderLevel = reorderLevel,
            IsActive = isActive,
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
