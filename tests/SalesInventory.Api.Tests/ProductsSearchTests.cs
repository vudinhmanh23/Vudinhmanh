using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// GET /api/products/search: server-side paging, name search, category filter and sorting
public class ProductsSearchTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductsSearchTests(CustomWebApplicationFactory factory)
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

    // Creates three products sharing a unique name tag, with prices 300 / 100 / 200
    private static async Task<string> SeedTaggedProductsAsync(HttpClient client)
    {
        var tag = $"Srch{Guid.NewGuid():N}"[..12];
        foreach (var (suffix, price) in new[] { ("B", 300m), ("A", 100m), ("C", 200m) })
        {
            var response = await client.PostAsJsonAsync("/api/products", new CreateProductDto
            {
                Name = $"{tag}-{suffix}",
                Sku = $"{tag}{suffix}".ToUpperInvariant(),
                Unit = "cái",
                PurchasePrice = 10,
                SalePrice = price,
                Quantity = 1,
                IsActive = true,
                CategoryId = 1
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        return tag;
    }

    private static async Task<PagedResult<ProductDto>> GetPageAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/products/search?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>())!;
    }

    [Fact]
    public async Task Search_ByName_PagesAndReportsTotals()
    {
        var client = await KhoClientAsync();
        var tag = await SeedTaggedProductsAsync(client);

        var page1 = await GetPageAsync(client, $"search={tag}&pageSize=2&page=1");
        var page2 = await GetPageAsync(client, $"search={tag}&pageSize=2&page=2");

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal(new[] { $"{tag}-A", $"{tag}-B" }, page1.Items.Select(p => p.Name));
        Assert.Equal($"{tag}-C", page2.Items[0].Name);
    }

    [Fact]
    public async Task Search_SortByPriceDescending_OrdersInDatabase()
    {
        var client = await KhoClientAsync();
        var tag = await SeedTaggedProductsAsync(client);

        var result = await GetPageAsync(client, $"search={tag}&sortBy=price&sortDir=desc");

        Assert.Equal(new[] { 300m, 200m, 100m }, result.Items.Select(p => p.SalePrice));
    }

    [Fact]
    public async Task Search_CategoryFilter_OnlyReturnsThatCategory()
    {
        var client = await KhoClientAsync();

        var result = await GetPageAsync(client, "categoryId=1&pageSize=100");

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, p => Assert.Equal(1, p.CategoryId));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("sortBy=sku")]
    [InlineData("sortDir=sideways")]
    public async Task Search_InvalidParameters_Return400(string query)
    {
        var client = await KhoClientAsync();

        var response = await client.GetAsync($"/api/products/search?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/products/search");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
