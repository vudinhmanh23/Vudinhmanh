using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SalesInventory.Web.Services;

/// <summary>Reads the claims we need from a JWT payload. It does not verify the signature: the API does that on every call.</summary>
public static class JwtParser
{
    // JwtSecurityTokenHandler writes ClaimTypes.Role as "role" but other issuers may use the long URI
    private static readonly string[] RoleKeys = { "role", ClaimTypes.Role };

    /// <summary>Returns the principal for a token, or null if the token is malformed or already expired.</summary>
    public static ClaimsPrincipal? TryCreatePrincipal(string token, DateTime utcNow)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            var payload = doc.RootElement;

            if (payload.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                && DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime <= utcNow)
            {
                return null;
            }

            var claims = new List<Claim>();

            // The token has no name claim, so the email is shown as the user name
            var name = payload.TryGetProperty("email", out var email) ? email.GetString() : null;
            claims.Add(new Claim(ClaimTypes.Name, string.IsNullOrEmpty(name) ? "Người dùng" : name));

            foreach (var key in RoleKeys)
            {
                if (!payload.TryGetProperty(key, out var roles))
                {
                    continue;
                }

                // One role is serialized as a string, several as an array
                var values = roles.ValueKind == JsonValueKind.Array
                    ? roles.EnumerateArray().Select(r => r.GetString())
                    : new[] { roles.GetString() };
                claims.AddRange(values.Where(r => !string.IsNullOrEmpty(r)).Select(r => new Claim(ClaimTypes.Role, r!)));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt", ClaimTypes.Name, ClaimTypes.Role));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
