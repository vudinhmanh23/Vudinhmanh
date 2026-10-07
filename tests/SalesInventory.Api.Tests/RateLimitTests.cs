using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Ai;

namespace SalesInventory.Api.Tests;

// Per-user rate limiting of the AI endpoints. Anthropic is replaced by a fake handler, so no call costs anything.
public class RateLimitTests
{
    private const string FakeKey = "test-only-key-0123456789";
    private const string Reply = """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";

    // Settings of one test host; null = leave the value from appsettings.json (10 requests per 60 seconds, FixedWindow)
    public sealed class LimitedFactory : CustomWebApplicationFactory
    {
        private readonly Dictionary<string, string> _settings;

        public AssistantTests.FakeAnthropicHandler Handler { get; } = new() { Reply = _ => Reply };

        public LimitedFactory(params (string Key, string Value)[] settings)
        {
            _settings = settings.ToDictionary(s => s.Key, s => s.Value);
        }

        protected override void ConfigureExtraSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("Anthropic:ApiKey", FakeKey);
            foreach (var (key, value) in _settings)
            {
                builder.UseSetting(key, value);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
                services.AddHttpClient<IChatService, AnthropicChatService>().ConfigurePrimaryHttpMessageHandler(() => Handler));
        }
    }

    private static async Task<HttpClient> ClientAsync(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> Ask(HttpClient client) =>
        client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = "Xin chào" });

    [Fact]
    public async Task With_the_default_configuration_the_11th_call_within_a_minute_is_429_with_Retry_After()
    {
        using var factory = new LimitedFactory();
        var client = await ClientAsync(factory);

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 11; i++)
        {
            last = await Ask(client);
            statuses.Add(last.StatusCode);
        }

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.OK, 10), statuses.Take(10));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
        var retryAfter = Assert.Single(last!.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter), 1, 60);
        Assert.Equal("application/problem+json", last.Content.Headers.ContentType!.MediaType);
        Assert.Equal(10, factory.Handler.Calls.Count); // the rejected call never reached the model
    }

    [Theory]
    [InlineData("FixedWindow")]
    [InlineData("SlidingWindow")]
    public async Task After_the_window_has_passed_the_user_can_call_again(string algorithm)
    {
        using var factory = new LimitedFactory(
            ("AiSafety:RateLimit:PermitLimit", "3"),
            ("AiSafety:RateLimit:WindowSeconds", "2"),
            ("AiSafety:RateLimit:SegmentsPerWindow", "2"),
            ("AiSafety:RateLimit:Algorithm", algorithm));
        var client = await ClientAsync(factory);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Ask(client)).StatusCode);
        }

        var blocked = await Ask(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        var wait = int.Parse(Assert.Single(blocked.Headers.GetValues("Retry-After")));
        Assert.InRange(wait, 1, 3);

        // Waiting as long as the server said (plus a little) frees the quota again
        await Task.Delay(TimeSpan.FromSeconds(wait) + TimeSpan.FromMilliseconds(1200));

        Assert.Equal(HttpStatusCode.OK, (await Ask(client)).StatusCode);
    }

    [Fact]
    public async Task Each_user_has_their_own_quota()
    {
        using var factory = new LimitedFactory(("AiSafety:RateLimit:PermitLimit", "2"));
        var heavy = await ClientAsync(factory);
        var other = await ClientAsync(factory);

        await Ask(heavy);
        await Ask(heavy);
        var heavyBlocked = await Ask(heavy);
        var otherStillOk = await Ask(other);

        Assert.Equal(HttpStatusCode.TooManyRequests, heavyBlocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherStillOk.StatusCode);
    }

    [Fact]
    public async Task The_same_user_on_another_connection_shares_one_quota_across_all_AI_endpoints()
    {
        using var factory = new LimitedFactory(("AiSafety:RateLimit:PermitLimit", "3"));
        var client = await ClientAsync(factory);
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var second = factory.CreateClient(); // a different HttpClient (as if from another IP) with the same account
        second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var a = await Ask(client);
        var b = await client.PostAsJsonAsync("/api/assistant/ask/stream", new AskRequestDto { Question = "Xin chào" });
        var c = await second.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "Xin chào" });
        var d = await second.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "Xin chào" });

        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, b.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, c.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, d.StatusCode);
    }

    [Fact]
    public async Task Anonymous_calls_get_401_and_never_use_up_a_users_quota()
    {
        using var factory = new LimitedFactory(("AiSafety:RateLimit:PermitLimit", "2"));
        var anonymous = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await Ask(anonymous)).StatusCode);
        }

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        var user = await ClientAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await Ask(user)).StatusCode);
    }
}
