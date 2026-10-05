using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// Stock-in endpoint: POST /api/products is restricted to Admin/Kho
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
        Sku = $"SKU-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
        Unit = "cái",
        PurchasePrice = 8000,
        SalePrice = 10000,
        Quantity = 5,
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
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/products", NewProductDto());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_CorrectRole_Succeeds()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Kho");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/products", NewProductDto());

        // POST returns 201 Created on success, the "authorized" counterpart to 401/403
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_SalePriceBelowPurchasePrice_Returns400ProblemDetails()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Kho");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var dto = NewProductDto();
        dto.PurchasePrice = 20000;
        dto.SalePrice = 10000;

        var response = await client.PostAsJsonAsync("/api/products", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("SalePrice", problem!.Errors.Keys);
        Assert.Contains("Giá bán không được nhỏ hơn giá nhập", problem.Errors["SalePrice"]);
    }

    [Fact]
    public async Task CreateProduct_DuplicateSku_Returns400OnSecondRequest()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Kho");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var dto = NewProductDto();

        var first = await client.PostAsJsonAsync("/api/products", dto);
        var second = await client.PostAsJsonAsync("/api/products", dto);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        // Rejected by the async SKU validator before reaching the service
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.Contains("Sku", problem!.Errors.Keys);
    }

    [Fact]
    public async Task CreateProduct_MissingNameAndBadSku_Returns400WithPerFieldErrors()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Kho");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var dto = NewProductDto();
        dto.Name = "";
        dto.Sku = "kb";

        var response = await client.PostAsJsonAsync("/api/products", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.Contains("Name", problem!.Errors.Keys);
        Assert.Contains("Sku", problem.Errors.Keys);
    }

    [Fact]
    public async Task CreateProduct_Response_DoesNotExposePurchasePrice()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Kho");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/products", NewProductDto());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("purchaseprice", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("costprice", body, StringComparison.OrdinalIgnoreCase);
    }
}
