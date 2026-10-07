using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace SalesInventory.Web.Services;

/// <summary>Builds the user's identity (name and roles) from the JWT held in the <see cref="TokenStore"/>.</summary>
public class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly TokenStore _tokenStore;

    public JwtAuthenticationStateProvider(TokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _tokenStore.GetAsync();
        if (string.IsNullOrEmpty(token))
        {
            return Anonymous;
        }

        var principal = JwtParser.TryCreatePrincipal(token, DateTime.UtcNow);
        if (principal is null)
        {
            // Malformed or expired token: forget it so the user is asked to log in again
            await _tokenStore.ClearAsync();
            return Anonymous;
        }

        return new AuthenticationState(principal);
    }

    /// <summary>Re-reads the token and tells the UI (AuthorizeView, cascading state) that the user may have changed.</summary>
    public void NotifyStateChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
