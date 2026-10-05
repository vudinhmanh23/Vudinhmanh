using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalesInventory.Api.Middleware;
using SalesInventory.Application.Exceptions;

namespace SalesInventory.Api.Tests;

// Unit tests for the central exception -> ProblemDetails mapping
public class GlobalExceptionHandlingMiddlewareTests
{
    private static async Task<(int Status, string ContentType, JsonElement Body)> RunAsync(Exception toThrow, string environment)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw toThrow, NullLogger<GlobalExceptionHandlingMiddleware>.Instance, env.Object);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, context.Response.ContentType!, body.RootElement);
    }

    [Fact]
    public async Task NotFound_Returns404()
    {
        var (status, contentType, body) = await RunAsync(new NotFoundException("Supplier 5 not found"), Environments.Production);

        Assert.Equal(404, status);
        Assert.StartsWith("application/problem+json", contentType);
        Assert.Equal("Supplier 5 not found", body.GetProperty("detail").GetString());
        Assert.Equal("/api/test", body.GetProperty("instance").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task BusinessRule_Returns409()
    {
        var (status, _, _) = await RunAsync(new BusinessRuleException("Insufficient stock"), Environments.Production);

        Assert.Equal(409, status);
    }

    [Fact]
    public async Task UnprocessableEntity_Returns422ProblemJson()
    {
        var (status, contentType, body) = await RunAsync(new UnprocessableEntityException("SKU 'A1' đã tồn tại."), Environments.Production);

        Assert.Equal(422, status);
        Assert.StartsWith("application/problem+json", contentType);
        Assert.Equal("SKU 'A1' đã tồn tại.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ValidationException_WithManyFields_ListsEveryFieldAndMessage()
    {
        var ex = new ValidationException(new[]
        {
            new ValidationFailure("SupplierId", "SupplierId is required"),
            new ValidationFailure("Items[0].Quantity", "Quantity must be greater than 0"),
            new ValidationFailure("Items[0].UnitPrice", "UnitPrice must be >= 0"),
            new ValidationFailure("Items[0].UnitPrice", "UnitPrice has too many decimals"),
        });

        var (_, _, body) = await RunAsync(ex, Environments.Production);

        var errors = body.GetProperty("errors");
        Assert.Equal(3, errors.EnumerateObject().Count());
        Assert.Equal(2, errors.GetProperty("Items[0].UnitPrice").GetArrayLength());
    }

    [Fact]
    public async Task ValidationException_Returns400WithPerFieldErrors()
    {
        var ex = new ValidationException(new[] { new ValidationFailure("Name", "Name must not be empty") });

        var (status, _, body) = await RunAsync(ex, Environments.Production);

        Assert.Equal(400, status);
        Assert.Equal("Name must not be empty", body.GetProperty("errors").GetProperty("Name")[0].GetString());
    }

    [Fact]
    public async Task UnhandledException_InProduction_HidesDetails()
    {
        var (status, _, body) = await RunAsync(new InvalidOperationException("secret db password"), Environments.Production);

        Assert.Equal(500, status);
        Assert.DoesNotContain("secret", body.GetRawText());
    }

    [Fact]
    public async Task UnhandledException_InDevelopment_IncludesStackTrace()
    {
        var (_, _, body) = await RunAsync(new InvalidOperationException("boom"), Environments.Development);

        Assert.Contains("boom", body.GetProperty("detail").GetString());
    }
}
