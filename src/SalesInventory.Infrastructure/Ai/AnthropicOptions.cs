namespace SalesInventory.Infrastructure.Ai;

// Bound from the "Anthropic" configuration section (connection, models, rules). The limits on cost and use are in AiSafetyOptions. The API key is a secret: set it with user-secrets
// ("Anthropic:ApiKey") or the ANTHROPIC_API_KEY environment variable, never in a committed file.
public class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.anthropic.com/";

    // Value of the anthropic-version header
    public string ApiVersion { get; set; } = "2023-06-01";

    // Tier name used by the rest of the code: "haiku", "sonnet" or "opus"
    public string ModelTier { get; set; } = "sonnet";

    // The ONE place that maps a tier to a concrete model id. Values come from configuration (appsettings.json);
    // check them against the official model list at https://docs.claude.com and change them there, not in code.
    public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Thinking depth for the tiers that support it ("low" keeps simple Q&A cheap and fast); empty = provider default
    public string? Effort { get; set; } = "low";

    public int TimeoutSeconds { get; set; } = 60;

    // The assistant's standing rules (role, scope, refusal rules). Lives in configuration so it can be tuned without a
    // rebuild; "{ShopName}" is replaced by Shop:Name. It must not contain secrets. Empty = the assistant is treated as not configured.
    public string? SystemPrompt { get; set; }

    // Resolves a tier name to a model id, or null when the tier is unknown or has no id configured
    public string? ResolveModel(string tier)
    {
        return Models.TryGetValue(tier, out var model) && !string.IsNullOrWhiteSpace(model) ? model : null;
    }
}
