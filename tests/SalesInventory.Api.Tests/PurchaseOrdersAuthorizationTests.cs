using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// Stock-in endpoint: POST /api/purchaseorders is restricted to Admin/Kho
public class PurchaseOrdersAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PurchaseOrdersAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static CreatePurchaseOrderDto NewPurchaseOrderDto() => new()
    {
        OrderDate = DateTime.UtcNow,
        SupplierId = 1, // seeded via AppDbContext.OnModelCreating HasData
        Items = new List<CreatePurchaseOrderItemDto>
        {
            new() { ProductId = 1, Quantity = 10, UnitPrice = 100000 }
        }
    };

    [Fact]
    public async Task CreatePurchaseOrder_NoToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/purchaseorders", NewPurchaseOrderDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreatePurchaseOrder_WrongRole_Returns403()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/purchaseorders", NewPurchaseOrderDto());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreatePurchaseOrder_CorrectRole_Succeeds()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/purchaseorders", NewPurchaseOrderDto());

        // POST returns 201 Created on success, the "authorized" counterpart to 401/403
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
