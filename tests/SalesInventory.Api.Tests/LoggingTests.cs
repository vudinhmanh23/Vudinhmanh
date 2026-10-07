using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SalesInventory.Api.Logging;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Security;

namespace SalesInventory.Api.Tests;

// Structured logging: what a request line contains, how loud failures are, what an exception entry carries, that the files are
// really written (JSON, one per day) and that no secret ever reaches them.
public class LoggingTests : IClassFixture<LoggingTests.EventFactory>
{
    private const string Password = "Sup3rS3cret!Pw";
    private const string ConnectionString = "Server=db.internal;Database=Shop;User Id=sa;Password=Sup3rS3cret!Pw;TrustServerCertificate=True;";

    private readonly EventFactory _factory;

    public LoggingTests(EventFactory factory)
    {
        _factory = factory;
    }

    // ---- the request line ----

    [Fact]
    public async Task RequestLog_AuthenticatedGet_HasMethodPathStatusDurationAndTheUserAsStructuredFields()
    {
        // Arrange: a signed-in user
        var (client, userId) = await SignedInAsync(_factory);

        // Act: a GET with a query string that must NOT end up in the log
        var response = await client.GetAsync("/api/products?search=QUERYSECRET123");

        // Assert: one Information event with named fields, not just text
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = _factory.RequestLines().Single(e => Prop(e, "UserId") == userId && Prop(e, "RequestPath") == "/api/products");
        Assert.Equal(LogEventLevel.Information, line.Level);
        Assert.Equal("GET", Prop(line, "RequestMethod"));
        Assert.Equal("200", Prop(line, "StatusCode"));
        Assert.True(double.Parse(Prop(line, "Elapsed")!, System.Globalization.CultureInfo.InvariantCulture) >= 0);
        Assert.False(string.IsNullOrEmpty(Prop(line, "TraceId")));
        Assert.Equal("SalesInventory.Api", Prop(line, "Application"));

        // Nothing from the query string or the headers (the bearer token) is in the event
        var everything = string.Join(" ", line.Properties.Select(p => p.Key + "=" + p.Value)) + line.RenderMessage();
        Assert.DoesNotContain("QUERYSECRET123", everything);
        Assert.DoesNotContain("Authorization", everything);
        Assert.DoesNotContain("Bearer", everything);
    }

    [Fact]
    public async Task RequestLog_NoToken_IsAWarningForAnAnonymousUser()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/customers?marker=anon401");

