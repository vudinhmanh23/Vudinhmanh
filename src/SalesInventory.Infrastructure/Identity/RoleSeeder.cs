using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SalesInventory.Infrastructure.Identity;

public static class RoleSeeder
{
    // Creates any missing role from AppRoles.All; safe to run on every startup
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in AppRoles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole(role));
            // Another instance may have created it between the check and the insert
            if (!result.Succeeded && !await roleManager.RoleExistsAsync(role))
            {
                throw new InvalidOperationException(
                    $"Failed to seed role '{role}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }
        }
    }

    // Creates the first Admin account from config "SeedAdmin"; skipped if it already exists
    public static async Task SeedAdminAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = config["SeedAdmin:Email"];
        var password = config["SeedAdmin:Password"];

        // No config = no seeding (e.g. production without explicit setup)
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = config["SeedAdmin:FullName"] ?? "Administrator",
                EmailConfirmed = true
            };

            // Identity validates the password against the configured policy and hashes it
            var result = await userManager.CreateAsync(admin, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed admin (check SeedAdmin:Password against the password policy): {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }
        }

        // Also repairs an existing account that lost its Admin role; the password is never overwritten
        if (!await userManager.IsInRoleAsync(admin, AppRoles.Admin))
        {
            var roleResult = await userManager.AddToRoleAsync(admin, AppRoles.Admin);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to assign Admin role to seeded admin: {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
            }
        }
    }
}