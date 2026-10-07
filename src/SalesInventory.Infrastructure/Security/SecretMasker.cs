using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace SalesInventory.Infrastructure.Security;

// Finds the application's secrets (API keys, the JWT key, database passwords and connection strings) and replaces them in any
// text. One place for this rule, used by everything that must never reveal a secret: what is sent to the AI model (PromptGuard)
// and what is written to the log files (the Serilog formatters in the API project).
public sealed class SecretMasker
{
    public const string MaskText = "[đã ẩn]";

    // A secret shorter than this is ignored: masking short strings would damage ordinary text
    private const int MinSecretLength = 8;

    // Configuration keys that hold secrets, matched by name so a secret added later is covered without changing this class
    private static readonly Regex SecretKeyName = new(
        @"(api_?key|secret|password|pwd|token|connectionstring|:key$|^key$|^connectionstrings:)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Secrets that are recognisable by their shape even when they are not configured here
    private static readonly Regex[] Shapes =
    {
        new(@"sk-ant-[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled),
        new(@"(?i)\b(password|pwd)\s*=\s*[^;\s""]+", RegexOptions.Compiled),
        // A JWT: three base64url parts, the first two starting with "eyJ" ({" in base64). Covers a bearer token pasted into any text.
        new(@"eyJ[A-Za-z0-9_\-]{8,}\.eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}", RegexOptions.Compiled)
    };

    // Every secret value, longest first, in its plain form and as it looks inside a JSON string (escaped quotes and backslashes)
    public IReadOnlyList<string> Secrets { get; }

    // `extraSecrets` are values known from other places (for example options that were filled from environment variables)
    public SecretMasker(IConfiguration configuration, params string?[] extraSecrets)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, value) in configuration.AsEnumerable())
        {
            if (!string.IsNullOrEmpty(value) && SecretKeyName.IsMatch(key))
            {
                found.Add(value);
            }
        }

        foreach (var extra in extraSecrets)
        {
            if (!string.IsNullOrEmpty(extra))
            {
                found.Add(extra);
            }
        }

        // A connection string in a JSON log line has its backslashes and quotes escaped, so it would not match its plain form
        foreach (var secret in found.ToList())
        {
            found.Add(secret.Replace("\\", "\\\\").Replace("\"", "\\\""));
        }

        Secrets = found.Where(s => s.Length >= MinSecretLength).OrderByDescending(s => s.Length).ToArray();
    }

    // The text with every known secret (and anything shaped like one) replaced by a mask
    public string Scrub(string text) => ScrubShapes(ScrubSecrets(text));

    // Only the exact secret values of this configuration. The exact values must be masked BEFORE the shapes: a shape such as
    // "Password=..." would otherwise rewrite part of a connection string and the whole value would no longer match.
    public string ScrubSecrets(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var secret in Secrets)
        {
            if (text.Contains(secret, StringComparison.Ordinal))
            {
                text = text.Replace(secret, MaskText, StringComparison.Ordinal);
            }
        }

        return text;
    }

    // Only the things recognisable by their shape (a "Password=..." pair, an sk-ant- key, a JWT); needs no configuration
    public static string ScrubShapes(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var shape in Shapes)
        {
            text = shape.Replace(text, MaskText);
        }

        return text;
    }

    // True when the text still holds a configured secret
    public bool ContainsSecret(string text)
    {
        return Secrets.Any(secret => text.Contains(secret, StringComparison.Ordinal));
    }
}
