using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// PUT without purchasePrice (what UIs must do, since cost price is never returned) keeps the stored cost price
public class ProductsUpdateKeepsCostPriceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductsUpdateKeepsCostPriceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, ProductDto Product, string Sku)> CreateWithCostAsync(decimal cost)
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Kho");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var sku = $"KC-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var response = await client.PostAsJsonAsync("/api/products", new CreateProductDto
        {
            Name = "Keep Cost", Sku = sku, Unit = "cái", PurchasePrice = cost, SalePrice = cost * 2, Quantity = 1, CategoryId = 1
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (client, (await response.Content.ReadFromJsonAsync<ProductDto>())!, sku);
    }

    private static object Body(string sku, decimal salePrice) => new
    {
        name = "Keep Cost", sku, unit = "cái", salePrice, quantity = 1, categoryId = 1, isActive = true
    };

    [Fact]
    public async Task Put_WithoutPurchasePrice_ChecksSalePriceAgainstStoredCost()
    {
        var (client, product, sku) = await CreateWithCostAsync(1000);

        var tooLow = await client.PutAsJsonAsync($"/api/products/{product.Id}", Body(sku, 500));
        var fine = await client.PutAsJsonAsync($"/api/products/{product.Id}", Body(sku, 1500));

        Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, fine.StatusCode);
    }

    [Fact]
    public async Task Put_WithoutPurchasePrice_UnknownProduct_Returns404()
    {
        var (client, _, _) = await CreateWithCostAsync(1000);

        // Unused SKU, so validation passes and the missing product is what fails

        var response = await client.PutAsJsonAsync("/api/products/999999", Body($"NF-{Guid.NewGuid():N}"[..20].ToUpperInvariant(), 1500));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
