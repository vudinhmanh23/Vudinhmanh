namespace SalesInventory.Infrastructure.Ai;

// Bound from the "Embeddings" section. Anthropic has no embeddings model of its own (its docs recommend Voyage AI), so the
// default provider is Voyage; "OpenAI" works for any OpenAI-compatible /embeddings endpoint. The key is a secret: set it with
// user-secrets ("Embeddings:ApiKey") or the VOYAGE_API_KEY environment variable, never in a committed file.
public class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    // "Voyage" (default) or "OpenAI" (OpenAI-compatible)
    public string Provider { get; set; } = "Voyage";

    public string? ApiKey { get; set; }

    // Empty = the provider's default (Voyage: https://ai.mongodb.com/v1/, OpenAI: https://api.openai.com/v1/)
    public string? BaseUrl { get; set; }

    // Empty = the provider's default (Voyage: voyage-4, OpenAI: text-embedding-3-small)
    public string? Model { get; set; }

    // Texts per request
    public int BatchSize { get; set; } = 64;

    public int TimeoutSeconds { get; set; } = 60;

    public bool IsVoyage => !Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase);

    public string ResolveBaseUrl() => !string.IsNullOrWhiteSpace(BaseUrl)
        ? (BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/")
        : IsVoyage ? "https://ai.mongodb.com/v1/" : "https://api.openai.com/v1/";

    public string ResolveModel() => !string.IsNullOrWhiteSpace(Model)
        ? Model
        : IsVoyage ? "voyage-4" : "text-embedding-3-small";
}
