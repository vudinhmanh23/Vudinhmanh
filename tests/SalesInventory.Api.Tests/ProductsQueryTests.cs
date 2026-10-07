using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// GET /api/products: optional filters, whitelisted sorting and paging metadata
public class ProductsQueryTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductsQueryTests(CustomWebApplicationFactory factory)
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

    // Three products sharing a unique tag: price/stock = B 300/1, A 100/3, C 200/2
    private static async Task<string> SeedAsync(HttpClient client)
    {
        var tag = $"Qry{Guid.NewGuid():N}"[..12];
        foreach (var (suffix, price, qty) in new[] { ("B", 300m, 1), ("A", 100m, 3), ("C", 200m, 2) })
        {
            var response = await client.PostAsJsonAsync("/api/products", new CreateProductDto
            {
                Name = $"{tag}-{suffix}",
                Sku = $"{tag}{suffix}".ToUpperInvariant(),
                Unit = "cái",
                PurchasePrice = 10,
                SalePrice = price,
                Quantity = qty,
                IsActive = true,
                CategoryId = 1
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        return tag;
    }

    private static async Task<PagedResult<ProductListItemDto>> GetAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/products?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<ProductListItemDto>>())!;
    }

    [Fact]
    public async Task Keyword_MatchesSku_AndPagingReportsTotals()
    {
        var client = await KhoClientAsync();
        var tag = await SeedAsync(client);

        var page = await GetAsync(client, $"keyword={tag.ToLowerInvariant()}&pageSize=2&page=2");

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(2, page.Page);
        Assert.Single(page.Items);
    }

    [Theory]
    [InlineData("sortBy=price&sortDescending=true", new[] { 300, 200, 100 })]
    [InlineData("sortBy=stock", new[] { 300, 200, 100 })]
    [InlineData("sortBy=name", new[] { 100, 300, 200 })]
    [InlineData("sortBy=bogus;drop", new[] { 100, 300, 200 })]
    public async Task SortBy_UsesWhitelist_UnknownFallsBackToName(string sort, int[] expectedPrices)
    {
        var client = await KhoClientAsync();
        var tag = await SeedAsync(client);

        var result = await GetAsync(client, $"keyword={tag}&{sort}");

        Assert.Equal(expectedPrices.Select(p => (decimal)p), result.Items.Select(i => i.SalePrice));
    }

    [Fact]
    public async Task PriceRange_FiltersInclusive_AndPageSizeIsClamped()
    {
        var client = await KhoClientAsync();
        var tag = await SeedAsync(client);

        var result = await GetAsync(client, $"keyword={tag}&minPrice=150&maxPrice=300&pageSize=1000");

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(100, result.PageSize);
    }

    // Creates one product under the given tag, returns nothing; used by the stock and multi-key sort tests
    private static async Task CreateAsync(HttpClient client, string tag, string suffix, decimal price, int qty, int categoryId)
    {
        var response = await client.PostAsJsonAsync("/api/products", new CreateProductDto
        {
            Name = $"{tag}-{suffix}",
            Sku = $"{tag}{suffix}".ToUpperInvariant(),
            Unit = "cái",
            PurchasePrice = 10,
            SalePrice = price,
            Quantity = qty,
            IsActive = true,
            CategoryId = categoryId
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InStockOnly_FiltersOnlyWhenTrue()
    {
        var client = await KhoClientAsync();
        var tag = $"Stk{Guid.NewGuid():N}"[..12];
        await CreateAsync(client, tag, "A", 100, 5, 1);
        await CreateAsync(client, tag, "B", 100, 0, 1);

        var all = await GetAsync(client, $"keyword={tag}");
        var explicitFalse = await GetAsync(client, $"keyword={tag}&inStockOnly=false");
        var inStock = await GetAsync(client, $"keyword={tag}&inStockOnly=true");

        Assert.Equal(2, all.TotalCount);
        Assert.Equal(2, explicitFalse.TotalCount);
        Assert.Equal(1, inStock.TotalCount);
        Assert.All(inStock.Items, i => Assert.True(i.Quantity > 0));
    }

    [Fact]
    public async Task SortBy_MultipleKeys_OrdersByCategoryThenPrice()
    {
        var client = await KhoClientAsync();
        var tag = $"Mul{Guid.NewGuid():N}"[..12];

        // The test database only seeds category 1, so create a second one
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryDto { Name = $"Cat{tag}" });
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        var otherCategory = (await categoryResponse.Content.ReadFromJsonAsync<CategoryDto>())!.Id;

        await CreateAsync(client, tag, "A", 300, 1, 1);
        await CreateAsync(client, tag, "B", 100, 1, 1);
        await CreateAsync(client, tag, "C", 400, 1, otherCategory);
        await CreateAsync(client, tag, "D", 200, 1, otherCategory);

        var result = await GetAsync(client, $"keyword={tag}&sortBy=category,price");

        // Whatever the collation puts first, each category must be one contiguous block with prices ascending inside it
        var groups = result.Items.GroupBy(i => i.CategoryId, (_, rows) => rows.ToList()).ToList();
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(g.Select(i => i.SalePrice).OrderBy(x => x), g.Select(i => i.SalePrice)));
        // A key that is not whitelisted is ignored rather than breaking the query
        var withJunk = await GetAsync(client, $"keyword={tag}&sortBy=category,Name;DROP,price");
        Assert.Equal(result.Items.Select(i => i.Id), withJunk.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task MinPriceAboveMaxPrice_Returns400()
    {
        var client = await KhoClientAsync();

        var response = await client.GetAsync("/api/products?minPrice=500&maxPrice=100");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ListsAllQueryParameters()
    {
        var json = await _factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        foreach (var name in new[] { "Keyword", "CategoryId", "MinPrice", "MaxPrice", "SortBy", "SortDescending", "Page", "PageSize" })
        {
            Assert.Contains($"\"name\": \"{name}\"", json.Replace("\":\"", "\": \""));
        }
    }
}
