using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// Stock-in endpoint: POST /api/products is restricted to Admin/WarehouseManager
public class ProductsAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductsAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static CreateProductDto NewProductDto() => new()
    {
        Name = "Integration Test Product",
        Sku = $"SKU-TEST-{Guid.NewGuid():N}",
        Price = 10000,
        StockQuantity = 5,
        CategoryId = 1 // seeded via AppDbContext.OnModelCreating HasData
    };

    [Fact]
    public async Task CreateProduct_NoToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/products", NewProductDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_WrongRole_Returns403()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "SalesStaff");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/products", NewProductDto());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_CorrectRole_Succeeds()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "WarehouseManager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/products", NewProductDto());

        // POST returns 201 Created on success, the "authorized" counterpart to 401/403
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
