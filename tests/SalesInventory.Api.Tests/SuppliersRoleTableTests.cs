using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SalesInventory.Api.Tests;

// POST /api/suppliers across all roles:
//   Admin -> 201, Kho -> 201, BanHang -> 403, no token -> 401
public class SuppliersRoleTableTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SuppliersRoleTableTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.Created)]
    [InlineData("Kho", HttpStatusCode.Created)]
    [InlineData("BanHang", HttpStatusCode.Forbidden)]
    public async Task CreateSupplier_StatusByRole(string role, HttpStatusCode expected)
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/suppliers", new { code = $"SUP-{role}", name = $"NCC {role}", phone = "0900000001" });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task CreateSupplier_NoToken_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/suppliers", new { code = "SUP-ANON", name = "NCC anon" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
