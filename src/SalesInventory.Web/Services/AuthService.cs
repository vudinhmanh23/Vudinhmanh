using System.Net.Http.Json;

namespace SalesInventory.Web.Services;

public record LoginResult(bool Success, string? Error = null);

/// <summary>Logs in against the API and keeps the returned JWT in the <see cref="TokenStore"/>.</summary>
public class AuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenStore _tokenStore;
    private readonly JwtAuthenticationStateProvider _authState;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IHttpClientFactory httpClientFactory, TokenStore tokenStore,
        JwtAuthenticationStateProvider authState, ILogger<AuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStore = tokenStore;
        _authState = authState;
        _logger = logger;
    }

    /// <summary>Calls POST /api/auth/login. The API identifies users by email, so the first argument is the email.</summary>
    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ApiClient.Name);
            var response = await client.PostAsJsonAsync("api/auth/login", new { email, password });

            if (!response.IsSuccessStatusCode)
            {
                return new LoginResult(false, await ReadErrorAsync(response));
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (auth is null || string.IsNullOrEmpty(auth.Token))
            {
                return new LoginResult(false, "Phản hồi đăng nhập không hợp lệ");
            }

            await _tokenStore.SaveAsync(auth.Token);
            _authState.NotifyStateChanged();
            return new LoginResult(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // API is down, unreachable, timed out, or returned a body we cannot parse
            _logger.LogError(ex, "Login request failed.");
            return new LoginResult(false, "Không thể kết nối tới máy chủ. Vui lòng thử lại sau");
        }
    }

    public async Task LogoutAsync()
    {
        await _tokenStore.ClearAsync();
        _authState.NotifyStateChanged();
    }

    // The API reports failures as { "message": "..." }; fall back to a generic text if it is missing
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
            if (!string.IsNullOrWhiteSpace(body?.Message))
            {
                return body.Message;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not a JSON error body; use the generic message below
        }

        return "Đăng nhập thất bại";
    }

    private record AuthResponse(string Token, DateTime ExpiresAt);

    private record ErrorBody(string? Message);
}

