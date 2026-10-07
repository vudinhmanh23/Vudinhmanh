using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace SalesInventory.Web.Services;

/// <summary>Stores the JWT in the browser's encrypted session storage (cleared when the tab closes).</summary>
public class TokenStore
{
    private const string Key = "auth_token";

    private readonly ProtectedSessionStorage _storage;
    private readonly ILogger<TokenStore> _logger;

    public TokenStore(ProtectedSessionStorage storage, ILogger<TokenStore> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task SaveAsync(string token)
    {
        try
        {
            await _storage.SetAsync(Key, token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException)
        {
            // Browser storage is unavailable (e.g. during prerendering or after the circuit disconnected)
            _logger.LogWarning(ex, "Could not save the token to session storage.");
        }
    }

    /// <summary>Returns the stored token, or null if there is none or storage cannot be read.</summary>
    public async Task<string?> GetAsync()
    {
        try
        {
            var result = await _storage.GetAsync<string>(Key);
            return result.Success ? result.Value : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException or System.Security.Cryptography.CryptographicException)
        {
            // Unavailable storage or a payload we can no longer decrypt: treat as "not logged in"
            _logger.LogWarning(ex, "Could not read the token from session storage.");
            return null;
        }
    }

    public async Task ClearAsync()
    {
        try
        {
            await _storage.DeleteAsync(Key);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException)
        {
            _logger.LogWarning(ex, "Could not delete the token from session storage.");
        }
    }
}

