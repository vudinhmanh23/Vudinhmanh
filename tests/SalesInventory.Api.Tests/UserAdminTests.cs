using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

public class UserAdminTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/admin/users";
    private readonly CustomWebApplicationFactory _factory;

    public UserAdminTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static AdminCreateUserDto NewUser(string role = "Kho") => new()
    {
        Email = $"u_{Guid.NewGuid():N}@test.local",
        TemporaryPassword = "Test1234",
        FullName = "Staff",
        Roles = new List<string> { role }
    };

    private async Task<HttpClient> ClientAsync(string role)
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<AdminUserDto> CreateAsync(HttpClient admin, AdminCreateUserDto dto)
    {
        var response = await admin.PostAsJsonAsync(Url, dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminUserDto>())!;
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Kho")]
    [InlineData("BanHang")]
    public async Task NonAdmin_Returns403(string role)
    {
        var client = await ClientAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{Url}/x/lock", null)).StatusCode);
    }

    [Fact]
    public async Task Admin_ListsUsers_WithoutSecrets()
    {
        var admin = await ClientAsync("Admin");

        var response = await admin.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUser_Admin_ReturnsDetails_UnknownId_Returns404()
    {
        var admin = await ClientAsync("Admin");
        var dto = NewUser("BanHang");
        var created = await CreateAsync(admin, dto);

        var response = await admin.GetAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = (await response.Content.ReadFromJsonAsync<AdminUserDto>())!;
        Assert.Equal(dto.Email, user.Email);
        Assert.Equal(dto.Email, user.UserName);
        Assert.Equal(new[] { "BanHang" }, user.Roles);
        Assert.False(user.IsLockedOut);
        Assert.DoesNotContain("passwordHash", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task GetUser_AuthChecks()
    {
        var url = $"{Url}/{Guid.NewGuid()}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClientAsync("BanHang")).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateEmail_Returns409()
    {
        var admin = await ClientAsync("Admin");
        var dto = NewUser();
        await CreateAsync(admin, dto);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Url, dto)).StatusCode);
    }

    [Fact]
    public async Task Create_InvalidInput_Returns400()
    {
        var admin = await ClientAsync("Admin");

        var badEmail = NewUser();
        badEmail.Email = "not-an-email";
        var weak = NewUser();
        weak.TemporaryPassword = "abc";
        var unknownRole = NewUser("Boss");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url, badEmail)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url, weak)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url, unknownRole)).StatusCode);
    }

    [Fact]
    public async Task SetRoles_ReplacesRoles()
    {
        var admin = await ClientAsync("Admin");
        var user = await CreateAsync(admin, NewUser("Kho"));

        var response = await admin.PutAsJsonAsync($"{Url}/{user.Id}/roles",
            new SetUserRolesDto { Roles = new List<string> { "BanHang" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AdminUserDto>();
        Assert.Equal(new[] { "BanHang" }, updated!.Roles);
    }

    [Fact]
    public async Task SetRoles_UnknownRole_Returns400_UnknownUser_Returns404()
    {
        var admin = await ClientAsync("Admin");
        var user = await CreateAsync(admin, NewUser());

        var bad = await admin.PutAsJsonAsync($"{Url}/{user.Id}/roles",
            new SetUserRolesDto { Roles = new List<string> { "Boss" } });
        var missing = await admin.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}/roles",
            new SetUserRolesDto { Roles = new List<string> { "Kho" } });

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task LockThenUnlock_TogglesLoginAndFlag()
    {
        var admin = await ClientAsync("Admin");
        var dto = NewUser();
        var user = await CreateAsync(admin, dto);
        var login = new LoginDto { Email = dto.Email, Password = dto.TemporaryPassword };

        var locked = await admin.PostAsync($"{Url}/{user.Id}/lock", null);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.True((await locked.Content.ReadFromJsonAsync<AdminUserDto>())!.IsLockedOut);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", login)).StatusCode);

        var unlocked = await admin.PostAsync($"{Url}/{user.Id}/unlock", null);
        Assert.False((await unlocked.Content.ReadFromJsonAsync<AdminUserDto>())!.IsLockedOut);
        Assert.Equal(HttpStatusCode.OK,
            (await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", login)).StatusCode);
    }

    [Fact]
    public async Task LockUnknownUser_Returns404()
    {
        var admin = await ClientAsync("Admin");

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"{Url}/{Guid.NewGuid()}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"{Url}/{Guid.NewGuid()}/unlock", null)).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ChangesPassword_AndRejectsWeak()
    {
        var admin = await ClientAsync("Admin");
        var dto = NewUser();
        var user = await CreateAsync(admin, dto);

        var weak = await admin.PostAsJsonAsync($"{Url}/{user.Id}/reset-password",
            new AdminResetPasswordDto { NewPassword = "abc" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var ok = await admin.PostAsJsonAsync($"{Url}/{user.Id}/reset-password",
            new AdminResetPasswordDto { NewPassword = "NewPass1234" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new LoginDto { Email = dto.Email, Password = dto.TemporaryPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login",
            new LoginDto { Email = dto.Email, Password = "NewPass1234" })).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_UnknownUser_Returns404()
    {
        var admin = await ClientAsync("Admin");

        var response = await admin.PostAsJsonAsync($"{Url}/{Guid.NewGuid()}/reset-password",
            new AdminResetPasswordDto { NewPassword = "NewPass1234" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
