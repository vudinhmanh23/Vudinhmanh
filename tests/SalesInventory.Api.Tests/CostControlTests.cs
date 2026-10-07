using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Ai;

namespace SalesInventory.Api.Tests;

// Cost controls of the assistant: input length cap (400), max_tokens, model tier by question length, and one usage log line per request
public class CostControlTests : IClassFixture<CostControlTests.CostFactory>
{
    private const string FakeKey = "test-only-key-0123456789";
    private const string Reply = """{"content":[{"type":"text","text":"Xin chào"}],"stop_reason":"end_turn","usage":{"input_tokens":123,"output_tokens":45}}""";

    private readonly CostFactory _factory;

    public CostControlTests(CostFactory factory)
    {
        _factory = factory;
        _factory.Handler.Calls.Clear();
        _factory.ClearLogs();
        _factory.Handler.Reply = _ => Reply;
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string LongQuestion(int length) => new('a', length);

    [Fact]
    public async Task A_5000_character_question_is_rejected_with_400_on_every_endpoint_without_calling_the_model()
    {
        var client = await ClientAsync();
        var text = LongQuestion(5000);

        var ask = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = text });
        var askStream = await client.PostAsJsonAsync("/api/assistant/ask/stream", new AskRequestDto { Question = text });
        var chat = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = text });

        Assert.Equal(HttpStatusCode.BadRequest, ask.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, askStream.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, chat.StatusCode);
        Assert.Contains("2000", await ask.Content.ReadAsStringAsync());
        Assert.Empty(_factory.Handler.Calls);
        Assert.DoesNotContain(_factory.Logs, l => l.Contains("Assistant usage"));
    }

    [Fact]
    public async Task The_limit_is_exactly_2000_characters()
    {
        var client = await ClientAsync();

        var atLimit = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = LongQuestion(2000) });
        var over = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = LongQuestion(2001) });

        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Single(_factory.Handler.Calls);
    }

    [Fact]
    public async Task A_valid_question_logs_one_usage_line_with_the_tokens_and_no_content()
    {
        var client = await ClientAsync();
        const string question = "Câu hỏi bí mật của khách hàng về chính sách bảo hành máy giặt, dài hơn tám mươi ký tự để dùng tầng Sonnet";

        var response = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = question });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = Assert.Single(_factory.Logs, l => l.Contains("Assistant usage"));
        Assert.Contains("tier=sonnet", line);
        Assert.Contains("inputTokens=123", line);
        Assert.Contains("outputTokens=45", line);
        Assert.Contains("totalTokens=168", line);
        Assert.Contains($"questionChars={question.Length}", line);
        // Counts only: the question, the answer and the key never go into a log
        Assert.DoesNotContain("bí mật", string.Join("\n", _factory.Logs));
        Assert.DoesNotContain(FakeKey, string.Join("\n", _factory.Logs));
    }

    [Fact]
    public async Task The_streaming_endpoint_logs_its_usage_too()
    {
        _factory.Handler.Reply = _ =>
            "data: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":77}}}\n\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Xin chào\"}}\n\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":9}}\n\n";
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "Xin chào" });
        await response.Content.ReadAsStringAsync();

        var line = Assert.Single(_factory.Logs, l => l.Contains("Assistant usage"));
        Assert.Contains("inputTokens=77", line);
        Assert.Contains("outputTokens=9", line);
        Assert.Contains("outcome=completed", line);
    }

    [Fact]
    public async Task Short_questions_use_the_cheap_tier_and_longer_ones_the_default_and_both_cap_max_tokens()
    {
        var client = await ClientAsync();

        await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = "Giá máy giặt ABC?" });
        await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = LongQuestion(81) });

        Assert.Equal(2, _factory.Handler.Calls.Count);
        using var shortBody = JsonDocument.Parse(_factory.Handler.Calls[0].Body);
        using var longBody = JsonDocument.Parse(_factory.Handler.Calls[1].Body);

        // The ids come from Anthropic:Models in appsettings.json; this test only checks which tier was picked
        Assert.Equal("claude-haiku-4-5", shortBody.RootElement.GetProperty("model").GetString());
        Assert.Equal("claude-sonnet-5-5", longBody.RootElement.GetProperty("model").GetString());

        // Haiku does not take the effort setting; the default tier does
        Assert.False(shortBody.RootElement.TryGetProperty("output_config", out _));
        Assert.True(longBody.RootElement.TryGetProperty("output_config", out _));

        Assert.Equal(1024, shortBody.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(1024, longBody.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(new[] { "haiku", "sonnet" }, _factory.Logs.Where(l => l.Contains("Assistant usage")).Select(l => l.Contains("tier=haiku") ? "haiku" : "sonnet"));
    }

    // Keeps every formatted log message so a test can read what was logged
    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages;

        public CaptureLoggerProvider(ConcurrentQueue<string> messages) => _messages = messages;

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _messages;

            public CaptureLogger(ConcurrentQueue<string> messages) => _messages = messages;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _messages.Enqueue(formatter(state, exception));
            }
        }
    }

    public sealed class CostFactory : CustomWebApplicationFactory
    {
        private readonly ConcurrentQueue<string> _logs = new();

        public AssistantTests.FakeAnthropicHandler Handler { get; } = new();

        // A snapshot of everything logged so far
        public string[] Logs => _logs.ToArray();

        public void ClearLogs() => _logs.Clear();

        protected override void ConfigureExtraSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("Anthropic:ApiKey", FakeKey);
            builder.UseSetting("AiSafety:RateLimit:PermitLimit", "1000");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.AddProvider(new CaptureLoggerProvider(_logs)));
            builder.ConfigureServices(services =>
                services.AddHttpClient<IChatService, AnthropicChatService>().ConfigurePrimaryHttpMessageHandler(() => Handler));
        }
    }
}