        // Assert: a refused request is louder than a normal one
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var line = _factory.RequestLines().Last(e => Prop(e, "RequestPath") == "/api/customers" && Prop(e, "StatusCode") == "401");
        Assert.Equal(LogEventLevel.Warning, line.Level);
        Assert.Equal("anonymous", Prop(line, "UserId"));
    }

    [Fact]
    public async Task ExceptionLog_RejectedBusinessRule_IsAWarningWithTheFullContextButNoStackTrace()
    {
        // Arrange: an order for a customer that does not exist
        var (client, userId) = await SignedInAsync(_factory);
        var order = new { orderDate = DateTime.UtcNow, customerId = 987654, discountAmount = 0m, items = new[] { new { productId = 1, quantity = 1, unitPrice = 1m } } };

        // Act
        var response = await client.PostAsJsonAsync("/api/sales-orders", order);

        // Assert: the middleware's own entry says what, who and why
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var entry = _factory.Events.Single(e => e.MessageTemplate.Text.StartsWith("Request rejected") && Prop(e, "UserId") == userId);
        Assert.Equal(LogEventLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal("POST", Prop(entry, "RequestMethod"));
        Assert.Equal("/api/sales-orders", Prop(entry, "RequestPath"));
        Assert.Equal("404", Prop(entry, "StatusCode"));
        Assert.Equal("NotFoundException", Prop(entry, "ExceptionType"));
        Assert.Contains("987654", Prop(entry, "Reason"));
        Assert.False(string.IsNullOrEmpty(Prop(entry, "TraceId")));

        // And the request line is a Warning with the same status
        var line = _factory.RequestLines().Single(e => Prop(e, "UserId") == userId && Prop(e, "StatusCode") == "404");
        Assert.Equal(LogEventLevel.Warning, line.Level);
    }

    [Fact]
    public async Task ExceptionLog_ServerFault_IsAnErrorWithTheExceptionAndTheContext()
    {
        // Arrange: the assistant service blows up when it is created
        var (client, userId) = await SignedInAsync(_factory);

        // Act
        var response = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = "Xin chào" });

        // Assert: the client gets a 500...
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // ...and the log gets an Error with the exception object (so the stack trace is kept) and the context to find the cause
        var entry = _factory.Events.Single(e => e.MessageTemplate.Text.StartsWith("Unhandled") && Prop(e, "UserId") == userId);
        Assert.Equal(LogEventLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Equal("POST", Prop(entry, "RequestMethod"));
        Assert.Equal("/api/assistant/ask", Prop(entry, "RequestPath"));
        Assert.Equal("500", Prop(entry, "StatusCode"));
        Assert.Equal("InvalidOperationException", Prop(entry, "ExceptionType"));
        Assert.False(string.IsNullOrEmpty(Prop(entry, "TraceId")));

        var line = _factory.RequestLines().Single(e => Prop(e, "UserId") == userId && Prop(e, "StatusCode") == "500");
        Assert.Equal(LogEventLevel.Error, line.Level);
    }

    // ---- the formatters mask secrets in whatever they print ----

    [Fact]
    public void JsonFormatter_SecretsInMessageArgumentsPropertiesAndExceptions_AreMaskedEvenWhenJsonEscapedBackslashesAreInvolved()
    {
        // Arrange: a masker that knows a connection string with a backslash (escaped as \\ in JSON) and a password
        const string withBackslash = @"Server=HOST\SQLEXPRESS;Database=Shop;User Id=sa;Password=Sup3rS3cret!Pw;";
        var masker = new SecretMasker(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = withBackslash,
            ["Database:Password"] = Password // a secret known by its configuration key, also when it stands alone
        }).Build());
        var formatter = new ScrubbingJsonFormatter { Masker = masker };
        var sink = new EventSink();
        var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        // Act: the secret is in the message argument, in a context property and in the exception
        logger.ForContext("Connection", withBackslash)
            .Error(new InvalidOperationException("Cannot open " + withBackslash), "Failed with {Cs} for {Pw}", withBackslash, Password);
        var text = Render(formatter, sink.Events.Single());

        // Assert
        Assert.DoesNotContain("Sup3rS3cret", text);
        Assert.DoesNotContain("HOST", text);
        Assert.Contains(SecretMasker.MaskText, text);
        Assert.Contains("\"@l\":\"Error\"", text); // still a valid structured entry
        JsonDocument.Parse(text).Dispose();
    }

    [Theory]
    [InlineData("Password=hunter22hunter")]
    [InlineData("pwd = hunter22hunter")]
    [InlineData("key sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123")]
    [InlineData("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijklmnop")]
    public void TextFormatter_ThingsShapedLikeSecrets_AreMaskedWithoutKnowingThem(string leaked)
    {
        // Arrange
        var formatter = new ScrubbingTextFormatter("[{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
        {
            Masker = new SecretMasker(new ConfigurationBuilder().Build())
        };
        var sink = new EventSink();
        var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        // Act
        logger.Warning(new Exception("inner " + leaked), "Something happened: {Text}", leaked);
        var text = Render(formatter, sink.Events.Single());

        // Assert
        Assert.DoesNotContain("hunter22hunter", text);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz0123", text);
        Assert.DoesNotContain("abcdefghijklmnop", text);
        Assert.Contains(SecretMasker.MaskText, text);
    }

    [Fact]
    public void TextFormatter_ARequestLine_ShowsTheMessageAndItsStructuredProperties()
    {
        // Arrange
        var formatter = new ScrubbingTextFormatter("[{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
        {
            Masker = new SecretMasker(new ConfigurationBuilder().Build())
        };
        var sink = new EventSink();
        var logger = new LoggerConfiguration().Enrich.WithProperty("Application", "SalesInventory.Api").WriteTo.Sink(sink).CreateLogger();

        // Act
        logger.ForContext("UserId", "u-1").Information(RequestLogging.MessageTemplate, "GET", "/api/products", 200, 12.34);
        var text = Render(formatter, sink.Events.Single());

        // Assert: the message is readable, and the properties follow as JSON. The values that are already in the message
        // (method, path, status, time) are not repeated there; the rest of the context (user, application) is.
        Assert.StartsWith("[INF] HTTP GET /api/products responded 200 in 12.3 ms ", text);
        Assert.Contains("\"UserId\":\"u-1\"", text);
        Assert.Contains("\"Application\":\"SalesInventory.Api\"", text);
    }

    // ---- the files ----

    [Fact]
    public async Task LogFile_AfterRealRequests_ExistsAsDailyJsonLinesAndHoldsNoSecret()
    {
        // Arrange: a host that writes its files to a folder of its own
        using var host = new FileFactory();
        var client = host.CreateClient();

        // Act: register (password in the body), log in (password in the body, bearer token in the answer), call an API with the token and
        // a secret in the query string, fail once with an exception whose message holds the connection string
        var email = $"logfile_{Guid.NewGuid():N}@test.local";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterDto { Email = email, Password = Password, FullName = "Log File", Role = "BanHang" });
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = Password });
        var token = (await login.Content.ReadFromJsonAsync<AuthResponseDto>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await client.GetAsync("/api/products?apikey=QUERYSECRET123");
        await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = "Xin chào" });
        host.Dispose(); // stops the host and closes its logger

        // Assert: files named like logs/test-YYYYMMDD.log (rolling by day), one JSON object per line
        var files = Directory.GetFiles(host.Directory, "*.log");
        var file = Assert.Single(files);
        Assert.Matches(@"test-\d{8}\.log$", file);

        string text;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            text = reader.ReadToEnd();
        }

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.NotEmpty(lines);
        var loginLine = lines.Single(l => l.TryGetProperty("RequestPath", out var p) && p.GetString() == "/api/auth/login");
        Assert.Equal("POST", loginLine.GetProperty("RequestMethod").GetString());
        Assert.Equal(200, loginLine.GetProperty("StatusCode").GetInt32());
        Assert.True(loginLine.TryGetProperty("Elapsed", out _));
        Assert.Contains(lines, l => l.TryGetProperty("@x", out _)); // the server fault is there with its exception

        // Nothing sensitive: not the password, the token, the query secret, the connection string, or the exception text that held it
        Assert.DoesNotContain(Password, text);
        Assert.DoesNotContain("Sup3rS3cret", text);
        Assert.DoesNotContain(token, text);
        Assert.DoesNotContain("QUERYSECRET123", text);
        Assert.DoesNotContain("db.internal", text);
        Assert.Contains(SecretMasker.MaskText, text); // the connection string in the exception message was replaced, not dropped
    }

    // ---- helpers ----

    private static async Task<(HttpClient Client, string UserId)> SignedInAsync(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var userId = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .First(c => c.Type is ClaimTypes.NameIdentifier or "nameid" or "sub").Value;
        return (client, userId);
    }

    // The scalar value of an event property as text (strings without quotes)
    private static string? Prop(LogEvent e, string name) =>
        e.Properties.TryGetValue(name, out var value) ? (value is ScalarValue s ? Convert.ToString(s.Value, System.Globalization.CultureInfo.InvariantCulture) : value.ToString()) : null;

    private static string Render(Serilog.Formatting.ITextFormatter formatter, LogEvent e)
    {
        using var writer = new StringWriter();
        formatter.Format(e, writer);
        return writer.ToString();
    }

    private sealed class EventSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    // The standard test host plus a sink that keeps every log event, and a service that fails when it is created
    public class EventFactory : CustomWebApplicationFactory
    {
        public ConcurrentQueue<LogEvent> EventQueue { get; } = new();

        public LogEvent[] Events => EventQueue.ToArray();

        // The one-line-per-request entries
        public IEnumerable<LogEvent> RequestLines() => Events.Where(e => e.MessageTemplate.Text == RequestLogging.MessageTemplate);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILogEventSink>(new QueueSink(EventQueue));
                services.AddScoped<IChatService>(_ => throw new InvalidOperationException("The assistant failed to start"));
            });
        }
    }

    // Writes its files to a folder of its own, and the failure message holds the connection string
    private sealed class FileFactory : EventFactory
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "salesinventory-tests", "logfile-" + Guid.NewGuid().ToString("N"));

        protected override string LogDirectory => Directory;

        protected override void ConfigureExtraSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
            builder.UseSetting("AiSafety:RateLimit:PermitLimit", "1000");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
                services.AddScoped<IChatService>(_ => throw new InvalidOperationException("Cannot connect with " + ConnectionString)));
        }
    }

    private sealed class QueueSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _queue;

        public QueueSink(ConcurrentQueue<LogEvent> queue) => _queue = queue;

        public void Emit(LogEvent logEvent) => _queue.Enqueue(logEvent);
    }
}
