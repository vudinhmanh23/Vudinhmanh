using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Dtos;
using SalesInventory.Infrastructure.Identity;

namespace SalesInventory.Api.Tests;

public class AdminSeederTests
{
    // Throwaway credentials used only inside these tests
    private const string Email = "seeded-admin@test.local";
    private const string Password = "Seed1234";

    private sealed class SeedAdminFactory(string? email, string? password) : CustomWebApplicationFactory
    {
        protected override void ConfigureExtraSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("SeedAdmin:Email", email);
            builder.UseSetting("SeedAdmin:Password", password);
        }
    }

    private static CustomWebApplicationFactory FactoryWithSeedAdmin(string? email, string? password) =>
        new SeedAdminFactory(email, password);

    [Fact]
    public async Task SeedAdmin_FromConfig_CanLoginWithAdminRole()
    {
        using var factory = FactoryWithSeedAdmin(Email, Password);
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginDto { Email = Email, Password = Password });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<AuthResponseDto>())!.Token;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task SeedAdmin_RunTwice_IsIdempotent()
    {
        using var factory = FactoryWithSeedAdmin(Email, Password);
        _ = factory.CreateClient();

        await RoleSeeder.SeedAdminAsync(factory.Services);
        await RoleSeeder.SeedAdminAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Single(users.Users.Where(u => u.Email == Email));
    }

    [Fact]
    public async Task NoSeedConfig_CreatesNoAdmin()
    {
        using var factory = FactoryWithSeedAdmin(null, null);
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Empty(await users.GetUsersInRoleAsync(AppRoles.Admin));
    }
}
