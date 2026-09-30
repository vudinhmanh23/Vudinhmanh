using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// GET /api/auth/me reads identity from token claims and requires authentication
public class AuthMeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthMeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Me_NoToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ValidToken_ReturnsClaimsFromToken()
    {
        var client = _factory.CreateClient();
        var email = $"me_{Guid.NewGuid():N}@test.local";
        const string password = "Test1234";

        (await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = email,
            Password = password,
            FullName = "Me User",
            Role = "WarehouseManager"
        })).EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = password });
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>())!.Token;

        // The user id is whatever the token carries in "sub"
        var expectedId = new JwtSecurityTokenHandler().ReadJwtToken(token).Subject;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(expectedId, root.GetProperty("id").GetString());
        Assert.Equal(email, root.GetProperty("email").GetString());
        Assert.Equal(new[] { "WarehouseManager" }, root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray());
    }
}
