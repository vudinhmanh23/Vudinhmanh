using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;

namespace SalesInventory.Api.Security;

// Writes a security log entry every time the API answers 401 (not authenticated) or 403 (not allowed), whoever produced the answer:
// the JWT middleware, the authorization policies or a controller (a wrong password at login is a 401 too). A burst of them from one
// address is the signature of password guessing or of someone probing which endpoints exist, so repeated failures raise an Error.
//
// What is logged: the kind of failure, the status, the method, the path WITHOUT its query string, the client address and, for a
// signed-in user, the opaque user id; and only whether a bearer token was SENT, never the token. What is never logged: the
// Authorization header, any other header, cookies, the body (passwords) or the query string. A failed login does not record which
// e-mail was tried, because that is personal data and a typo of a real user would be stored.
public sealed class AuthFailureLoggingMiddleware
{
    private const int MaxLoggedPathLength = 200;

    private readonly RequestDelegate _next;
    private readonly ILogger<AuthFailureLoggingMiddleware> _logger;
    private readonly AuthFailureTracker _tracker;
    private readonly AuthFailureOptions _options;

    public AuthFailureLoggingMiddleware(
        RequestDelegate next,
        ILogger<AuthFailureLoggingMiddleware> logger,
        AuthFailureTracker tracker,
        IOptions<AuthFailureOptions> options)
    {
        _next = next;
        _logger = logger;
        _tracker = tracker;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Wraps authentication, authorization and the controllers, so it sees the final status of every request
        await _next(context);

        var status = context.Response.StatusCode;
        if (status is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden))
        {
            return;
        }

        var path = Sanitize(context.Request.Path.Value);
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var tokenSent = context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        var userId = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;

        var reason = status == StatusCodes.Status403Forbidden ? "Forbidden"
            : path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ? "LoginFailed"
            : tokenSent ? "InvalidOrExpiredToken"
            : "MissingCredentials";

        _logger.LogWarning(
            "Security: {SecurityEvent} ({Reason}) {StatusCode} on {RequestMethod} {RequestPath} from {ClientIp}, user {UserId}, bearer token sent: {TokenSent}",
            "AuthFailure", reason, status, context.Request.Method, path, client, userId ?? "anonymous", tokenSent);

        var failures = _tracker.Record(client);
        var threshold = Math.Max(1, _options.AlertThreshold);
        if (failures >= threshold && failures % threshold == 0)
        {
            _logger.LogError(
                "Security: {SecurityEvent}: {FailureCount} authentication or authorization failures from {ClientIp} within {WindowMinutes} minutes. Possible password guessing or scanning",
                "SuspiciousActivity", failures, client, Math.Max(1, _options.WindowMinutes));
        }
    }

    // The path comes from the client. A line break in it could forge a second log line in a text log, so control characters are
    // replaced, and a very long one is cut.
    private static string Sanitize(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        var text = new StringBuilder(Math.Min(path.Length, MaxLoggedPathLength));
        foreach (var c in path.Take(MaxLoggedPathLength))
        {
            text.Append(char.IsControl(c) ? '?' : c);
        }

        return text.ToString();
    }
}
