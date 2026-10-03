using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Api.Tests;

// Stock-in business rules: server-side totals, stock increase, stock movements, validation, 404
public class PurchaseOrdersTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PurchaseOrdersTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Each test gets its own product so stock assertions don't interfere with each other
    private async Task<int> AddProductAsync(int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product
        {
            Name = "PO test product",
            Sku = $"PO-{Guid.NewGuid():N}"[..20],
            Price = 1000,
            StockQuantity = stock,
            CategoryId = 1,
            SupplierId = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task<int> GetStockAsync(int productId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).StockQuantity;
    }

    // Errors must be RFC 7807 ProblemDetails (application/problem+json) with a readable message, never a raw exception
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string? messagePart = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains($"\"status\":{(int)status}", body);
        if (messagePart is not null)
        {
            Assert.Contains(messagePart, body);
        }
    }

    private static CreatePurchaseOrderDto Dto(params (int productId, int quantity, decimal unitPrice)[] lines) => new()
    {
        OrderDate = DateTime.UtcNow,
        SupplierId = 1,
        Note = "test",
        Items = lines.Select(l => new CreatePurchaseOrderItemDto
        {
            ProductId = l.productId,
            Quantity = l.quantity,
            UnitPrice = l.unitPrice
        }).ToList()
    };

    [Fact]
    public async Task Create_ComputesTotalsIncreasesStockAndLogsMovements()
    {
        var client = await AdminClientAsync();
        var p1 = await AddProductAsync(stock: 10);
        var p2 = await AddProductAsync(stock: 5);

        var response = await client.PostAsJsonAsync("/api/purchase-orders", Dto((p1, 3, 100m), (p2, 2, 250.50m)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PurchaseOrderDto>();
        Assert.NotNull(created);
        Assert.Equal(300m + 501m, created!.TotalAmount);
        Assert.Equal(new[] { 300m, 501m }, created.Items.Select(i => i.LineTotal).OrderBy(x => x));

        Assert.Equal(13, await GetStockAsync(p1));
        Assert.Equal(7, await GetStockAsync(p2));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movements = await db.StockMovements.Where(m => m.RefId == created.Id).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m => Assert.Equal("Purchase", m.Reason));
        Assert.Equal(3, movements.Single(m => m.ProductId == p1).ChangeQuantity);
        Assert.Equal(2, movements.Single(m => m.ProductId == p2).ChangeQuantity);
    }

    [Fact]
    public async Task Create_GeneratesSequentialCodePerDay()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 0);

        var first = await (await client.PostAsJsonAsync("/api/purchase-orders", Dto((p, 1, 1m)))).Content.ReadFromJsonAsync<PurchaseOrderDto>();
        var second = await (await client.PostAsJsonAsync("/api/purchase-orders", Dto((p, 1, 1m)))).Content.ReadFromJsonAsync<PurchaseOrderDto>();

        var prefix = $"PO-{DateTime.UtcNow:yyyyMMdd}-";
        Assert.StartsWith(prefix, first!.Code);
        Assert.Matches(@"^PO-\d{8}-\d{3,}$", first.Code);
        Assert.Equal(int.Parse(first.Code[prefix.Length..]) + 1, int.Parse(second!.Code[prefix.Length..]));
    }

    [Fact]
    public async Task Create_SameProductOnTwoLines_AccumulatesStock()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 0);

        var response = await client.PostAsJsonAsync("/api/purchase-orders", Dto((p, 4, 10m), (p, 6, 10m)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(10, await GetStockAsync(p));
    }

    [Fact]
    public async Task Create_ClientSuppliedTotalsAreIgnored()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 0);

        // Extra JSON properties the DTO doesn't declare must not influence the stored totals
        var body = new
        {
            orderDate = DateTime.UtcNow,
            supplierId = 1,
            totalAmount = 1m,
            items = new[] { new { productId = p, quantity = 2, unitPrice = 50m, lineTotal = 1m } }
        };
        var response = await client.PostAsJsonAsync("/api/purchase-orders", body);

        var created = await response.Content.ReadFromJsonAsync<PurchaseOrderDto>();
        Assert.Equal(100m, created!.TotalAmount);
        Assert.Equal(100m, created.Items.Single().LineTotal);
    }

    [Fact]
    public async Task Create_UnknownSupplier_Returns400AndChangesNothing()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);
        var dto = Dto((p, 5, 10m));
        dto.SupplierId = 99999;

        var response = await client.PostAsJsonAsync("/api/purchase-orders", dto);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "Supplier with Id 99999");
        Assert.Equal(10, await GetStockAsync(p));
    }

    [Fact]
    public async Task Create_UnknownProductOnLaterLine_Returns400AndLeavesStockUntouched()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);

        var response = await client.PostAsJsonAsync("/api/purchase-orders", Dto((p, 5, 10m), (99999, 1, 10m)));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "99999");
        Assert.Equal(10, await GetStockAsync(p));
    }

    [Theory]
    [InlineData(0, 10)]   // Quantity must be > 0
    [InlineData(-1, 10)]
    [InlineData(1, -0.01)] // UnitPrice must be >= 0
    public async Task Create_InvalidLine_Returns400(int quantity, double unitPrice)
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);

        var response = await client.PostAsJsonAsync("/api/purchase-orders", Dto((p, quantity, (decimal)unitPrice)));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(10, await GetStockAsync(p));
    }

    [Fact]
    public async Task Create_ZeroUnitPrice_IsAllowed()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 0);

        var response = await client.PostAsJsonAsync("/api/purchase-orders", Dto((p, 1, 0m)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_NoItems_Returns400()
    {
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/purchase-orders", Dto());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "at least one item");
    }

    [Fact]
    public async Task Get_ReturnsCreatedOrderWithProductNames_And404ForUnknownId()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 0);
        var created = await (await client.PostAsJsonAsync("/api/purchase-orders", Dto((p, 2, 10m))))
            .Content.ReadFromJsonAsync<PurchaseOrderDto>();

        var found = await client.GetAsync($"/api/purchase-orders/{created!.Id}");
        var missing = await client.GetAsync("/api/purchase-orders/99999");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var dto = await found.Content.ReadFromJsonAsync<PurchaseOrderDto>();
        Assert.Equal(20m, dto!.TotalAmount);
        Assert.Equal("PO test product", dto.Items.Single().ProductName);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Delete_ReversesStockAndLogsNegativeMovement()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);
        var created = await (await client.PostAsJsonAsync("/api/purchase-orders", Dto((p, 5, 10m))))
            .Content.ReadFromJsonAsync<PurchaseOrderDto>();
        Assert.Equal(15, await GetStockAsync(p));

        var response = await client.DeleteAsync($"/api/purchase-orders/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10, await GetStockAsync(p));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains(db.StockMovements, m => m.RefId == created.Id && m.Reason == "PurchaseCancel" && m.ChangeQuantity == -5);
    }
}
