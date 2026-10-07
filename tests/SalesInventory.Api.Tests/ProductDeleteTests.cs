using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// DELETE /api/products/{id}: a product that orders or the stock ledger refer to cannot be deleted.
// Before the fix the database's foreign key made the delete fail and the API answered 500.
public class ProductDeleteTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductDeleteTests(CustomWebApplicationFactory factory)
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

    private static async Task<ProductDto> CreateProductAsync(HttpClient client, int stock = 10)
    {
        var response = await client.PostAsJsonAsync("/api/products", new CreateProductDto
        {
            Name = "Delete Test Product",
            Sku = $"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            Unit = "cái",
            PurchasePrice = 1000,
            SalePrice = 2000,
            Quantity = stock,
            IsActive = true,
            CategoryId = 1
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private static async Task AssertDeleteRefusedAsync(HttpClient client, int productId)
    {
        var response = await client.DeleteAsync($"/api/products/{productId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // Nothing was deleted
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/products/{productId}")).StatusCode);
    }

    [Fact]
    public async Task Delete_ProductWithNoDocuments_Returns204_AndTheProductIsGone()
    {
        var client = await AdminClientAsync();
        var product = await CreateProductAsync(client);

        var response = await client.DeleteAsync($"/api/products/{product.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/products/{product.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownProduct_Returns404()
    {
        var client = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/products/999999")).StatusCode);
    }

    [Fact]
    public async Task Delete_ProductOnAPurchaseOrder_Returns409_NotAServerError()
    {
        var client = await AdminClientAsync();
        var product = await CreateProductAsync(client);
        var order = await client.PostAsJsonAsync("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            OrderDate = DateTime.UtcNow,
            SupplierId = 1,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { ProductId = product.Id, Quantity = 3, UnitPrice = 100m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode); // a Draft is enough: its line points at the product

        await AssertDeleteRefusedAsync(client, product.Id);
    }

    [Fact]
    public async Task Delete_ProductOnASalesOrder_Returns409_NotAServerError()
    {
        var client = await AdminClientAsync();
        var product = await CreateProductAsync(client, stock: 10);
        var customer = await client.PostAsJsonAsync("/api/customers", new CreateCustomerDto { Name = "Delete Test Customer" });
        Assert.Equal(HttpStatusCode.Created, customer.StatusCode);
        var customerId = (await customer.Content.ReadFromJsonAsync<CustomerDto>())!.Id;

        var sale = await client.PostAsJsonAsync("/api/sales-orders", new CreateOrderDto
        {
            OrderDate = DateTime.UtcNow,
            CustomerId = customerId,
            Items = new List<CreateOrderItemDto> { new() { ProductId = product.Id, Quantity = 2, UnitPrice = 2000m } }
        });
        Assert.Equal(HttpStatusCode.Created, sale.StatusCode);

        await AssertDeleteRefusedAsync(client, product.Id);
    }

    [Fact]
    public async Task Delete_ProductWithOnlyStockHistory_Returns409_NotAServerError()
    {
        var client = await AdminClientAsync();
        var product = await CreateProductAsync(client, stock: 10);
        var adjust = await client.PostAsJsonAsync($"/api/products/{product.Id}/adjust-stock",
            new AdjustStockDto { Delta = 5, Reason = "Delete test: a ledger row and nothing else" });
        Assert.Equal(HttpStatusCode.OK, adjust.StatusCode);

        await AssertDeleteRefusedAsync(client, product.Id);
    }
}
