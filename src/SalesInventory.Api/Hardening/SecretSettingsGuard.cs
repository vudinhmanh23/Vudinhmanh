using Microsoft.Extensions.Configuration;

namespace SalesInventory.Api.Hardening;

// Production start-up checks for secrets. A secret must reach the application from the environment (or a secret store), never from
// a file that is built into the image and committed to source control; and the application must not start half-configured.
public static class SecretSettingsGuard
{
    // The settings that hold secrets. The first four are required in production or the assistant/identity cannot work; the rest
    // are optional but must still never come from a file.
    private static readonly string[] SecretKeys =
    {
        "Jwt:Key",
        "ConnectionStrings:DefaultConnection",
        "Anthropic:ApiKey",
        "Embeddings:ApiKey",
        "SeedAdmin:Password"
    };

    // Throws when a settings FILE (appsettings.json, appsettings.Production.json, ...) supplies a secret. Values from environment
    // variables, command-line arguments or a secret store are fine.
    public static void EnsureNoSecretsInSettingsFiles(IConfigurationRoot configuration)
    {
        var fromFiles = new List<string>();

        foreach (var provider in configuration.Providers.OfType<FileConfigurationProvider>())
        {
            foreach (var key in SecretKeys)
            {
                if (provider.TryGet(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    fromFiles.Add(key);
                }
            }
        }

        if (fromFiles.Count > 0)
        {
            throw new InvalidOperationException(
                $"Secrets found in a settings file: {string.Join(", ", fromFiles.Distinct())}. In production secrets must come from " +
                "environment variables (for example ConnectionStrings__DefaultConnection, Jwt__Key) or a secret store, never from a " +
                "file shipped with the application. Remove them from the file and set them in the environment.");
        }
    }

    // Production does not start without a database connection (it would only fail later, at the first request)
    public static void EnsureRequiredSettings(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
        {
            throw new InvalidOperationException(
                "The database connection string is not set. Set the environment variable ConnectionStrings__DefaultConnection.");
        }
    }
}
