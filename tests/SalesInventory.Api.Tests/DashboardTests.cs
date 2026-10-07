using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Api.Tests;

// GET /api/dashboard/summary and /low-stock-items: aggregates, period handling and access control
public class DashboardTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DashboardTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientAsync(string role = "Kho")
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<DashboardSummaryDto> GetSummaryAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/dashboard/summary{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DashboardSummaryDto>())!;
    }

    // Orders are written straight to the database with dates in 2001 so no other test's data falls in the period
    private void SeedOrders(params (DateTime Date, decimal Total, SalesOrderStatus Status)[] orders)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer { Name = "Dashboard buyer" };
        db.Customers.Add(customer);
        db.SaveChanges();

        foreach (var (date, total, status) in orders)
        {
            db.SalesOrders.Add(new SalesOrder
            {
                OrderNumber = $"SO-DB-{Guid.NewGuid():N}"[..28],
                OrderDate = date,
                CustomerId = customer.Id,
                TotalAmount = total,
                Status = status
            });
        }

        db.SaveChanges();
    }

    private void SeedProduct(string name, int stock, decimal cost, int threshold, bool active)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Products.Add(new Product
        {
            Name = name,
            Sku = $"DB-{Guid.NewGuid():N}"[..20],
            Price = 100,
            SalePrice = 100,
            PurchasePrice = cost,
            StockQuantity = stock,
            LowStockThreshold = threshold,
            IsActive = active,
            CategoryId = 1,
            SupplierId = 1,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Summary_CountsOnlyCompletedOrders_InsideThePeriod()
    {
        SeedOrders(
            (new DateTime(2001, 3, 10, 10, 0, 0), 1000m, SalesOrderStatus.Completed),
            (new DateTime(2001, 3, 20, 15, 30, 0), 500.50m, SalesOrderStatus.Completed),
            (new DateTime(2001, 3, 12, 9, 0, 0), 9999m, SalesOrderStatus.Cancelled),
            (new DateTime(2001, 4, 5, 9, 0, 0), 700m, SalesOrderStatus.Completed));
        var client = await ClientAsync();

        var march = await GetSummaryAsync(client, "?from=2001-03-01&to=2001-03-31");

        Assert.Equal(1500.50m, march.TotalRevenue);
        Assert.Equal(2, march.OrderCount);

        // A date-only "to" covers the whole day, so the order at 15:30 on 20 March is included
        var lastDay = await GetSummaryAsync(client, "?from=2001-03-15&to=2001-03-20");
        Assert.Equal(500.50m, lastDay.TotalRevenue);
        Assert.Equal(1, lastDay.OrderCount);

        // Only a lower bound: March and April both count
        var fromMarch = await GetSummaryAsync(client, "?from=2001-03-01&to=2001-12-31");
        Assert.Equal(2200.50m, fromMarch.TotalRevenue);
    }

    [Fact]
    public async Task Summary_AverageOrderValue_IsRevenueOverOrders_AndZeroWhenNoOrders()
    {
        // Own year (2003) so the figures cannot mix with other tests: 1000 + 500.50 over 2 orders
        SeedOrders(
            (new DateTime(2003, 3, 10, 10, 0, 0), 1000m, SalesOrderStatus.Completed),
            (new DateTime(2003, 3, 20, 15, 30, 0), 500.50m, SalesOrderStatus.Completed));
        var client = await ClientAsync();

        var withOrders = await GetSummaryAsync(client, "?from=2003-03-01&to=2003-03-31");
        var empty = await GetSummaryAsync(client, "?from=1990-01-01&to=1990-01-31");

        Assert.Equal(750.25m, withOrders.AverageOrderValue);   // 1500.50 / 2 by hand
        Assert.Equal(0m, empty.AverageOrderValue);             // OrderCount = 0, no division by zero
    }

    [Fact]
    public async Task Summary_PreviousPeriod_IsTheSameLengthRightBefore_WithoutOverlapOrGap()
    {
        // Current period 11-20 June 2002 inclusive = 10 days, so the previous one is 1-10 June
        SeedOrders(
            (new DateTime(2002, 5, 31, 23, 59, 59), 888m, SalesOrderStatus.Completed),   // before the previous period
            (new DateTime(2002, 6, 5, 12, 0, 0), 400m, SalesOrderStatus.Completed),      // previous
            (new DateTime(2002, 6, 10, 23, 59, 59), 100m, SalesOrderStatus.Completed),   // previous, last second
            (new DateTime(2002, 6, 11, 0, 0, 0), 1000m, SalesOrderStatus.Completed),     // current, first second
            (new DateTime(2002, 6, 20, 23, 0, 0), 500m, SalesOrderStatus.Completed),     // current, last day
            (new DateTime(2002, 6, 21, 0, 0, 0), 777m, SalesOrderStatus.Completed),      // after the current period
            (new DateTime(2002, 6, 7, 9, 0, 0), 5000m, SalesOrderStatus.Cancelled));     // cancelled never counts
        var client = await ClientAsync();

        var summary = await GetSummaryAsync(client, "?from=2002-06-11&to=2002-06-20");

        Assert.Equal(1500m, summary.TotalRevenue);
        Assert.Equal(500m, summary.PreviousPeriodRevenue);      // 400 + 100
        Assert.Equal(200.0m, summary.RevenueChangePercent);     // (1500 - 500) / 500 x 100 by hand
    }

    [Fact]
    public async Task Summary_RevenueDrop_GivesNegativePercent()
    {
        // Current 1-10 Aug 2002 earns 300, the 10 days before earn 400: -25%
        SeedOrders(
            (new DateTime(2002, 7, 25, 10, 0, 0), 400m, SalesOrderStatus.Completed),
            (new DateTime(2002, 8, 5, 10, 0, 0), 300m, SalesOrderStatus.Completed));
        var client = await ClientAsync();

        var summary = await GetSummaryAsync(client, "?from=2002-08-01&to=2002-08-10");

        Assert.Equal(-25.0m, summary.RevenueChangePercent);
    }

    [Fact]
    public async Task Summary_PreviousPeriodMissingOrZero_GivesNullPercent()
    {
        SeedOrders((new DateTime(2002, 10, 5, 10, 0, 0), 300m, SalesOrderStatus.Completed));
        var client = await ClientAsync();

        // Nothing in the 10 days before: previous revenue is 0, so a percentage is undefined
        var noPreviousSales = await GetSummaryAsync(client, "?from=2002-10-01&to=2002-10-10");
        Assert.Equal(0m, noPreviousSales.PreviousPeriodRevenue);
        Assert.Null(noPreviousSales.RevenueChangePercent);

        // Without "to" (or "from") there is no previous period at all
        var openEnded = await GetSummaryAsync(client, "?from=2002-10-01");
        Assert.Null(openEnded.PreviousPeriodRevenue);
        Assert.Null(openEnded.RevenueChangePercent);
    }

    [Fact]
    public async Task Summary_EmptyPeriod_ReturnsZeroRevenueAndOrders()
    {
        var client = await ClientAsync();

        var summary = await GetSummaryAsync(client, "?from=1990-01-01&to=1990-01-31");

        Assert.Equal(0m, summary.TotalRevenue);
        Assert.Equal(0, summary.OrderCount);
    }

    [Fact]
    public async Task Summary_InventoryValueAndLowStock_FollowStockCostAndThreshold()
    {
        var client = await ClientAsync();
        var before = await GetSummaryAsync(client);

        SeedProduct("Dash low", stock: 2, cost: 10m, threshold: 5, active: true);          // low, value 20
        SeedProduct("Dash plenty", stock: 100, cost: 3m, threshold: 5, active: true);      // fine, value 300
        SeedProduct("Dash retired", stock: 1, cost: 7m, threshold: 5, active: false);      // inactive: value 7, never an alert

        var after = await GetSummaryAsync(client);

        Assert.Equal(before.InventoryValue + 327m, after.InventoryValue);
        Assert.Equal(before.LowStockCount + 1, after.LowStockCount);
    }

    [Fact]
    public async Task LowStockItems_ListsActiveProductsAtOrBelowThreshold_LowestFirst()
    {
        var tag = $"Alert{Guid.NewGuid():N}"[..12];
        SeedProduct($"{tag}-empty", stock: 0, cost: 1m, threshold: 5, active: true);
        SeedProduct($"{tag}-few", stock: 3, cost: 1m, threshold: 5, active: true);
        SeedProduct($"{tag}-ok", stock: 50, cost: 1m, threshold: 5, active: true);
        SeedProduct($"{tag}-retired", stock: 0, cost: 1m, threshold: 5, active: false);
        var client = await ClientAsync();

        var response = await client.GetAsync("/api/dashboard/low-stock-items?limit=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<List<DashboardLowStockItemDto>>())!;
        Assert.All(items, i => Assert.True(i.StockQuantity <= i.LowStockThreshold));
        Assert.Equal(items.Select(i => i.StockQuantity).OrderBy(x => x), items.Select(i => i.StockQuantity));

        var mine = items.Where(i => i.Name.StartsWith(tag)).Select(i => i.Name).ToList();
        Assert.Equal(new[] { $"{tag}-empty", $"{tag}-few" }, mine);
    }

    [Fact]
    public async Task LowStockItems_RespectsLimit()
    {
        SeedProduct("Limit a", stock: 0, cost: 1m, threshold: 5, active: true);
        SeedProduct("Limit b", stock: 0, cost: 1m, threshold: 5, active: true);
        var client = await ClientAsync();

        var response = await client.GetAsync("/api/dashboard/low-stock-items?limit=1");

        Assert.Single((await response.Content.ReadFromJsonAsync<List<DashboardLowStockItemDto>>())!);
    }

    [Fact]
    public async Task Summary_FromAfterTo_Returns400()
    {
        var client = await ClientAsync();

        var response = await client.GetAsync("/api/dashboard/summary?from=2001-03-31&to=2001-03-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Summary_NoToken_Returns401_AndSalesRole_Returns403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/dashboard/summary")).StatusCode);

        var sales = await ClientAsync("BanHang");
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync("/api/dashboard/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync("/api/dashboard/low-stock-items")).StatusCode);
    }

    [Fact]
    public async Task Swagger_ListsSummaryEndpointWithFromAndTo()
    {
        var json = await _factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        var compact = json.Replace("\": ", "\":");

        Assert.Contains("\"/api/dashboard/summary\"", compact);
        Assert.Contains("\"name\":\"from\"", compact);
        Assert.Contains("\"name\":\"to\"", compact);
    }
}
