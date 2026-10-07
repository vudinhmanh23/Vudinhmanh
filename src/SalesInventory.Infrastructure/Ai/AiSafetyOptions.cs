using Microsoft.Extensions.Options;

namespace SalesInventory.Infrastructure.Ai;

// Every number that limits what the AI assistant may cost or do, in ONE place: the "AiSafety" section of appsettings.json.
// (Connection settings, model names and the system prompt stay in the "Anthropic" section.) Bound with the Options pattern
// and validated when the application starts, so a bad value stops the app instead of silently weakening a limit.
public class AiSafetyOptions
{
    public const string SectionName = "AiSafety";

    // Upper bound for one model answer, in tokens (thinking counts toward it). Sent as max_tokens on every request.
    public int MaxTokens { get; set; } = 1024;

    // Longest question accepted, in characters; a longer one is rejected with HTTP 400 before any provider call is made
    public int MaxQuestionLength { get; set; } = 2000;

    // How many earlier messages of a conversation are sent to the model as context (older ones are dropped; they cost tokens)
    public int MaxHistoryMessages { get; set; } = 20;

    // How many times the model may ask for tools before the assistant stops and answers with what it has
    public int MaxToolRounds { get; set; } = 4;

    // A question of at most this many characters is answered by the cheaper ShortQuestionTier instead of Anthropic:ModelTier.
    // 0 = off (every question uses Anthropic:ModelTier).
    public int ShortQuestionMaxLength { get; set; } = 80;

    // Tier for short questions: "haiku", "sonnet" or "opus" (a key of Anthropic:Models)
    public string ShortQuestionTier { get; set; } = "haiku";

    // How often one signed-in user may call the AI endpoints
    public RateLimitSettings RateLimit { get; set; } = new();

    // Price per tier, used only to estimate the cost shown in the usage log. Set it from the provider's price list;
    // a tier without a price is logged as "unknown" instead of a wrong number.
    public Dictionary<string, TierPrice> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Label printed next to the estimated cost in the log (the prices above are in this currency)
    public string Currency { get; set; } = "USD";

    // The estimated cost of one request, or null when the tier has no price configured
    public decimal? EstimateCost(string tier, int inputTokens, int outputTokens)
    {
        if (!Pricing.TryGetValue(tier, out var price))
        {
            return null;
        }

        return (inputTokens * price.InputPerMillionTokens + outputTokens * price.OutputPerMillionTokens) / 1_000_000m;
    }

    // The tier that answers this question: the cheap tier for a short one, defaultTier otherwise. Always lower case.
    public string TierFor(string question, string defaultTier)
    {
        var tier = ShortQuestionMaxLength > 0 && question.Length <= ShortQuestionMaxLength && !string.IsNullOrWhiteSpace(ShortQuestionTier)
            ? ShortQuestionTier
            : defaultTier;
        return tier.ToLowerInvariant();
    }
}

public class RateLimitSettings
{
    // Requests one user may make per window. The three AI endpoints share this one quota, because each call costs money.
    public int PermitLimit { get; set; } = 10;

    // Length of the window in seconds
    public int WindowSeconds { get; set; } = 60;

    // "FixedWindow": the counter resets when each window ends (a user can burst across a boundary).
    // "SlidingWindow": the window moves in steps, which smooths that burst.
    public string Algorithm { get; set; } = "FixedWindow";

    // SlidingWindow only: how many steps the window is divided into
    public int SegmentsPerWindow { get; set; } = 6;

    public bool IsSliding => Algorithm.Equals("SlidingWindow", StringComparison.OrdinalIgnoreCase);

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}

// Money per one million tokens, in AiSafety:Currency
public class TierPrice
{
    public decimal InputPerMillionTokens { get; set; }

    public decimal OutputPerMillionTokens { get; set; }
}

// Rejects values that would turn a limit off or break it, and says which setting is wrong
public sealed class AiSafetyOptionsValidator : IValidateOptions<AiSafetyOptions>
{
    public ValidateOptionsResult Validate(string? name, AiSafetyOptions options)
    {
        var errors = new List<string>();

        if (options.MaxTokens is < 1 or > 128_000)
        {
            errors.Add("AiSafety:MaxTokens must be between 1 and 128000.");
        }

        if (options.MaxQuestionLength is < 1 or > 100_000)
        {
            errors.Add("AiSafety:MaxQuestionLength must be between 1 and 100000.");
        }

        if (options.MaxHistoryMessages is < 0 or > 200)
        {
            errors.Add("AiSafety:MaxHistoryMessages must be between 0 and 200.");
        }

        if (options.MaxToolRounds is < 0 or > 20)
        {
            errors.Add("AiSafety:MaxToolRounds must be between 0 and 20.");
        }

        if (options.ShortQuestionMaxLength < 0)
        {
            errors.Add("AiSafety:ShortQuestionMaxLength must be 0 (off) or more.");
        }

        var rate = options.RateLimit;
        if (rate.PermitLimit < 1)
        {
            errors.Add("AiSafety:RateLimit:PermitLimit must be at least 1.");
        }

        if (rate.WindowSeconds < 1)
        {
            errors.Add("AiSafety:RateLimit:WindowSeconds must be at least 1.");
        }

        if (!rate.Algorithm.Equals("FixedWindow", StringComparison.OrdinalIgnoreCase) && !rate.IsSliding)
        {
            errors.Add("AiSafety:RateLimit:Algorithm must be FixedWindow or SlidingWindow.");
        }

        if (rate.SegmentsPerWindow < 2)
        {
            errors.Add("AiSafety:RateLimit:SegmentsPerWindow must be at least 2.");
        }

        foreach (var (tier, price) in options.Pricing)
        {
            if (price.InputPerMillionTokens < 0 || price.OutputPerMillionTokens < 0)
            {
                errors.Add($"AiSafety:Pricing:{tier} must not be negative.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
