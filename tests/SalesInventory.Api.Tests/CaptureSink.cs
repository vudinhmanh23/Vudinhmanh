using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace SalesInventory.Api.Tests;

// Keeps every log event as the text a person would read in the console ("tier=sonnet inputTokens=10 ..."), so a test can check what the
// application logged. Register it in the test host with services.AddSingleton<ILogEventSink>(sink): Serilog picks it up from DI.
public sealed class CaptureSink : ILogEventSink
{
    // "lj": strings without quotes, other values as JSON: the same rendering the console formatter uses
    private static readonly MessageTemplateTextFormatter Formatter = new("{Message:lj}");

    private readonly ConcurrentQueue<string> _messages;

    public CaptureSink(ConcurrentQueue<string> messages) => _messages = messages;

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        Formatter.Format(logEvent, writer);
        _messages.Enqueue(writer.ToString());
    }
}
