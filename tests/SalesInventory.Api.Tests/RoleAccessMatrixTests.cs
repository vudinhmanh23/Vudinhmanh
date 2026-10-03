using System.Net;
using System.Net.Http.Headers;

namespace SalesInventory.Api.Tests;

// Controller-level access rules (GET list endpoints):
//   purchaseorders, suppliers -> Admin, Kho
//   salesorders               -> Admin, BanHang, Kho
//   products, categories GET  -> any authenticated role
//   users                     -> Admin
public class RoleAccessMatrixTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RoleAccessMatrixTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/products")]
    [InlineData("/api/purchase-orders")]
    [InlineData("/api/salesorders")]
    [InlineData("/api/suppliers")]
    [InlineData("/api/categories")]
    [InlineData("/api/users")]
    public async Task NoToken_Returns401(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/purchase-orders", "BanHang")]
    [InlineData("/api/suppliers", "BanHang")]
    [InlineData("/api/users", "Kho")]
    [InlineData("/api/users", "BanHang")]
    public async Task WrongRole_Returns403(string url, string role)
    {
        var response = await GetAsAsync(url, role);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/products", "Admin")]
    [InlineData("/api/products", "Kho")]
    [InlineData("/api/purchase-orders", "Admin")]
    [InlineData("/api/purchase-orders", "Kho")]
    [InlineData("/api/products", "BanHang")]
    [InlineData("/api/suppliers", "Admin")]
    [InlineData("/api/suppliers", "Kho")]
    [InlineData("/api/salesorders", "Admin")]
    [InlineData("/api/salesorders", "BanHang")]
    [InlineData("/api/salesorders", "Kho")]
    [InlineData("/api/categories", "Admin")]
    [InlineData("/api/categories", "Kho")]
    [InlineData("/api/categories", "BanHang")]
    [InlineData("/api/users", "Admin")]
    public async Task CorrectRole_Returns200(string url, string role)
    {
        var response = await GetAsAsync(url, role);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetAsAsync(string url, string role)
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync(url);
    }
}
