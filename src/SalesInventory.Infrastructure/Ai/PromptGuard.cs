using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace SalesInventory.Infrastructure.Ai;

// Code-level guards around the model, so the safety of the assistant does not rest on the system prompt alone:
//  1. Secrets (API keys, the JWT key, database connection strings) are removed from everything that goes INTO a prompt
//     (user text, history, documents, tool results) and from everything that comes OUT of the model.
//  2. A verbatim dump of the system prompt is recognised in the model's output, so it can be cut off.
// A prompt is only a request to the model; these checks run on the server and cannot be talked out of anything.
public sealed class PromptGuard
{
    public const string Mask = "[đã ẩn]";

    // A secret shorter than this is ignored: masking short strings would damage ordinary text
    private const int MinSecretLength = 8;

    // The system prompt counts as leaked when the output repeats this many characters of it in a row. It is long enough
    // that the fixed sentences the prompt tells the assistant to say (about 70 characters) do not trigger it.
    public const int LeakWindow = 120;

    // Configuration keys that hold secrets, matched by name so a secret added later is covered without changing this class
    private static readonly Regex SecretKeyName = new(
        @"(api_?key|secret|password|pwd|token|connectionstring|:key$|^key$|^connectionstrings:)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Secrets that are recognisable by their shape even when they are not configured here
    private static readonly Regex[] Shapes =
    {
        new(@"sk-ant-[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled),
        new(@"(?i)\b(password|pwd)\s*=\s*[^;\s]+", RegexOptions.Compiled)
    };

    private readonly string[] _secrets;

    public PromptGuard(IConfiguration configuration, IOptions<AnthropicOptions> anthropic, IOptions<EmbeddingOptions> embeddings)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, value) in configuration.AsEnumerable())
        {
            if (!string.IsNullOrEmpty(value) && SecretKeyName.IsMatch(key))
            {
                found.Add(value);
            }
        }

        // The options also get values from places such as the ANTHROPIC_API_KEY environment variable
        found.Add(anthropic.Value.ApiKey ?? string.Empty);
        found.Add(embeddings.Value.ApiKey ?? string.Empty);

        // Longest first, so a secret that contains another one is masked as a whole
        _secrets = found.Where(s => s.Length >= MinSecretLength).OrderByDescending(s => s.Length).ToArray();
    }

    // The text with every known secret (and anything shaped like one) replaced by a mask
    public string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var secret in _secrets)
        {
            if (text.Contains(secret, StringComparison.Ordinal))
            {
                text = text.Replace(secret, Mask, StringComparison.Ordinal);
            }
        }

        foreach (var shape in Shapes)
        {
            text = shape.Replace(text, Mask);
        }

        return text;
    }

    // True when the text still holds a configured secret (used on the system prompt, which must never contain one)
    public bool ContainsSecret(string text)
    {
        return _secrets.Any(secret => text.Contains(secret, StringComparison.Ordinal));
    }

    // While streaming, a secret can arrive split across two pieces. This is how many characters at the end of `pending` must be
    // kept back for now: the longest tail that could be the beginning of a secret, or that is still part of a run copied from the
    // system prompt. Normal text gives 0 or a few characters.
    public int HeldBackLength(string pending, string systemPrompt)
    {
        var hold = 0;

        foreach (var secret in _secrets)
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
