using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using SalesInventory.Infrastructure.Ai;

namespace SalesInventory.Api.RateLimiting;

public static class RateLimitingExtensions
{
    // Name of the policy attached to the AI endpoints with [EnableRateLimiting]
    public const string AssistantPolicy = "assistant";

    // Registers the "assistant" rate-limit policy. The limit, the window and the algorithm come from the AiSafety:RateLimit
    // section of appsettings.json (read through IOptions, validated at startup). Remember to call app.UseRateLimiter() AFTER
    // UseAuthentication/UseAuthorization, otherwise the user is not known yet when the partition key is chosen.
    public static IServiceCollection AddAssistantRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(AssistantPolicy, httpContext =>
            {
                var settings = Settings(httpContext);
                var key = PartitionKey(httpContext);

                // One limiter (one counter) per partition key, so one user can never use up another user's quota
                return settings.IsSliding
                    ? RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = settings.Window,
                        SegmentsPerWindow = settings.SegmentsPerWindow,
                        QueueLimit = 0 // reject at once instead of making the caller wait
                    })
                    : RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = settings.Window,
                        QueueLimit = 0
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                // Tell the client when to try again (whole seconds, at least 1). The fixed-window limiter knows the exact time.
                // The sliding-window limiter gives no hint, and the only safe answer is a whole window: if the user spent every
                // permit just now, the first one is free again only when that moment has slid out of the window.
                var settings = Settings(context.HttpContext);
                TimeSpan? wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? retryAfter
                    : settings.IsSliding ? settings.Window : null;
                if (wait is { } seconds)
                {
                    context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(seconds.TotalSeconds)).ToString();
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"title\":\"Too many requests.\",\"status\":429,\"detail\":\"Bạn hỏi quá nhanh, vui lòng thử lại sau ít phút.\"}",
                    cancellationToken);
            };
        });

        return services;
    }

    private static RateLimitSettings Settings(HttpContext httpContext)
    {
        return httpContext.RequestServices.GetRequiredService<IOptions<AiSafetyOptions>>().Value.RateLimit;
    }

    // The signed-in user's id (not the IP: users behind one office or proxy IP must not share a quota). The keys are prefixed so a
    // user id can never collide with an IP string. The IP is only a fallback for a request without a user, which the [Authorize]
    // on the controllers already turns away with 401 before this runs.
    private static string PartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrEmpty(userId)
            ? "user:" + userId
            : "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}
