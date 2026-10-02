using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SalesInventory.Api.Tests;

// Write endpoints of categories/suppliers (Admin, Kho only) and delete of sales orders (Admin only)
public class WriteAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public WriteAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/categories")]
    [InlineData("/api/suppliers")]
    public async Task Post_NoToken_Returns401(string url)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(url, new { name = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/categories", "BanHang")]
    [InlineData("/api/suppliers", "BanHang")]
    public async Task Post_WrongRole_Returns403(string url, string role)
    {
        var response = await PostAsAsync(url, role);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/categories", "Admin")]
    [InlineData("/api/categories", "Kho")]
    [InlineData("/api/suppliers", "Admin")]
    [InlineData("/api/suppliers", "Kho")]
    public async Task Post_CorrectRole_Returns201(string url, string role)
    {
        var response = await PostAsAsync(url, role);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("Kho")]
    [InlineData("BanHang")]
    public async Task DeleteSalesOrder_NonAdmin_Returns403(string role)
    {
        var client = await ClientAsAsync(role);

        var response = await client.DeleteAsync("/api/salesorders/999999");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> ClientAsAsync(string role)
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpResponseMessage> PostAsAsync(string url, string role)
    {
        var client = await ClientAsAsync(role);
        return await client.PostAsJsonAsync(url, new { code = $"T-{Guid.NewGuid():N}"[..20], name = $"Test {Guid.NewGuid():N}", phone = "0900000001" });
    }
}
