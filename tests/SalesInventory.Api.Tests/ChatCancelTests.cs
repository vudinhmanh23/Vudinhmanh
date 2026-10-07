using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Dtos;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Api.Tests;

// The client cancels while the answer is still being written: the call to the model must stop, the text streamed so far
// must be kept, and nothing may blow up. The model's side is a pipe the test controls, so the cancel lands mid-stream.
public class ChatCancelTests : IClassFixture<StreamingIncrementalTests.SlowFactory>
{
    private readonly StreamingIncrementalTests.SlowFactory _factory;

    public ChatCancelTests(StreamingIncrementalTests.SlowFactory factory)
    {
        _factory = factory;
    }

    private static string Sse(string json) => "data: " + json + "\n\n";

    private static string Delta(string text) =>
        Sse(JsonSerializer.Serialize(new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text } }));

    [Fact]
    public async Task Cancelling_mid_stream_stops_the_model_call_and_keeps_the_text_streamed_so_far()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new ChatStreamRequestDto { Message = "Kể tôi nghe" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await _factory.Handler.WriteAsync(
            Sse("{\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":3}}}") +
            Sse("{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}") +
            Delta("Xin ") + Delta("chào "));

        using var cancel = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Guid conversationId = default;
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using (response)
        using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token)))
        {
            string? line;
            var seen = new List<string>();
            while ((line = await reader.ReadLineAsync(timeout.Token)) is not null)
            {
                seen.Add(line);
                if (line.StartsWith("data:") && line.Contains("\"conversationId\""))
                {
                    conversationId = JsonDocument.Parse(line[5..]).RootElement.GetProperty("conversationId").GetGuid();
                }

                if (line.StartsWith("data:") && line.Contains("\"text\":\"chào \""))
                {
                    break;
                }
            }

            Assert.NotEqual(Guid.Empty, conversationId);

            // The user presses "Hủy": the client drops the response while the model is still writing
            cancel.Cancel();
        } // disposing the response closes the connection, which is what a cancelled HttpClient request does

        // The model's stream is closed from our side: its reader is gone, so the pipe reports "completed" once the server noticed
        var upstreamClosed = false;
        for (var i = 0; i < 100 && !upstreamClosed; i++)
        {
            var flush = await _factory.Handler.Pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(": still writing\n\n"));
            upstreamClosed = flush.IsCompleted;
            if (!upstreamClosed)
            {
                await Task.Delay(100);
            }
        }

        Assert.True(upstreamClosed, "the call to the model should have been stopped");

        // The partial answer was saved as the assistant's message
        List<(string Role, string Content)> saved = new();
        for (var i = 0; i < 50; i++)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            saved = (await db.ChatMessages.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Id).ToListAsync())
                .Select(m => (m.Role, m.Content)).ToList();
            if (saved.Count == 2)
            {
                break;
            }

            await Task.Delay(100);
        }

        Assert.Equal(new[] { ("user", "Kể tôi nghe"), ("assistant", "Xin chào") }, saved);

        // And the server is still healthy afterwards
        Assert.True((await client.GetAsync("/api/chat")).IsSuccessStatusCode);
    }
}
