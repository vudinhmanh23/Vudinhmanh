using System.Text.Encodings.Web;
using System.Text.Json;

namespace SalesInventory.Api.Streaming;

// Writes Server-Sent Events (text/event-stream) to an HTTP response
public static class SseResponse
{
    // Vietnamese stays readable (and ~3x smaller) instead of "à"; safe here because the data is never embedded in HTML
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Call once, before the first event: after this the status line is sent and errors can only be reported as events
    public static void Begin(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no"; // keeps nginx-style proxies from holding the stream back
    }

    // One event: "event: name", "data: {json}", blank line; flushed at once so the client sees it now
    public static async Task WriteAsync(HttpResponse response, string name, object data, CancellationToken cancellationToken)
    {
        await response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(data, Json)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
