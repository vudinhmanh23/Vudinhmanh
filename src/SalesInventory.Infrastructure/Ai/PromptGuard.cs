using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SalesInventory.Infrastructure.Security;

namespace SalesInventory.Infrastructure.Ai;

// Code-level guards around the model, so the safety of the assistant does not rest on the system prompt alone:
//  1. Secrets (API keys, the JWT key, database connection strings) are removed from everything that goes INTO a prompt
//     (user text, history, documents, tool results) and from everything that comes OUT of the model.
//  2. A verbatim dump of the system prompt is recognised in the model's output, so it can be cut off.
// A prompt is only a request to the model; these checks run on the server and cannot be talked out of anything.
public sealed class PromptGuard
{
    public const string Mask = SecretMasker.MaskText;

    // The system prompt counts as leaked when the output repeats this many characters of it in a row. It is long enough
    // that the fixed sentences the prompt tells the assistant to say (about 70 characters) do not trigger it.
    public const int LeakWindow = 120;

    // The rule for what is a secret lives in SecretMasker (it also protects the log files)
    private readonly SecretMasker _masker;

    public PromptGuard(IConfiguration configuration, IOptions<AnthropicOptions> anthropic, IOptions<EmbeddingOptions> embeddings)
    {
        // The options also get values from places such as the ANTHROPIC_API_KEY environment variable
        _masker = new SecretMasker(configuration, anthropic.Value.ApiKey, embeddings.Value.ApiKey);
    }

    // The text with every known secret (and anything shaped like one) replaced by a mask
    public string Scrub(string text) => _masker.Scrub(text);

    // True when the text still holds a configured secret (used on the system prompt, which must never contain one)
    public bool ContainsSecret(string text) => _masker.ContainsSecret(text);

    // While streaming, a secret can arrive split across two pieces. This is how many characters at the end of `pending` must be
    // kept back for now: the longest tail that could be the beginning of a secret, or that is still part of a run copied from the
    // system prompt. Normal text gives 0 or a few characters.
    public int HeldBackLength(string pending, string systemPrompt)
    {
        var hold = 0;

        foreach (var secret in _masker.Secrets)
        {
            for (var length = Math.Min(pending.Length, secret.Length - 1); length > hold; length--)
            {
                if (pending.AsSpan(pending.Length - length).SequenceEqual(secret.AsSpan(0, length)))
                {
                    hold = length;
                    break;
                }
            }
        }

        // Only a tail that is already at least this long can be the start of a copy; shorter ones match ordinary words
        const int MinRun = 16;
        for (var length = Math.Min(pending.Length, LeakWindow - 1); length >= MinRun && length > hold; length--)
        {
            if (systemPrompt.AsSpan().Contains(pending.AsSpan(pending.Length - length), StringComparison.Ordinal))
            {
                hold = length;
                break;
            }
        }

        return hold;
    }

    // True when the text repeats LeakWindow or more characters of the system prompt in a row
    public bool LeaksSystemPrompt(string text, string systemPrompt)
    {
        if (text.Length < LeakWindow || systemPrompt.Length < LeakWindow)
        {
            return false;
        }

        for (var start = 0; start + LeakWindow <= text.Length; start++)
        {
            if (systemPrompt.AsSpan().Contains(text.AsSpan(start, LeakWindow), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
