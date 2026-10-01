using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

public class AdminEmployeesTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/admin/employees";
    private readonly CustomWebApplicationFactory _factory;

    public AdminEmployeesTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static CreateEmployeeDto NewEmployee(string role = "Kho", string? email = null) => new()
    {
        Email = email ?? $"emp_{Guid.NewGuid():N}@test.local",
        Password = "Test1234",
        FullName = "Employee",
        Role = role
    };

    private async Task<HttpClient> AdminClientAsync(string role = "Admin")
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(Url, NewEmployee());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Kho")]
    [InlineData("BanHang")]
    public async Task NonAdmin_Returns403(string callerRole)
    {
        var client = await AdminClientAsync(callerRole);

        var response = await client.PostAsJsonAsync(Url, NewEmployee());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Kho")]
    [InlineData("BanHang")]
    public async Task Admin_CreatesEmployee_CanLoginWithRole(string role)
    {
        var admin = await AdminClientAsync();
        var dto = NewEmployee(role);

        var response = await admin.PostAsJsonAsync(Url, dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(new[] { role }, created!.Roles);

        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginDto { Email = dto.Email, Password = dto.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Boss")]
    public async Task InvalidRole_Returns400(string role)
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(Url, NewEmployee(role));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateEmail_Returns409()
    {
        var admin = await AdminClientAsync();
        var dto = NewEmployee();
        (await admin.PostAsJsonAsync(Url, dto)).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync(Url, dto);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task WeakPassword_Returns400()
    {
        var admin = await AdminClientAsync();
        var dto = NewEmployee();
        dto.Password = "abc";

        var response = await admin.PostAsJsonAsync(Url, dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
