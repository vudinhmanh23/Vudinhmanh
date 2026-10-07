using System.Diagnostics;
using System.Security.Claims;
using Serilog;
using Serilog.Events;

namespace SalesInventory.Api.Logging;

// What the one-line-per-request log (UseSerilogRequestLogging) says and how loud it is. It records the method, the path WITHOUT its
// query string, the status code and the time taken. It never records headers (Authorization carries the token), cookies or bodies
// (passwords, chat messages), and the user is identified only by the opaque Identity id, never by e-mail or name.
public static class RequestLogging
{
    public const string MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";

    // Failures stand out: 5xx (or an exception that got out) is an Error, 4xx (rejected, unauthorized, throttled) a Warning, and
    // health probes are Debug so they do not fill the log
    public static LogEventLevel GetLevel(HttpContext context, double elapsedMs, Exception? exception)
    {
        if (exception is not null || context.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        if (context.Response.StatusCode >= 400)
        {
            return LogEventLevel.Warning;
        }

        return context.Request.Path.StartsWithSegments("/api/health") ? LogEventLevel.Debug : LogEventLevel.Information;
    }

    // Context added to the request line, so a log entry can be tied to a user, a client and a response body (ProblemDetails traceId)
    public static void Enrich(IDiagnosticContext diagnosticContext, HttpContext context)
    {
        diagnosticContext.Set("UserId", context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous");
        diagnosticContext.Set("ClientIp", context.Connection.RemoteIpAddress?.ToString());
        // The same id the error responses carry in "traceId", so a user report can be found in the log
        diagnosticContext.Set("TraceId", Activity.Current?.Id ?? context.TraceIdentifier);
    }
}
