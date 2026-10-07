using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SalesInventory.Web.Models;
using SalesInventory.Web.Services;

namespace SalesInventory.Api.Tests;

// The Blazor chat client (AssistantApi, the same file the ChatBox component uses) run against the real /api/chat/stream pipeline
public class BlazorChatClientTests : IClassFixture<RagTests.RagFactory>
{
    private readonly RagTests.RagFactory _factory;

    public BlazorChatClientTests(RagTests.RagFactory factory)
    {
        _factory = factory;
        _factory.Handler.Calls.Clear();
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public SingleClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private static string TextStream(params string[] pieces)
    {
        var sb = new StringBuilder();
        sb.Append("data: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":4}}}\n\n");
        sb.Append("data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n");
        foreach (var piece in pieces)
        {
            sb.Append("data: ").Append(JsonSerializer.Serialize(new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = piece } })).Append("\n\n");
        }

        sb.Append("data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":3}}\n\n");
        return sb.ToString();
    }

    private async Task<AssistantApi> ApiAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new AssistantApi(new SingleClientFactory(client));
    }

    [Fact]
    public async Task The_client_yields_start_then_each_piece_then_done_and_keeps_the_conversation_id()
    {
        _factory.Handler.Reply = _ => TextStream("Bảo hành ", "24 tháng");
        var api = await ApiAsync();

        var first = new List<AssistantStreamEvent>();
        await foreach (var e in api.ChatStreamAsync(null, "Xin chào")) first.Add(e);

        Assert.Equal(new[] { AssistantEventKind.Start, AssistantEventKind.Delta, AssistantEventKind.Delta, AssistantEventKind.Done }, first.Select(e => e.Kind));
        Assert.Equal(new[] { "Bảo hành ", "24 tháng" }, first.Where(e => e.Kind == AssistantEventKind.Delta).Select(e => e.Text));
        Assert.Equal("Bảo hành 24 tháng", first[^1].Text);
        var id = first[0].ConversationId;
        Assert.NotNull(id);
        Assert.Equal(id, first[^1].ConversationId);

        // The next message continues the same conversation: the provider receives the earlier turns
        _factory.Handler.Calls.Clear();
        await foreach (var _ in api.ChatStreamAsync(id, "Còn gì nữa?")) { }
        using var body = JsonDocument.Parse(Assert.Single(_factory.Handler.Calls).Body);
        Assert.Equal(3, body.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task After_a_reload_a_new_client_loads_the_saved_history_and_the_list()
    {
        _factory.Handler.Reply = _ => TextStream("Lịch sử ", "còn đó");
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Guid? id = null;
        await foreach (var e in new AssistantApi(new SingleClientFactory(client)).ChatStreamAsync(null, "Hỏi gì đó"))
        {
            id ??= e.ConversationId;
        }

        // F5: the page and its component state are gone; only the conversation id (from the URL) and the login remain
        var reloaded = new AssistantApi(new SingleClientFactory(client));
        var messages = await reloaded.GetMessagesAsync(id!.Value);
        var list = await reloaded.ListConversationsAsync();

        Assert.Equal(new[] { ("user", "Hỏi gì đó"), ("assistant", "Lịch sử còn đó") }, messages.Select(m => (m.Role, m.Content)));
        Assert.Equal(id, Assert.Single(list).Id);
        Assert.Equal("Hỏi gì đó", list[0].Title);
    }

    [Fact]
    public async Task Loading_another_users_conversation_gives_a_readable_exception()
    {
        var owner = await ApiAsync();
        Guid? id = null;
        await foreach (var e in owner.ChatStreamAsync(null, "Của tôi")) id ??= e.ConversationId;
        var stranger = await ApiAsync();

        var ex = await Assert.ThrowsAsync<AssistantRequestException>(() => stranger.GetMessagesAsync(id!.Value));

        Assert.Contains("Không tìm thấy", ex.Message);
    }

    [Fact]
    public async Task The_client_turns_a_refused_message_into_a_readable_exception()
    {
        var api = await ApiAsync();

        var ex = await Assert.ThrowsAsync<AssistantRequestException>(async () =>
        {
            await foreach (var _ in api.ChatStreamAsync(null, new string('a', 4001))) { }
        });

        Assert.Contains("quá dài", ex.Message);
    }
}
