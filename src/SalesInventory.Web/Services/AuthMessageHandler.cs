using System.Net;
using System.Net.Http.Headers;

namespace SalesInventory.Web.Services;

/// <summary>Adds the stored JWT to outgoing API requests and clears it when the API answers 401.</summary>
public class AuthMessageHandler : DelegatingHandler
{
    private readonly CircuitServicesAccessor _accessor;

    public AuthMessageHandler(CircuitServicesAccessor accessor)
    {
        _accessor = accessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Resolve the circuit's TokenStore; null when the call is made outside a Blazor circuit
        var tokenStore = _accessor.Services?.GetService<TokenStore>();

        if (tokenStore is not null)
        {
            var token = await tokenStore.GetAsync();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && tokenStore is not null)
        {
            // Token is expired or invalid: drop it so the UI can send the user back to login
            await tokenStore.ClearAsync();
            _accessor.Services?.GetService<JwtAuthenticationStateProvider>()?.NotifyStateChanged();
        }

        return response;
    }
}

