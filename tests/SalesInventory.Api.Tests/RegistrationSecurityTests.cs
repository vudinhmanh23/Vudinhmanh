using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// POST /api/auth/register is public, so it must never hand a privileged role to a stranger.
// Before the fix a request with "role": "Admin" created an Admin account.
public class RegistrationSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Test1234";

    private readonly CustomWebApplicationFactory _factory;

    public RegistrationSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string NewEmail(string prefix) => $"{prefix}_{Guid.NewGuid():N}@test.local";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string? role) =>
        client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = email,
            Password = Password,
            FullName = "Registration Test",
            Role = role
        });

    private static async Task<string[]> RolesOfAsync(HttpClient client, string email)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<AuthResponseDto>())!.Token;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.SendAsync(request);
        me.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Kho")]
    public async Task Anonymous_RegisteringPrivilegedRole_Returns403_AndCreatesNoAccount(string role)
    {
        var client = _factory.CreateClient();
        var email = NewEmail(role.ToLowerInvariant());

        var response = await RegisterAsync(client, email, role);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Refused before anything was created: the account cannot log in
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BanHang")]
    public async Task Anonymous_RegisteringDefaultRole_Succeeds_AsBanHangOnly(string? role)
    {
        var client = _factory.CreateClient();
        var email = NewEmail("stranger");

        var response = await RegisterAsync(client, email, role);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "BanHang" }, await RolesOfAsync(client, email));
    }

    [Fact]
    public async Task Anonymous_UnknownRole_StillReturns400()
    {
        var client = _factory.CreateClient();

        var response = await RegisterAsync(client, NewEmail("bogus"), "SuperUser");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignedInBanHang_CannotRegisterAnAdmin()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        var email = NewEmail("escalate");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new RegisterDto { Email = email, Password = Password, FullName = "Escalation", Role = "Admin" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SignedInAdmin_CanRegisterAKhoAccount()
    {
        var client = _factory.CreateClient();
        var adminToken = await AuthTestHelper.RegisterAndLoginAsync(client, "Admin");
        var email = NewEmail("kho");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new RegisterDto { Email = email, Password = Password, FullName = "Kho By Admin", Role = "Kho" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "Kho" }, await RolesOfAsync(client, email));
    }
}
