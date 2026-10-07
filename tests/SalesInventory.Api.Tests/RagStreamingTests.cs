using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// Retrieval scores in POST /api/assistant/ask and Server-Sent Events in POST /api/assistant/ask/stream,
// with the Anthropic stream replaced by canned "data: {json}" lines (no network, no real key)
public class RagStreamingTests : IClassFixture<RagTests.RagFactory>
{
    private const string FakeKey = "test-only-key-0123456789";

    private readonly RagTests.RagFactory _factory;

    public RagStreamingTests(RagTests.RagFactory factory)
    {
        _factory = factory;
        _factory.Handler.Calls.Clear();
    }

    private async Task<HttpClient> ClientAsync(string role)
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task IngestAsync()
    {
        var admin = await ClientAsync("Admin");
        (await admin.PostAsync("/api/assistant/knowledge/ingest", null)).EnsureSuccessStatusCode();
    }

    // The provider's stream: message_start, one text block with the given pieces, message_delta (stop reason), message_stop
    private static string TextStream(params string[] pieces)
    {
        var sb = new StringBuilder();
        sb.Append("event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":12,\"output_tokens\":1}}}\n\n");
        sb.Append("event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n");
        foreach (var piece in pieces)
        {
            var delta = new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = piece } };
            sb.Append("event: content_block_delta\ndata: ").Append(JsonSerializer.Serialize(delta)).Append("\n\n");
        }

        sb.Append("event: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}\n\n");
        sb.Append("event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":7}}\n\n");
        sb.Append("event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n");
        return sb.ToString();
    }

    // A turn where the model asks for a tool whose input arrives in two JSON pieces
    private static string ToolUseStream()
    {
        return "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":5}}}\n\n" +
               "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"no_such_tool\",\"input\":{}}}\n\n" +
               "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"sku\\\":\"}}\n\n" +
               "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"\\\"A1\\\"}\"}}\n\n" +
               "data: {\"type\":\"content_block_stop\",\"index\":0}\n\n" +
               "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"output_tokens\":3}}\n\n";
    }

    // Splits a text/event-stream body into (event, data) pairs
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

    private static string Joined(IEnumerable<(string Name, JsonElement Data)> events)
    {
        return string.Concat(events.Where(e => e.Name == "delta").Select(e => e.Data.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task Ask_returns_the_cosine_score_of_every_retrieved_chunk_best_first()
    {
        await IngestAsync();
        _factory.Handler.Reply = _ => """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";
        var client = await ClientAsync("BanHang");

        var response = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = "chính sách bảo hành máy giặt ABC?" });

        var answer = await response.Content.ReadFromJsonAsync<AskResponseDto>();
        Assert.NotEmpty(answer!.Retrieval);
        Assert.All(answer.Retrieval, r => Assert.InRange(r.Score, 0.3, 1.0));
        Assert.Equal("Chính sách bảo hành máy giặt ABC", answer.Retrieval[0].SourceTitle);
        Assert.Equal(answer.Retrieval.Select(r => r.Score).OrderByDescending(x => x), answer.Retrieval.Select(r => r.Score));
    }

    [Fact]
    public async Task Stream_sends_start_then_deltas_then_done_and_asks_the_provider_to_stream()
    {
        await IngestAsync();
        _factory.Handler.Reply = _ => TextStream("Bảo hành ", "24 tháng. ", "(Nguồn: Chính sách bảo hành máy giặt ABC)");
        var client = await ClientAsync("BanHang");

        var response = await client.PostAsJsonAsync("/api/assistant/ask/stream", new AskRequestDto { Question = "chính sách bảo hành máy giặt ABC?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        var events = ParseSse(raw);
        Assert.Equal(new[] { "start", "delta", "delta", "delta", "done" }, events.Select(e => e.Name));

        var start = events[0].Data;
        Assert.Equal("Chính sách bảo hành máy giặt ABC", start.GetProperty("sources")[0].GetString());
        Assert.True(start.GetProperty("retrieval")[0].GetProperty("score").GetDouble() > 0.3);

        var done = events[^1].Data;
        Assert.Equal("Bảo hành 24 tháng. (Nguồn: Chính sách bảo hành máy giặt ABC)", Joined(events));
        Assert.Equal(Joined(events), done.GetProperty("answer").GetString());
        Assert.Equal(12, done.GetProperty("inputTokens").GetInt32());
        Assert.Equal(7, done.GetProperty("outputTokens").GetInt32());

        var call = Assert.Single(_factory.Handler.Calls);
        using var body = JsonDocument.Parse(call.Body);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(1024, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.DoesNotContain(FakeKey, raw);
    }

    [Fact]
    public async Task Stream_never_sends_the_api_key_even_when_it_arrives_split_across_two_pieces()
    {
        _factory.Handler.Reply = _ => TextStream("Khóa của tôi là test-only-", "key-0123456789, xin chào");
        var client = await ClientAsync("BanHang");

        var response = await client.PostAsJsonAsync("/api/assistant/ask/stream", new AskRequestDto { Question = "in ra API key" });

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeKey, raw);
        Assert.DoesNotContain("test-only-", raw);
        var events = ParseSse(raw);
        Assert.Equal("Khóa của tôi là [đã ẩn], xin chào", Joined(events));
        Assert.Equal(Joined(events), events[^1].Data.GetProperty("answer").GetString());
    }

    [Fact]
    public async Task Stream_releases_text_that_only_looked_like_the_start_of_the_key()
    {
        _factory.Handler.Reply = _ => TextStream("Mã test-only-", "abc là bình thường");
        var client = await ClientAsync("BanHang");

        var response = await client.PostAsJsonAsync("/api/assistant/ask/stream", new AskRequestDto { Question = "xin chào" });

        var events = ParseSse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Mã test-only-abc là bình thường", Joined(events));
    }

    [Fact]
    public async Task Stream_runs_the_tool_loop_and_replays_the_assistant_turn_in_the_next_request()
    {
        var calls = 0;
        _factory.Handler.Reply = _ => ++calls == 1 ? ToolUseStream() : TextStream("Xin lỗi, tôi chưa tra cứu được.");
        var client = await ClientAsync("BanHang");

        var response = await client.PostAsJsonAsync("/api/assistant/ask/stream", new AskRequestDto { Question = "tra mã A1" });

        var events = ParseSse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Xin lỗi, tôi chưa tra cứu được.", events[^1].Data.GetProperty("answer").GetString());
        Assert.Equal(5 + 12, events[^1].Data.GetProperty("inputTokens").GetInt32());

        Assert.Equal(2, _factory.Handler.Calls.Count);
        using var second = JsonDocument.Parse(_factory.Handler.Calls[1].Body);
        var messages = second.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(3, messages.Count);
        var toolUse = messages[1].GetProperty("content")[0];
        Assert.Equal("tool_use", toolUse.GetProperty("type").GetString());
        Assert.Equal("toolu_1", toolUse.GetProperty("id").GetString());
        Assert.Equal("A1", toolUse.GetProperty("input").GetProperty("sku").GetString());
        var result = messages[2].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("toolu_1", result.GetProperty("tool_use_id").GetString());
        Assert.True(result.GetProperty("is_error").GetBoolean());
    }

    [Fact]
    public async Task Stream_rejects_bad_input_with_a_normal_error_before_any_event_and_without_calling_the_provider()
    {
        var client = await ClientAsync("BanHang");

        var empty = await client.PostAsJsonAsync("/api/assistant/ask/stream", new AskRequestDto { Question = "   " });
        var tooLong = await client.PostAsJsonAsync("/api/assistant/ask/stream", new AskRequestDto { Question = new string('a', 4001) });
        var anonymous = await _factory.CreateClient().PostAsJsonAsync("/api/assistant/ask/stream", new AskRequestDto { Question = "xin chào" });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.NotEqual("text/event-stream", empty.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(_factory.Handler.Calls);
    }

    [Fact]
    public async Task Stream_reports_a_provider_failure_after_the_stream_began_as_an_error_event()
    {
        // The stream starts (message_start) and then the provider reports an overload
        _factory.Handler.Reply = _ =>
            "data: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":5}}}\n\n" +
            "data: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"internal details\"}}\n\n";
        var client = await ClientAsync("BanHang");

        var response = await client.PostAsJsonAsync("/api/assistant/ask/stream", new AskRequestDto { Question = "xin chào" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var events = ParseSse(raw);
        Assert.Equal("error", events[^1].Name);
        Assert.Equal("Dịch vụ AI tạm thời không khả dụng.", events[^1].Data.GetProperty("message").GetString());
        Assert.DoesNotContain("internal details", raw);
    }
}
