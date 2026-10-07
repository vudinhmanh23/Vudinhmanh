using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// The stock ledger (StockMovements) must explain every unit of Products.StockQuantity.
// Before the fix, creating a product with opening stock and editing a product (PUT) changed StockQuantity with no ledger row,
// and a form opened before a sale could overwrite that sale with its stale stock value.
public class StockLedgerReconciliationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public StockLedgerReconciliationTests(CustomWebApplicationFactory factory)
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

    private static CreateProductDto NewProduct(int stock) => new()
    {
        Name = "Ledger Test Product",
        Sku = $"LG-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
        Unit = "cái",
        PurchasePrice = 1000,
        SalePrice = 2000,
        Quantity = stock,
        IsActive = true,
        CategoryId = 1
    };

    private static async Task<ProductDto> CreateAsync(HttpClient client, int stock)
    {
        var response = await client.PostAsJsonAsync("/api/products", NewProduct(stock));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private static async Task<ProductDto> GetAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<ProductDto>($"/api/products/{id}"))!;

    private static async Task<List<StockMovementDto>> LedgerAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<List<StockMovementDto>>($"/api/products/{id}/movements"))!;

    [Fact]
    public async Task Create_WithOpeningStock_WritesOneAdjustmentRowExplainingIt()
    {
        var client = await AdminClientAsync();

        var product = await CreateAsync(client, stock: 7);

        var ledger = await LedgerAsync(client, product.Id);
        var row = Assert.Single(ledger);
        Assert.Equal("Adjustment", row.MovementType);
        Assert.Equal(7, row.Quantity);
        Assert.Equal(7, row.StockAfter);
        Assert.Equal("InitialStock", row.RefType);
        Assert.Equal(7, (await GetAsync(client, product.Id)).Quantity);
    }

    [Fact]
    public async Task Create_WithZeroStock_WritesNoLedgerRow()
    {
        var client = await AdminClientAsync();

        var product = await CreateAsync(client, stock: 0);

        Assert.Empty(await LedgerAsync(client, product.Id));
    }

    [Fact]
    public async Task Update_IgnoresQuantity_KeepsStock_AndWritesNoLedgerRow()
    {
        var client = await AdminClientAsync();
        var product = await CreateAsync(client, stock: 10);

        var update = await client.PutAsJsonAsync($"/api/products/{product.Id}", new UpdateProductDto
        {
            Name = "Renamed By Update",
            Sku = product.Sku,
            Unit = "cái",
            PurchasePrice = 1000,
            SalePrice = 2500,
            Quantity = 999, // what a stale or hand-edited form could send
            IsActive = true,
            CategoryId = 1
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var after = await GetAsync(client, product.Id);
        Assert.Equal("Renamed By Update", after.Name); // the update itself still works
        Assert.Equal(2500, after.SalePrice);
        Assert.Equal(10, after.Quantity); // but stock did not move
        Assert.Single(await LedgerAsync(client, product.Id)); // and no row was invented
    }

    [Fact]
    public async Task StaleForm_CannotOverwriteASaleMadeAfterItWasOpened()
    {
        var client = await AdminClientAsync();
        var product = await CreateAsync(client, stock: 10);
        var stale = await GetAsync(client, product.Id); // the form is opened here, showing 10

        var customer = await client.PostAsJsonAsync("/api/customers", new CreateCustomerDto { Name = "Ledger Customer" });
        var customerId = (await customer.Content.ReadFromJsonAsync<CustomerDto>())!.Id;
        var sale = await client.PostAsJsonAsync("/api/sales-orders", new CreateOrderDto
        {
            OrderDate = DateTime.UtcNow,
            CustomerId = customerId,
            Items = new List<CreateOrderItemDto> { new() { ProductId = product.Id, Quantity = 4, UnitPrice = 2000m } }
        });
        Assert.Equal(HttpStatusCode.Created, sale.StatusCode); // stock is now 6

        // ...and the form is saved afterwards with the value it showed (10)
        var save = await client.PutAsJsonAsync($"/api/products/{product.Id}", new UpdateProductDto
        {
            Name = "Edited After Sale", Sku = stale.Sku, Unit = "cái", PurchasePrice = 1000, SalePrice = 2000,
            Quantity = stale.Quantity, IsActive = true, CategoryId = 1
        });
        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);

        Assert.Equal(6, (await GetAsync(client, product.Id)).Quantity); // the sale is not erased
    }

    [Fact]
    public async Task Ledger_ReconcilesWithStock_AfterCreateReceiveSellAdjustAndEdit()
    {
        var client = await AdminClientAsync();
        var product = await CreateAsync(client, stock: 10); // +10 (opening stock)

        var order = await client.PostAsJsonAsync("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            OrderDate = DateTime.UtcNow,
            SupplierId = 1,
            Items = new List<CreatePurchaseOrderItemDto> { new() { ProductId = product.Id, Quantity = 5, UnitPrice = 900m } }
        });
        var orderId = (await order.Content.ReadFromJsonAsync<PurchaseOrderDto>())!.Id;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/purchase-orders/{orderId}/approve", null)).StatusCode); // +5

        var customer = await client.PostAsJsonAsync("/api/customers", new CreateCustomerDto { Name = "Ledger Customer 2" });
        var customerId = (await customer.Content.ReadFromJsonAsync<CustomerDto>())!.Id;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/sales-orders", new CreateOrderDto
        {
            OrderDate = DateTime.UtcNow,
            CustomerId = customerId,
            Items = new List<CreateOrderItemDto> { new() { ProductId = product.Id, Quantity = 3, UnitPrice = 2000m } }
        })).StatusCode); // -3

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/products/{product.Id}/adjust-stock",
            new AdjustStockDto { Delta = -2, Reason = "Ledger test: breakage" })).StatusCode); // -2

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/products/{product.Id}", new UpdateProductDto
        {
            Name = "Edited", Sku = product.Sku, Unit = "cái", PurchasePrice = 1000, SalePrice = 2000,
            Quantity = 12345, IsActive = true, CategoryId = 1
        })).StatusCode); // changes nothing about stock

        var stock = (await GetAsync(client, product.Id)).Quantity;
        var ledger = await LedgerAsync(client, product.Id);

        Assert.Equal(10, stock); // 10 + 5 - 3 - 2
        Assert.Equal(4, ledger.Count);
        Assert.Equal(stock, ledger.Sum(r => r.Quantity)); // the ledger explains every unit
        Assert.Equal(stock, ledger.OrderByDescending(r => r.Id).First().StockAfter); // and its last row ends at the stock
    }
}
