using Microsoft.Extensions.Configuration;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Display;
using SalesInventory.Infrastructure.Security;

namespace SalesInventory.Api.Logging;

// Serilog formats every log event to text for the console and the file. These formatters wrap the normal ones and run the finished
// text through the SecretMasker, so a password, token, API key or connection string never reaches a log, wherever it came from:
// a message argument, a property, an exception message or a stack trace. Because the whole rendered line is checked, there is
// no need to remember to mask at each call site.
public static class LogScrubber
{
    // Every host registers the secrets of its own configuration here. They add up instead of replacing each other, so that when
    // several hosts live in one process (the integration tests) none of them loses its secrets. In production there is one host.
    // Until a host registers (and in tools that do not run Program.cs) it only knows the secrets recognisable by their shape,
    // such as "Password=...", "sk-ant-..." and JWTs.
    private static SecretMasker[] _maskers = { new(new ConfigurationBuilder().Build()) };

    public static void Add(SecretMasker masker)
    {
        // Lock-free append: retry if another host registered at the same moment
        SecretMasker[] current, updated;
        do
        {
            current = _maskers;
            updated = current.Append(masker).ToArray();
        }
        while (Interlocked.CompareExchange(ref _maskers, updated, current) != current);
    }

    // First the exact secrets of every host, then the shapes: see SecretMasker.ScrubSecrets for why the order matters
    public static string Scrub(string text) => SecretMasker.ScrubShapes(_maskers.Aggregate(text, (result, masker) => masker.ScrubSecrets(result)));
}

// Human-readable text lines, for example: [12:00:01 INF] HTTP GET /api/products responded 200 in 12.3 ms {"UserId": "..."}
// The properties of the event are appended as JSON, so the console shows the structure as well as the message.
// Configured in appsettings.json: { "type": "SalesInventory.Api.Logging.ScrubbingTextFormatter, SalesInventory.Api", "outputTemplate": "..." }
public sealed class ScrubbingTextFormatter : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _inner;

    public ScrubbingTextFormatter(string outputTemplate)
    {
        _inner = new MessageTemplateTextFormatter(outputTemplate);
    }

    // Normally null: the application-wide secrets (LogScrubber) are used. A test can supply its own masker.
    public SecretMasker? Masker { get; init; }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        _inner.Format(logEvent, buffer);
        output.Write(Masker is { } own ? own.Scrub(buffer.ToString()) : LogScrubber.Scrub(buffer.ToString()));
    }
}

// One JSON object per line (Compact Log Event Format), for files and log tools: every property is a field that can be searched.
// Configured in appsettings.json as "SalesInventory.Api.Logging.ScrubbingJsonFormatter, SalesInventory.Api".
public sealed class ScrubbingJsonFormatter : ITextFormatter
{
    private readonly CompactJsonFormatter _inner = new();

    // Normally null: the application-wide secrets (LogScrubber) are used. A test can supply its own masker.
    public SecretMasker? Masker { get; init; }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        _inner.Format(logEvent, buffer);
        output.Write(Masker is { } own ? own.Scrub(buffer.ToString()) : LogScrubber.Scrub(buffer.ToString()));
    }
}
