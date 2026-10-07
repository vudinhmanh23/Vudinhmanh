using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Ai;

namespace SalesInventory.Api.Tests;

// Proves the answer really arrives piece by piece: the upstream (Anthropic) stream is held open after its first piece,
// and the client must already have received that piece while the rest has not even been sent.
public class StreamingIncrementalTests : IClassFixture<StreamingIncrementalTests.SlowFactory>
{
    private const string FakeKey = "test-only-key-0123456789";

    private readonly SlowFactory _factory;

    public StreamingIncrementalTests(SlowFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task The_first_piece_reaches_the_client_before_the_provider_has_finished()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/ask/stream")
        {
            Content = JsonContent.Create(new AskRequestDto { Question = "xin chào" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await _factory.Handler.WriteAsync(
            Sse("{\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":3}}}") +
            Sse("{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}") +
            Sse("{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Xin \"}}"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var lines = new List<string>();
        string? line;
        // Read until the first piece of text has come through; the provider is still holding the rest back
        while ((line = await reader.ReadLineAsync(timeout.Token)) is not null)
        {
            lines.Add(line);
            if (line.StartsWith("data:") && line.Contains("\"text\":\"Xin \""))
            {
                break;
            }
        }

        Assert.False(_factory.Handler.Released, "the provider has not sent the rest yet");
        Assert.Contains("event: start", lines);
        Assert.Contains("event: delta", lines);

        // Let the provider finish; the rest of the answer and the "done" event follow
        _factory.Handler.Released = true;
        await _factory.Handler.WriteAsync(
            Sse("{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"chào bạn\"}}") +
            Sse("{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":2}}"));
        await _factory.Handler.Pipe.Writer.CompleteAsync();
        var rest = await reader.ReadToEndAsync(timeout.Token);
        Assert.Contains("\"text\":\"chào bạn\"", rest);
        Assert.Contains("event: done", rest);
        Assert.DoesNotContain(FakeKey, string.Join("\n", lines) + rest);
    }

    private static string Sse(string json) => "data: " + json + "\n\n";

    // The upstream body is a pipe the test writes into, so it decides exactly when each piece is "sent" by the provider
    public sealed class PipeHandler : HttpMessageHandler
    {
        public Pipe Pipe { get; } = new();

        public bool Released { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(Pipe.Reader.AsStream()) });
        }

        public async Task WriteAsync(string text)
        {
            await Pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(text));
            await Pipe.Writer.FlushAsync();
        }
    }

    public sealed class SlowFactory : CustomWebApplicationFactory
    {
        public PipeHandler Handler { get; } = new();

        protected override void ConfigureExtraSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("Anthropic:ApiKey", FakeKey);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
                services.AddHttpClient<IChatService, AnthropicChatService>().ConfigurePrimaryHttpMessageHandler(() => Handler));
        }
    }
}
