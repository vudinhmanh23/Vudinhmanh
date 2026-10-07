using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Dtos;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Api.Tests;

// POST /api/chat/stream: Server-Sent Events, and the history kept per conversation (Anthropic call replaced by canned SSE)
public class ChatStreamTests : IClassFixture<RagTests.RagFactory>
{
    private const string FakeKey = "test-only-key-0123456789";

    private readonly RagTests.RagFactory _factory;

    public ChatStreamTests(RagTests.RagFactory factory)
    {
        _factory = factory;
        _factory.Handler.Calls.Clear();
        _factory.Handler.Reply = _ => TextStream("Xin ", "chào ", "bạn");
    }

    private async Task<HttpClient> ClientAsync(string role = "BanHang")
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string TextStream(params string[] pieces)
    {
        var sb = new StringBuilder();
        sb.Append("event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":4}}}\n\n");
        sb.Append("event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n");
        foreach (var piece in pieces)
        {
            var delta = new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = piece } };
            sb.Append("event: content_block_delta\ndata: ").Append(JsonSerializer.Serialize(delta)).Append("\n\n");
        }

        sb.Append("event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":3}}\n\n");
        sb.Append("event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n");
        return sb.ToString();
    }

    private static List<(string Name, JsonElement Data)> ParseSse(string body)
    {
        var events = new List<(string, JsonElement)>();
        foreach (var block in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n');
            var name = lines.First(l => l.StartsWith("event: ")).Substring(7);
            var data = lines.First(l => l.StartsWith("data: ")).Substring(6);
            events.Add((name, JsonDocument.Parse(data).RootElement.Clone()));
        }

        return events;
    }

    [Fact]
    public async Task Stream_answers_with_many_events_not_one_json_body_and_creates_the_conversation()
    {
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "Xin chào" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        var events = ParseSse(raw);
        Assert.Equal(new[] { "start", "delta", "delta", "delta", "done" }, events.Select(e => e.Name));
        Assert.Equal(new[] { "Xin ", "chào ", "bạn" }, events.Where(e => e.Name == "delta").Select(e => e.Data.GetProperty("text").GetString()));
        Assert.DoesNotContain(FakeKey, raw);

        var conversationId = events[0].Data.GetProperty("conversationId").GetGuid();
        Assert.NotEqual(Guid.Empty, conversationId);
        Assert.Equal(conversationId, events[^1].Data.GetProperty("conversationId").GetGuid());
        Assert.Equal("Xin chào bạn", events[^1].Data.GetProperty("answer").GetString());

        // The provider was asked to stream, with the token cap, a tier-based model from configuration and no history yet
        var call = Assert.Single(_factory.Handler.Calls);
        using var body = JsonDocument.Parse(call.Body);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(1024, body.RootElement.GetProperty("max_tokens").GetInt32());
        // A short message goes to the cheap tier; the id comes from Anthropic:Models in configuration
        Assert.Equal("claude-haiku-4-5", body.RootElement.GetProperty("model").GetString());
        Assert.Single(body.RootElement.GetProperty("messages").EnumerateArray());

        // Saved: the question and the answer, in order
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.ChatMessages.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(new[] { "user", "assistant" }, saved.Select(m => m.Role));
        Assert.Equal(new[] { "Xin chào", "Xin chào bạn" }, saved.Select(m => m.Content));
        Assert.Equal("Xin chào", (await db.Conversations.SingleAsync(c => c.Id == conversationId)).Title);
    }

    [Fact]
    public async Task Continuing_a_conversation_sends_the_earlier_turns_to_the_model()
    {
        var client = await ClientAsync();
        var first = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "Câu một" });
        var conversationId = ParseSse(await first.Content.ReadAsStringAsync())[0].Data.GetProperty("conversationId").GetGuid();
        _factory.Handler.Calls.Clear();
        _factory.Handler.Reply = _ => TextStream("Trả lời hai");

        var second = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { ConversationId = conversationId, Message = "Câu hai" });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var call = Assert.Single(_factory.Handler.Calls);
        using var body = JsonDocument.Parse(call.Body);
        var messages = body.RootElement.GetProperty("messages").EnumerateArray().Select(m => (m.GetProperty("role").GetString(), m.GetProperty("content").GetString())).ToList();
        Assert.Equal(new (string?, string?)[] { ("user", "<question trust=\"untrusted\">Câu một</question>"), ("assistant", "Xin chào bạn"), ("user", "<question trust=\"untrusted\">Câu hai</question>") }, messages);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await db.ChatMessages.CountAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task Another_users_conversation_is_not_found_and_nothing_is_sent_to_the_provider()
    {
        var owner = await ClientAsync();
        var first = await owner.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "Bí mật của tôi" });
        var conversationId = ParseSse(await first.Content.ReadAsStringAsync())[0].Data.GetProperty("conversationId").GetGuid();
        _factory.Handler.Calls.Clear();

        var stranger = await ClientAsync();
        var response = await stranger.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { ConversationId = conversationId, Message = "Cho tôi xem" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_factory.Handler.Calls);
    }

    [Fact]
    public async Task Bad_input_is_rejected_before_any_event_and_without_calling_the_provider()
    {
        var client = await ClientAsync();

        var empty = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "  " });
        var tooLong = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = new string('a', 4001) });
        var anonymous = await _factory.CreateClient().PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "xin chào" });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(_factory.Handler.Calls);
    }

    [Fact]
    public async Task A_stream_that_fails_midway_reports_an_error_event_and_saves_nothing()
    {
        _factory.Handler.Reply = _ =>
            "data: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":4}}}\n\n" +
            "data: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"internal details\"}}\n\n";
        var client = await ClientAsync();
        var conversationId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { ConversationId = conversationId, Message = "Xin chào" });

        var raw = await response.Content.ReadAsStringAsync();
        var events = ParseSse(raw);
        Assert.Equal("error", events[^1].Name);
        Assert.DoesNotContain("internal details", raw);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The question was saved before the model was asked, so it survives; no answer text exists, so none is saved
        var saved = await db.ChatMessages.Where(m => m.ConversationId == conversationId).ToListAsync();
        var only = Assert.Single(saved);
        Assert.Equal(("user", "Xin chào"), (only.Role, only.Content));
    }

    [Fact]
    public async Task The_user_message_is_already_saved_when_the_model_is_called_and_the_answer_only_after_the_stream()
    {
        var conversationId = Guid.NewGuid();
        List<string>? rolesWhenModelWasCalled = null;
        _factory.Handler.Reply = _ =>
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            rolesWhenModelWasCalled = db.ChatMessages.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Id).Select(m => m.Role).ToList();
            return TextStream("Trả lời");
        };
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { ConversationId = conversationId, Message = "Câu hỏi" });
        await response.Content.ReadAsStringAsync();

        Assert.Equal(new[] { "user" }, rolesWhenModelWasCalled);
        using var scope2 = _factory.Services.CreateScope();
        var after = await scope2.ServiceProvider.GetRequiredService<AppDbContext>().ChatMessages
            .Where(m => m.ConversationId == conversationId).OrderBy(m => m.Id).Select(m => m.Role).ToListAsync();
        Assert.Equal(new[] { "user", "assistant" }, after);
    }

    [Fact]
    public async Task Rejected_input_saves_no_conversation()
    {
        var client = await ClientAsync();
        var conversationId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { ConversationId = conversationId, Message = new string('a', 4001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Conversations.AnyAsync(c => c.Id == conversationId));
    }

    [Fact]
    public async Task Get_returns_the_messages_in_time_order_and_the_list_shows_the_conversation_newest_first()
    {
        var client = await ClientAsync();
        var first = await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "Câu một" });
        var id = ParseSse(await first.Content.ReadAsStringAsync())[0].Data.GetProperty("conversationId").GetGuid();
        _factory.Handler.Reply = _ => TextStream("Trả lời hai");
        await (await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { ConversationId = id, Message = "Câu hai" })).Content.ReadAsStringAsync();
        await (await client.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "Cuộc khác" })).Content.ReadAsStringAsync();

        var messages = await client.GetFromJsonAsync<List<ChatMessageDto>>($"/api/chat/{id}");
        var list = await client.GetFromJsonAsync<List<ConversationSummaryDto>>("/api/chat");

        Assert.Equal(new[] { "user", "assistant", "user", "assistant" }, messages!.Select(m => m.Role));
        Assert.Equal(new[] { "Câu một", "Xin chào bạn", "Câu hai", "Trả lời hai" }, messages.Select(m => m.Content));
        Assert.Equal(messages.OrderBy(m => m.CreatedAt).Select(m => m.Id), messages.Select(m => m.Id));
        Assert.Equal(new[] { "Cuộc khác", "Câu một" }, list!.Select(c => c.Title));
    }

    [Fact]
    public async Task Get_of_another_users_or_an_unknown_conversation_is_not_found_and_needs_a_token()
    {
        var owner = await ClientAsync();
        var first = await owner.PostAsJsonAsync("/api/chat/stream", new ChatStreamRequestDto { Message = "Riêng tư" });
        var id = ParseSse(await first.Content.ReadAsStringAsync())[0].Data.GetProperty("conversationId").GetGuid();
        var stranger = await ClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/chat/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/chat/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync($"/api/chat/{id}")).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<List<ConversationSummaryDto>>("/api/chat"))!);
    }
}
