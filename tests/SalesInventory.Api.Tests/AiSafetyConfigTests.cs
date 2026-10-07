using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Ai;

namespace SalesInventory.Api.Tests;

// The AiSafety section: changing a number in configuration changes the behaviour, a wrong number stops the app at startup,
// and the usage log line carries the user id and the estimated cost without any content.
public class AiSafetyConfigTests
{
    private const string FakeKey = "test-only-key-0123456789";
    private const string Reply = """{"content":[{"type":"text","text":"Câu trả lời riêng tư của trợ lý"}],"stop_reason":"end_turn","usage":{"input_tokens":1000,"output_tokens":500}}""";

    public sealed class ConfigFactory : CustomWebApplicationFactory
    {
        private readonly (string Key, string Value)[] _settings;
        private readonly ConcurrentQueue<string> _logs = new();

        public AssistantTests.FakeAnthropicHandler Handler { get; } = new() { Reply = _ => Reply };

        public string[] Logs => _logs.ToArray();

        public ConfigFactory(params (string Key, string Value)[] settings)
        {
            _settings = settings;
        }

        protected override void ConfigureExtraSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("Anthropic:ApiKey", FakeKey);
            builder.UseSetting("AiSafety:RateLimit:PermitLimit", "1000");
            foreach (var (key, value) in _settings)
            {
                builder.UseSetting(key, value);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.AddProvider(new CaptureProvider(_logs)));
            builder.ConfigureServices(services =>
                services.AddHttpClient<IChatService, AnthropicChatService>().ConfigurePrimaryHttpMessageHandler(() => Handler));
        }
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages;

        public CaptureProvider(ConcurrentQueue<string> messages) => _messages = messages;

        public ILogger CreateLogger(string categoryName) => new Capture(_messages);

        public void Dispose()
        {
        }

        private sealed class Capture : ILogger
        {
            private readonly ConcurrentQueue<string> _messages;

            public Capture(ConcurrentQueue<string> messages) => _messages = messages;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _messages.Enqueue(formatter(state, exception));
        }
    }

    private static async Task<(HttpClient Client, string UserId)> ClientAsync(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims;
        var userId = claims.First(c => c.Type is ClaimTypes.NameIdentifier or "nameid" or "sub").Value;
        return (client, userId);
    }

    // ---- [easy] numbers in configuration drive the behaviour ----

    [Fact]
    public async Task The_defaults_in_appsettings_json_are_bound_from_the_AiSafety_section()
    {
        using var factory = new ConfigFactory();

        var safety = factory.Services.GetRequiredService<IOptions<AiSafetyOptions>>().Value;

        Assert.Equal(1024, safety.MaxTokens);
        Assert.Equal(2000, safety.MaxQuestionLength);
        Assert.Equal(20, safety.MaxHistoryMessages);
        Assert.Equal(4, safety.MaxToolRounds);
        Assert.Equal(80, safety.ShortQuestionMaxLength);
        Assert.Equal("FixedWindow", safety.RateLimit.Algorithm);
        Assert.Equal(2.00m, safety.Pricing["sonnet"].InputPerMillionTokens);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Changing_MaxTokens_and_MaxQuestionLength_in_configuration_changes_what_is_sent_and_accepted()
    {
        using var factory = new ConfigFactory(("AiSafety:MaxTokens", "256"), ("AiSafety:MaxQuestionLength", "50"));
        var (client, _) = await ClientAsync(factory);

        var ok = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = new string('a', 50) });
        var tooLong = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = new string('a', 51) });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("50", await tooLong.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(Assert.Single(factory.Handler.Calls).Body);
        Assert.Equal(256, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Theory]
    [InlineData("AiSafety:MaxTokens", "0", "MaxTokens")]
    [InlineData("AiSafety:MaxQuestionLength", "-5", "MaxQuestionLength")]
    [InlineData("AiSafety:RateLimit:PermitLimit", "0", "PermitLimit")]
    [InlineData("AiSafety:RateLimit:Algorithm", "Bucket", "Algorithm")]
    [InlineData("AiSafety:Pricing:sonnet:InputPerMillionTokens", "-1", "Pricing")]
    public void A_wrong_number_stops_the_app_at_startup_and_names_the_setting(string key, string value, string expectedInMessage)
    {
        using var factory = new ConfigFactory((key, value));

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        var all = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            all.Add(e.Message);
        }

        Assert.Contains(all, m => m.Contains("AiSafety") && m.Contains(expectedInMessage));
    }

    // ---- [medium] the usage log ----

    [Fact]
    public async Task The_usage_line_has_the_user_id_the_tokens_and_the_cost_from_the_configured_prices_and_no_content()
    {
        // 1000 input and 500 output tokens at the default sonnet prices (2.00 / 10.00 per million) = 0.002 + 0.005 = 0.007
        using var factory = new ConfigFactory();
        var (client, userId) = await ClientAsync(factory);
        const string question = "Số điện thoại của tôi là 0912345678, địa chỉ 12 Nguyễn Huệ, hãy tư vấn máy giặt ABC cho gia đình tôi";

        var response = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = question });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = Assert.Single(factory.Logs, l => l.Contains("Assistant usage"));
        Assert.Contains($"user={userId}", line);
        Assert.Contains("tier=sonnet", line);
        Assert.Contains("inputTokens=1000", line);
        Assert.Contains("outputTokens=500", line);
        Assert.Contains("estimatedCost=0.007000 USD", line);
        Assert.Contains($"questionChars={question.Length}", line);

        // No content: neither the question (with its phone number and address), nor the answer, nor the e-mail of the user
        var everything = string.Join("\n", factory.Logs);
        Assert.DoesNotContain("0912345678", everything);
        Assert.DoesNotContain("Nguyễn Huệ", everything);
        Assert.DoesNotContain("riêng tư của trợ lý", everything);
        Assert.DoesNotContain("@test.local", everything);
    }

    [Fact]
    public async Task The_cost_follows_the_prices_and_the_currency_in_configuration()
    {
        using var priced = new ConfigFactory(
            ("AiSafety:Pricing:sonnet:InputPerMillionTokens", "10"),
            ("AiSafety:Pricing:sonnet:OutputPerMillionTokens", "20"),
            ("AiSafety:Currency", "EUR"));
        var (pricedClient, _) = await ClientAsync(priced);
        await pricedClient.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = new string('a', 100) });

        // 1000 * 10 / 1e6 + 500 * 20 / 1e6 = 0.02
        Assert.Contains(priced.Logs, l => l.Contains("Assistant usage") && l.Contains("estimatedCost=0.020000 EUR"));
    }

    [Fact]
    public async Task The_short_tier_is_priced_with_its_own_prices()
    {
        using var factory = new ConfigFactory();
        var (client, _) = await ClientAsync(factory);

        await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = "Giá máy giặt ABC?" });

        // haiku: 1000 * 1.00 / 1e6 + 500 * 5.00 / 1e6 = 0.0035
        Assert.Contains(factory.Logs, l => l.Contains("tier=haiku") && l.Contains("estimatedCost=0.003500 USD"));
    }

    [Fact]
    public void An_unpriced_tier_has_no_cost_estimate_rather_than_a_wrong_one()
    {
        var options = new AiSafetyOptions();
        options.Pricing["sonnet"] = new TierPrice { InputPerMillionTokens = 2, OutputPerMillionTokens = 10 };

        Assert.Equal(0.007m, options.EstimateCost("sonnet", 1000, 500));
        Assert.Equal(0.007m, options.EstimateCost("SONNET", 1000, 500));
        Assert.Null(options.EstimateCost("opus", 1000, 500));
    }
}
