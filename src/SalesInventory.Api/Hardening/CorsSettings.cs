namespace SalesInventory.Api.Hardening;

// Reads and checks the origins of the Blazor client that may call this API from a browser ("Cors:AllowedOrigins" in configuration).
public static class CorsSettings
{
    public const string PolicyName = "BlazorClient";

    // The exact origins configured (empty = no browser from another origin may call the API, which is the default).
    // An invalid entry stops the application at start-up, because a wrong CORS setting is either broken or too open.
    public static string[] ReadAllowedOrigins(IConfiguration configuration, bool requireHttps)
    {
        var configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        var origins = new List<string>();

        foreach (var raw in configured)
        {
            var origin = (raw ?? string.Empty).Trim().TrimEnd('/');
            if (origin.Length == 0)
            {
                continue; // an empty entry (for example from an unset environment variable) means "none"
            }

            // "*" and "https://*.example.com" are patterns, not origins: they would let strangers in
            if (origin.Contains('*')
                || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins contains an invalid origin '{raw}'. Use exact origins such as https://app.example.com " +
                    "(a scheme, a host and an optional port; no '*', no path, no query).");
            }

            if (requireHttps && uri.Scheme != "https")
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins contains '{raw}', but in production the client must be served over https.");
            }

            origins.Add(uri.GetLeftPart(UriPartial.Authority));
        }

        return origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
