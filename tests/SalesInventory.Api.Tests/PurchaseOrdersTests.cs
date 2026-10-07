using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Api.Tests;

// Stock-in business rules: draft on create, approve adds stock + movements, validation, ProblemDetails, 404/409
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

    private static async Task<PurchaseOrderDto> CreateAsync(HttpClient client, CreatePurchaseOrderDto dto)
    {
        var response = await client.PostAsJsonAsync("/api/purchase-orders", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PurchaseOrderDto>())!;
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
    public async Task Create_ComputesTotals_AsDraft_AndDoesNotTouchStock()
    {
        var client = await AdminClientAsync();
        var p1 = await AddProductAsync(stock: 10);
        var p2 = await AddProductAsync(stock: 5);

        var response = await client.PostAsJsonAsync("/api/purchase-orders", Dto((p1, 3, 100m), (p2, 2, 250.50m)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PurchaseOrderDto>();
        Assert.NotNull(created);
        Assert.Equal("Draft", created!.Status);
        Assert.Equal(300m + 501m, created.TotalAmount);
        Assert.Equal(new[] { 300m, 501m }, created.Items.Select(i => i.LineTotal).OrderBy(x => x));

        // A draft changes nothing in stock and logs no movement
        Assert.Equal(10, await GetStockAsync(p1));
        Assert.Equal(5, await GetStockAsync(p2));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.StockMovements.Where(m => m.RefId == created.Id).ToListAsync());
    }

    [Fact]
    public async Task Approve_Draft_IncreasesStockForEveryLine_AndLogsImportMovements()
    {
        var client = await AdminClientAsync();
        var p1 = await AddProductAsync(stock: 10);
        var p2 = await AddProductAsync(stock: 5);
        var created = await CreateAsync(client, Dto((p1, 3, 100m), (p2, 2, 250.50m)));

        var response = await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = await response.Content.ReadFromJsonAsync<PurchaseOrderDto>();
        Assert.Equal("Approved", approved!.Status);

        Assert.Equal(13, await GetStockAsync(p1));
        Assert.Equal(7, await GetStockAsync(p2));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movements = await db.StockMovements.Where(m => m.RefId == created.Id).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m =>
        {
            Assert.Equal(StockMovementType.Import, m.MovementType);
            Assert.Equal("PurchaseOrder", m.RefType);
            Assert.True(m.Quantity > 0);
        });
        Assert.Equal(3, movements.Single(m => m.ProductId == p1).Quantity);
        Assert.Equal(2, movements.Single(m => m.ProductId == p2).Quantity);
        Assert.Equal(13, movements.Single(m => m.ProductId == p1).StockAfter);
        Assert.Equal(7, movements.Single(m => m.ProductId == p2).StockAfter);
        Assert.Equal(PurchaseOrderStatus.Approved, (await db.PurchaseOrders.SingleAsync(o => o.Id == created.Id)).Status);
    }

    [Fact]
    public async Task Approve_Twice_Returns409ProblemDetails_AndStockIsNotAddedAgain()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);
        var created = await CreateAsync(client, Dto((p, 4, 10m)));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null)).StatusCode);

        var second = await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null);

        await AssertProblemAsync(second, HttpStatusCode.Conflict, "Đã duyệt");
        Assert.Equal(14, await GetStockAsync(p));
    }

    [Fact]
    public async Task Approve_CancelledOrder_Returns409()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);
        var created = await CreateAsync(client, Dto((p, 4, 10m)));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PurchaseOrders.SingleAsync(o => o.Id == created.Id)).Status = PurchaseOrderStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "Đã hủy");
        Assert.Equal(10, await GetStockAsync(p));
    }

    [Fact]
    public async Task Approve_UnknownOrder_Returns404ProblemDetails()
    {
        var client = await AdminClientAsync();

        var response = await client.PostAsync("/api/purchase-orders/99999/approve", null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "99999");
    }

    [Fact]
    public async Task Approve_WrongRoleOrNoToken_IsRejected()
    {
        var noToken = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await noToken.PostAsync("/api/purchase-orders/1/approve", null)).StatusCode);

        var sales = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(sales, "BanHang");
        sales.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsync("/api/purchase-orders/1/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Create_TwoOrdersOnTheSameDay_GenerateSequentialCodes()
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

        var created = await CreateAsync(client, Dto((p, 4, 10m), (p, 6, 10m)));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null)).StatusCode);

        Assert.Equal(10, await GetStockAsync(p));
    }

    [Fact]
    public async Task Create_ClientSuppliedTotals_AreRecomputedByTheServer()
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
    public async Task Create_UnknownSupplier_Returns404AndChangesNothing()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);
        var dto = Dto((p, 5, 10m));
        dto.SupplierId = 99999;

        var response = await client.PostAsJsonAsync("/api/purchase-orders", dto);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "Không tìm thấy nhà cung cấp");
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
    public async Task Approve_OrderWithoutLines_Returns409ProblemDetails()
    {
        var client = await AdminClientAsync();
        int emptyOrderId;
        using (var scope = _factory.Services.CreateScope())
        {
            // The API refuses to create an empty order, so insert one directly to simulate bad/legacy data
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new PurchaseOrder
            {
                Code = $"PO-EMPTY-{Guid.NewGuid():N}"[..20],
                SupplierId = 1,
                OrderDate = DateTime.UtcNow,
                Status = PurchaseOrderStatus.Draft
            };
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync();
            emptyOrderId = order.Id;
        }

        var response = await client.PostAsync($"/api/purchase-orders/{emptyOrderId}/approve", null);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "không có dòng hàng");
        using var check = _factory.Services.CreateScope();
        var order2 = await check.ServiceProvider.GetRequiredService<AppDbContext>().PurchaseOrders.AsNoTracking().SingleAsync(o => o.Id == emptyOrderId);
        Assert.Equal(PurchaseOrderStatus.Draft, order2.Status);
    }

    [Fact]
    public async Task Cancel_ApprovedOrder_RestoresStockAndLogsNegativeAdjustment()
    {
        var client = await AdminClientAsync();
        var p1 = await AddProductAsync(stock: 10);
        var p2 = await AddProductAsync(stock: 5);
        var created = await CreateAsync(client, Dto((p1, 3, 100m), (p2, 2, 50m)));
        await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null);
        Assert.Equal(13, await GetStockAsync(p1));

        var response = await client.PostAsync($"/api/purchase-orders/{created.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", (await response.Content.ReadFromJsonAsync<PurchaseOrderDto>())!.Status);
        // Stock is back to the values before the approval
        Assert.Equal(10, await GetStockAsync(p1));
        Assert.Equal(5, await GetStockAsync(p2));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movements = await db.StockMovements.Where(m => m.RefId == created.Id).ToListAsync();
        Assert.Equal(4, movements.Count); // per product: one Purchase (+) and one Adjustment (-)
        foreach (var (productId, quantity) in new[] { (p1, 3), (p2, 2) })
        {
            var import = movements.Single(m => m.ProductId == productId && m.MovementType == StockMovementType.Import);
            var adjustment = movements.Single(m => m.ProductId == productId && m.MovementType == StockMovementType.Adjustment);
            Assert.Equal(quantity, import.Quantity);
            Assert.Equal(-quantity, adjustment.Quantity);
        }
    }

    [Fact]
    public async Task Cancel_DraftOrAlreadyCancelled_Returns409AndStockUnchanged()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);
        var created = await CreateAsync(client, Dto((p, 4, 10m)));

        await AssertProblemAsync(await client.PostAsync($"/api/purchase-orders/{created.Id}/cancel", null),
            HttpStatusCode.Conflict, "Nháp");

        await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/purchase-orders/{created.Id}/cancel", null)).StatusCode);

        await AssertProblemAsync(await client.PostAsync($"/api/purchase-orders/{created.Id}/cancel", null),
            HttpStatusCode.Conflict, "Đã hủy");
        Assert.Equal(10, await GetStockAsync(p));
    }

    [Fact]
    public async Task Cancel_WhenGoodsAlreadySold_Returns409AndChangesNothing()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 0);
        var created = await CreateAsync(client, Dto((p, 10, 10m)));
        await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null);

        // Simulate selling 7 of the 10 received units, leaving too little to take back
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Products.SingleAsync(x => x.Id == p)).StockQuantity = 3;
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/purchase-orders/{created.Id}/cancel", null);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "chỉ còn 3");
        Assert.Equal(3, await GetStockAsync(p));
        using var check = _factory.Services.CreateScope();
        var db2 = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PurchaseOrderStatus.Approved, (await db2.PurchaseOrders.AsNoTracking().SingleAsync(o => o.Id == created.Id)).Status);
        Assert.DoesNotContain(db2.StockMovements, m => m.RefId == created.Id && m.MovementType == StockMovementType.Adjustment);
    }

    [Fact]
    public async Task Cancel_UnknownOrder_Returns404()
    {
        var client = await AdminClientAsync();

        await AssertProblemAsync(await client.PostAsync("/api/purchase-orders/99999/cancel", null),
            HttpStatusCode.NotFound, "99999");
    }

    [Fact]
    public async Task ProductMovements_ProductWithSeveralMovements_ReturnsNewestFirst()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);
        var created = await CreateAsync(client, Dto((p, 5, 10m)));
        await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null);
        await client.PostAsync($"/api/purchase-orders/{created.Id}/cancel", null);

        var response = await client.GetAsync($"/api/products/{p}/movements");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movements = (await response.Content.ReadFromJsonAsync<List<StockMovementDto>>())!;
        Assert.Equal(2, movements.Count);
        // Newest first: the cancellation's Adjustment, then the approval's Purchase
        Assert.Equal("Adjustment", movements[0].MovementType);
        Assert.Equal(-5, movements[0].Quantity);
        Assert.Equal("Import", movements[1].MovementType);
        Assert.Equal(5, movements[1].Quantity);
        Assert.All(movements, m =>
        {
            Assert.Equal(p, m.ProductId);
            Assert.Equal("PurchaseOrder", m.RefType);
            Assert.Equal(created.Id, m.RefId);
        });
    }

    [Fact]
    public async Task ProductMovements_UnknownProduct_Returns404_AndWrongRoleReturns403()
    {
        var admin = await AdminClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/products/99999/movements")).StatusCode);

        var sales = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(sales, "BanHang");
        sales.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync("/api/products/1/movements")).StatusCode);
    }

    [Fact]
    public async Task Delete_ApprovedOrder_ReversesStockAndLogsExportMovement()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);
        var created = await CreateAsync(client, Dto((p, 5, 10m)));
        await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null);
        Assert.Equal(15, await GetStockAsync(p));

        var response = await client.DeleteAsync($"/api/purchase-orders/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10, await GetStockAsync(p));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains(db.StockMovements, m =>
            m.RefId == created.Id && m.MovementType == StockMovementType.Sale && m.Quantity == -5);
    }

    [Fact]
    public async Task Delete_DraftOrder_DoesNotTouchStock()
    {
        var client = await AdminClientAsync();
        var p = await AddProductAsync(stock: 10);
        var created = await CreateAsync(client, Dto((p, 5, 10m)));

        var response = await client.DeleteAsync($"/api/purchase-orders/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10, await GetStockAsync(p));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.StockMovements.Where(m => m.RefId == created.Id).ToListAsync());
    }
}
