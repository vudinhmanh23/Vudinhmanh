using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// Supplier Code uniqueness and soft delete behaviour
public class SuppliersCodeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SuppliersCodeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns400()
    {
        var client = await AdminClientAsync();
        var code = $"DUP-{Guid.NewGuid():N}"[..20];

        var first = await client.PostAsJsonAsync("/api/suppliers", new { code, name = "A" });
        var second = await client.PostAsJsonAsync("/api/suppliers", new { code = code.ToLowerInvariant(), name = "B" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Update_CodeOfAnotherSupplier_Returns400()
    {
        var client = await AdminClientAsync();
        var codeA = $"UA-{Guid.NewGuid():N}"[..20];
        var codeB = $"UB-{Guid.NewGuid():N}"[..20];
        await client.PostAsJsonAsync("/api/suppliers", new { code = codeA, name = "A" });
        var created = await (await client.PostAsJsonAsync("/api/suppliers", new { code = codeB, name = "B" }))
            .Content.ReadFromJsonAsync<SupplierDto>();

        var response = await client.PutAsJsonAsync($"/api/suppliers/{created!.Id}", new { code = codeA, name = "B", isActive = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_DeactivatesSupplier_AndKeepsRecord()
    {
        var client = await AdminClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/suppliers", new { code = $"DEL-{Guid.NewGuid():N}"[..20], name = "To deactivate" }))
            .Content.ReadFromJsonAsync<SupplierDto>();
        Assert.True(created!.IsActive);

        var delete = await client.DeleteAsync($"/api/suppliers/{created.Id}");
        var after = await client.GetFromJsonAsync<SupplierDto>($"/api/suppliers/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.False(after!.IsActive);
    }

    [Fact]
    public async Task GetActive_ReturnsOnlyActiveSuppliers()
    {
        var client = await AdminClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/suppliers", new { code = $"ACT-{Guid.NewGuid():N}"[..20], name = "Will be inactive" }))
            .Content.ReadFromJsonAsync<SupplierDto>();
        await client.DeleteAsync($"/api/suppliers/{created!.Id}");

        var active = await client.GetFromJsonAsync<List<SupplierDto>>("/api/suppliers/active");

        Assert.NotEmpty(active!);
        Assert.All(active!, s => Assert.True(s.IsActive));
        Assert.DoesNotContain(active!, s => s.Id == created.Id);
    }

    [Fact]
    public async Task Create_InvalidBody_Returns400ProblemDetails()
    {
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/suppliers", new { code = new string('X', 21), name = "", email = "not-an-email" });
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(400, problem!.Status);
        Assert.Contains("Code", problem.Errors.Keys);
        Assert.Contains("Name", problem.Errors.Keys);
        Assert.Contains("Email", problem.Errors.Keys);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("0123456789012")]
    public async Task Update_InvalidPhone_Returns400(string phone)
    {
        var client = await AdminClientAsync();
        var code = $"PH-{Guid.NewGuid():N}"[..20];
        var created = await client.PostAsJsonAsync("/api/suppliers", new { code, name = "Phone test" });
        var supplier = await created.Content.ReadFromJsonAsync<SupplierDto>();

        var response = await client.PutAsJsonAsync($"/api/suppliers/{supplier!.Id}", new { code, name = "Phone test", phone, isActive = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.Contains("Phone", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Update_ValidPhone_Returns204()
    {
        var client = await AdminClientAsync();
        var code = $"PH-{Guid.NewGuid():N}"[..20];
        var created = await client.PostAsJsonAsync("/api/suppliers", new { code, name = "Phone ok" });
        var supplier = await created.Content.ReadFromJsonAsync<SupplierDto>();

        var response = await client.PutAsJsonAsync($"/api/suppliers/{supplier!.Id}", new { code, name = "Phone ok", phone = "0901234567", isActive = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
