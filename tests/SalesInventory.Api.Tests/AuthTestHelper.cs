using System.Net.Http.Json;
using SalesInventory.Api.Dtos;

namespace SalesInventory.Api.Tests;

internal static class AuthTestHelper
{
    // Registers a fresh account with the given role and logs in, returning its JWT
    public static async Task<string> RegisterAndLoginAsync(HttpClient client, string role)
    {
        var email = $"{role.ToLowerInvariant()}_{Guid.NewGuid():N}@test.local";
        const string password = "Test1234";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = email,
            Password = password,
            Role = role
        });
        registerResponse.EnsureSuccessStatusCode();

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
