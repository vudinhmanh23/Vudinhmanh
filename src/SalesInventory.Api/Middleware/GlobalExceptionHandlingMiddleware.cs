using System.Diagnostics;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Services;

namespace SalesInventory.Api.Middleware;

// Central place that turns exceptions into RFC 9457 ProblemDetails responses (application/problem+json)
public class GlobalExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Keep Vietnamese characters readable instead of unicode escapes
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        var problem = BuildProblemDetails(context, exception);
        problem.Extensions["traceId"] = traceId;

        // Expected business failures (4xx) are warnings; anything else is a server fault
        if (problem.Status is >= 500)
        {
            _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", traceId);
        }
        else
        {
            _logger.LogWarning(exception, "Request rejected with {Status}: {Message}. TraceId: {TraceId}",
                problem.Status, exception.Message, traceId);
        }

        context.Response.Clear();
        context.Response.StatusCode = problem.Status!.Value;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, problem.GetType(), JsonOptions));
    }

    private ProblemDetails BuildProblemDetails(HttpContext context, Exception exception)
    {
        switch (exception)
        {
            case ValidationException validation:
                // Group messages per property so the client can show them next to each field
                var errors = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                return new ValidationProblemDetails(errors)
                {
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                    Title = "One or more validation errors occurred.",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "See the errors property for details.",
                    Instance = context.Request.Path
                };

            // Another request changed the same row (e.g. stock) between our read and write
            case Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException:
                return Create(context, StatusCodes.Status409Conflict, "Concurrency conflict.",
                    "https://tools.ietf.org/html/rfc9110#section-15.5.10", "The data was modified by another request. Please retry.");

            case NotFoundException:
                return Create(context, StatusCodes.Status404NotFound, "Resource not found.",
                    "https://tools.ietf.org/html/rfc9110#section-15.5.5", exception.Message);

            case UnprocessableEntityException:
                return Create(context, StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.",
                    "https://tools.ietf.org/html/rfc4918#section-11.2", exception.Message);

            // Duplicate SKU/barcode are business conflicts raised by the product service
            case BusinessRuleException or ConflictException or DuplicateSkuException or DuplicateBarcodeException:
                return Create(context, StatusCodes.Status409Conflict, "Business rule violated.",
                    "https://tools.ietf.org/html/rfc9110#section-15.5.10", exception.Message);

            default:
                // Never leak internals outside Development
                var detail = _environment.IsDevelopment()
                    ? exception.ToString()
                    : "An unexpected error occurred. Please contact support and quote the traceId.";
                return Create(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.",
                    "https://tools.ietf.org/html/rfc9110#section-15.6.1", detail);
        }
    }

    private static ProblemDetails Create(HttpContext context, int status, string title, string type, string detail)
    {
        return new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = status,
            Detail = detail,
            Instance = context.Request.Path
        };
    }
}
