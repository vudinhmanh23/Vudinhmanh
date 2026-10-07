using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SalesInventory.Web.Models;

namespace SalesInventory.Web.Services;

/// <summary>
/// Talks to POST /api/chat/stream. Blazor Server runs this on the web server, never in the browser, and the
/// AI key lives only in the API's configuration, so neither the browser nor this project ever sees it.
/// </summary>
public class AssistantApi
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AssistantApi(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Sends the message and yields the answer as it is written. <paramref name="conversationId"/> null starts a new
    /// conversation (its id comes back in the Start event). Throws <see cref="AssistantRequestException"/> when the API
    /// refuses the message before streaming starts; a failure later in the stream arrives as an Error event.
    /// </summary>
    public async IAsyncEnumerable<AssistantStreamEvent> ChatStreamAsync(Guid? conversationId, string message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(ApiClient.Name);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat/stream")
        {
            Content = JsonContent.Create(new { conversationId, message })
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        // ResponseHeadersRead: hand over the response as soon as the headers arrive, instead of waiting for the whole body
        using var response = await SendAsync(client, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AssistantRequestException(await ReadErrorAsync(response, cancellationToken));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? name = null;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal) && name is not null)
            {
                var parsed = Parse(name, line[5..].Trim());
                name = null;
                if (parsed is not null)
                {
                    yield return parsed;
                }
            }
        }
    }

    /// <summary>The saved messages of a conversation, oldest first. Throws <see cref="AssistantRequestException"/> when it cannot be loaded.</summary>
    public async Task<List<ChatHistoryMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(ApiClient.Name);
        using var response = await GetAsync(client, $"api/chat/{conversationId}", cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<ChatHistoryMessage>>(cancellationToken: cancellationToken) ?? new List<ChatHistoryMessage>();
    }

    /// <summary>The user's conversations, newest first. Empty when the list cannot be loaded (it is only a convenience).</summary>
    public async Task<List<ConversationSummary>> ListConversationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ApiClient.Name);
            using var response = await GetAsync(client, "api/chat", cancellationToken);
            return await response.Content.ReadFromJsonAsync<List<ConversationSummary>>(cancellationToken: cancellationToken) ?? new List<ConversationSummary>();
        }
        catch (Exception ex) when (ex is AssistantRequestException or JsonException)
        {
            return new List<ConversationSummary>();
        }
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new AssistantRequestException("Không kết nối được tới máy chủ, vui lòng thử lại sau.");
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                throw new AssistantRequestException(await ReadErrorAsync(response, cancellationToken));
            }
        }

        return response;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new AssistantRequestException("Không kết nối được tới máy chủ, vui lòng thử lại sau.");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // The API explains 400 / 429 / 503 in the "detail" of its ProblemDetails body
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // not a ProblemDetails body; use the fixed texts below
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Phiên đăng nhập đã hết hạn, vui lòng đăng nhập lại.",
            HttpStatusCode.Forbidden => "Bạn không có quyền dùng trợ lý AI.",
            HttpStatusCode.TooManyRequests => "Bạn hỏi quá nhanh, vui lòng thử lại sau ít phút.",
            _ => "Trợ lý AI tạm thời không khả dụng."
        };
    }

    private static AssistantStreamEvent? Parse(string name, string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            switch (name)
            {
                case "start":
                    return new AssistantStreamEvent { Kind = AssistantEventKind.Start, ConversationId = ConversationId(root), Sources = Sources(root), Retrieval = Retrieval(root) };
                case "delta":
                    return new AssistantStreamEvent { Kind = AssistantEventKind.Delta, Text = Text(root, "text") };
                case "done":
                    return new AssistantStreamEvent
                    {
                        Kind = AssistantEventKind.Done,
                        ConversationId = ConversationId(root),
                        Text = Text(root, "answer"),
                        Sources = Sources(root),
                        Retrieval = Retrieval(root),
                        InputTokens = root.TryGetProperty("inputTokens", out var i) ? i.GetInt32() : 0,
                        OutputTokens = root.TryGetProperty("outputTokens", out var o) ? o.GetInt32() : 0
                    };
                case "error":
                    return new AssistantStreamEvent { Kind = AssistantEventKind.Error, Text = Text(root, "message") };
                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid? ConversationId(JsonElement root)
    {
        return root.TryGetProperty("conversationId", out var id) && id.TryGetGuid(out var guid) ? guid : null;
    }

    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static List<string> Sources(JsonElement root)
    {
        return root.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array
            ? sources.EnumerateArray().Select(s => s.GetString()).OfType<string>().ToList()
            : new List<string>();
    }

    private static List<AssistantChunk> Retrieval(JsonElement root)
    {
        return root.TryGetProperty("retrieval", out var chunks) && chunks.ValueKind == JsonValueKind.Array
            ? chunks.EnumerateArray().Select(c => new AssistantChunk(Text(c, "sourceTitle"), c.TryGetProperty("score", out var s) ? s.GetDouble() : 0)).ToList()
            : new List<AssistantChunk>();
    }
}
