using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

internal static class AuthTestHelper
{
    // The Admin every test host starts with (CustomWebApplicationFactory sets SeedAdmin:* to these values). Throwaway, tests only.
    public const string SeedAdminEmail = "seed-admin@test.local";
    public const string SeedAdminPassword = "Admin1234";

    // Creates a fresh account with the given role and logs in, returning its JWT.
    // BanHang goes through the public POST /api/auth/register (the only role it grants to a stranger); Admin and Kho
    // accounts are created the way the real system does it, by the seeded Admin through POST /api/admin/users.
    public static async Task<string> RegisterAndLoginAsync(HttpClient client, string role)
    {
        var email = $"{role.ToLowerInvariant()}_{Guid.NewGuid():N}@test.local";
        const string password = "Test1234";

        if (role == "BanHang")
        {
            var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
            {
                Email = email,
                Password = password,
                FullName = "Test User",
                Role = role
            });
            registerResponse.EnsureSuccessStatusCode();
        }
        else
        {
            var adminToken = await LoginAsync(client, SeedAdminEmail, SeedAdminPassword);
            // A separate request: the caller's client keeps whatever default headers it has
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
            {
                Content = JsonContent.Create(new AdminCreateUserDto
                {
                    Email = email,
                    TemporaryPassword = password,
                    FullName = "Test User",
                    Roles = new List<string> { role }
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            (await client.SendAsync(request)).EnsureSuccessStatusCode();
        }

        return await LoginAsync(client, email, password);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = email,
            Password = password
        });
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        return auth!.Token;
    }
}
