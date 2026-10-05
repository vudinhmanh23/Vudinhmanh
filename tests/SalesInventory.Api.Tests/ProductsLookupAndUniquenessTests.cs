using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// by-sku lookup, SKU/Barcode uniqueness on update, and the inactive-products filter
public class ProductsLookupAndUniquenessTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductsLookupAndUniquenessTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> KhoClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Kho");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static CreateProductDto NewDto(string? barcode = null, bool isActive = true) => new()
    {
        Name = "Lookup Test Product",
        Sku = $"LK-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
        Barcode = barcode,
        Unit = "cái",
        PurchasePrice = 1000,
        SalePrice = 2000,
        Quantity = 1,
        IsActive = isActive,
        CategoryId = 1
    };

    private static UpdateProductDto ToUpdate(CreateProductDto d) => new()
    {
        Name = d.Name, Sku = d.Sku, Barcode = d.Barcode, Unit = d.Unit, PurchasePrice = d.PurchasePrice,
        SalePrice = d.SalePrice, Quantity = d.Quantity, IsActive = d.IsActive, CategoryId = d.CategoryId
    };

    private static async Task<ProductDto> CreateAsync(HttpClient client, CreateProductDto dto)
    {
        var response = await client.PostAsJsonAsync("/api/products", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    [Fact]
    public async Task GetBySku_Existing_ReturnsProduct_Unknown_Returns404()
    {
        var client = await KhoClientAsync();
        var created = await CreateAsync(client, NewDto());

        var found = await client.GetAsync($"/api/products/by-sku/{created.Sku}");
        var missing = await client.GetAsync("/api/products/by-sku/NOPE-NOPE");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(created.Id, (await found.Content.ReadFromJsonAsync<ProductDto>())!.Id);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Update_SkuOfAnotherProduct_Returns400()
    {
        var client = await KhoClientAsync();
        var a = await CreateAsync(client, NewDto());
        var bDto = NewDto();
        var b = await CreateAsync(client, bDto);
        var update = ToUpdate(bDto);
        update.Sku = a.Sku;

        var response = await client.PutAsJsonAsync($"/api/products/{b.Id}", update);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Sku", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Update_KeepingOwnSku_Succeeds()
    {
        var client = await KhoClientAsync();
        var dto = NewDto();
        var created = await CreateAsync(client, dto);

        var response = await client.PutAsJsonAsync($"/api/products/{created.Id}", ToUpdate(dto));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Barcode_Duplicate_BlockedOnCreateAndUpdate_NullsAllowedMultipleTimes()
    {
        var client = await KhoClientAsync();
        var barcode = $"BC-{Guid.NewGuid():N}";
        await CreateAsync(client, NewDto(barcode));
        await CreateAsync(client, NewDto()); // two products without a barcode are fine
        var other = await CreateAsync(client, NewDto());

        var dup = await client.PostAsJsonAsync("/api/products", NewDto(barcode));
        var update = ToUpdate(NewDto(barcode));
        update.Sku = other.Sku;
        var dupUpdate = await client.PutAsJsonAsync($"/api/products/{other.Id}", update);

        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, dupUpdate.StatusCode);
    }

    [Fact]
    public async Task Inactive_ReturnsOnlyDiscontinuedProducts()
    {
        var client = await KhoClientAsync();
        var inactive = await CreateAsync(client, NewDto(isActive: false));
        var active = await CreateAsync(client, NewDto());

        var response = await client.GetAsync("/api/products/inactive");
        var list = await response.Content.ReadFromJsonAsync<List<ProductDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(list!, p => p.Id == inactive.Id);
        Assert.DoesNotContain(list!, p => p.Id == active.Id);
        Assert.All(list!, p => Assert.False(p.IsActive));
    }
}
