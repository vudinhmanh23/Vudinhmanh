using System.Text.Json;

namespace SalesInventory.Api.Tests;

// Swagger UI shows a padlock only on [Authorize]d actions, driven by per-operation "security" entries
public class SwaggerSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SwaggerSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BearerSchemeIsDefined_AsHttpBearerJwt()
    {
        using var doc = await GetSwaggerAsync();

        var bearer = doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString(), ignoreCase: true);
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
    }

    [Theory]
    [InlineData("/api/products", "post", true)]
    [InlineData("/api/products", "get", true)]
    [InlineData("/api/suppliers", "get", true)]
    [InlineData("/api/auth/me", "get", true)]
    [InlineData("/api/auth/login", "post", false)]
    [InlineData("/api/auth/register", "post", false)]
    [InlineData("/api/health", "get", false)]
    public async Task Padlock_OnlyOnAuthorizedOperations(string path, string method, bool expectLock)
    {
        using var doc = await GetSwaggerAsync();

        // Swashbuckle writes paths with the controller name's casing (e.g. /api/Products)
        var paths = doc.RootElement.GetProperty("paths").EnumerateObject();
        var operation = paths.Single(p => string.Equals(p.Name, path, StringComparison.OrdinalIgnoreCase))
            .Value.GetProperty(method);
        var hasSecurity = operation.TryGetProperty("security", out var security) && security.GetArrayLength() > 0;

        Assert.Equal(expectLock, hasSecurity);
    }

    private async Task<JsonDocument> GetSwaggerAsync()
    {
        var response = await _factory.CreateClient().GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
