using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesInventory.Application;
using SalesInventory.Application.Assistant;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Infrastructure.Ai;

// Calls the Anthropic Messages API (POST /v1/messages) over HttpClient and runs the tool-use loop: when the model asks
// for a tool, the matching .NET function reads real data, its result goes back to the model, and this repeats until
// the model answers. Runs on the server only: the API key is read from configuration here and is never logged,
// returned or sent anywhere except the Anthropic request header.
public sealed class AnthropicChatService : IChatService
{
    // A tool result larger than this is cut before it goes back into the prompt (it costs tokens too)
    private const int MaxToolResultLength = 4000;

    private readonly HttpClient _client;
    private readonly AnthropicOptions _options;
    private readonly ShopSettings _shop;
    private readonly AssistantToolRegistry _tools;
    private readonly ILogger<AnthropicChatService> _logger;

    public AnthropicChatService(
        HttpClient client,
        IOptions<AnthropicOptions> options,
        IOptions<ShopSettings> shop,
        AssistantToolRegistry tools,
        ILogger<AnthropicChatService> logger)
    {
        _client = client;
        _options = options.Value;
        _shop = shop.Value;
        _tools = tools;
        _logger = logger;
    }

    public async Task<ChatAnswer> AskAsync(string question, ChatCaller caller, CancellationToken cancellationToken = default)
    {
        // Limits are checked before anything is sent, so an oversized or empty input never costs a provider call
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ChatInputException("Câu hỏi không được để trống.");
        }

        question = question.Trim();
        if (question.Length > _options.MaxQuestionLength)
        {
            throw new ChatInputException($"Câu hỏi quá dài (tối đa {_options.MaxQuestionLength} ký tự).");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("The assistant was called but no API key is configured (Anthropic:ApiKey or ANTHROPIC_API_KEY)");
            throw new AssistantUnavailableException("Trợ lý AI chưa được cấu hình.");
        }

        // Fail closed: without the configured rules the assistant must not run with a weaker default
        if (string.IsNullOrWhiteSpace(_options.SystemPrompt))
        {
            _logger.LogError("Anthropic:SystemPrompt is empty; refusing to call the model without the standing rules");
            throw new AssistantUnavailableException("Trợ lý AI chưa được cấu hình.");
        }

        var model = _options.ResolveModel();
        if (model is null)
        {
            _logger.LogError("No model is configured for tier '{Tier}' under Anthropic:Models", _options.ModelTier);
            throw new AssistantUnavailableException("Trợ lý AI chưa được cấu hình.");
        }

        // Only the tools this caller's roles allow are offered to the model
        var toolDefinitions = BuildToolDefinitions(_tools.GetFor(caller.Roles));

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = question }
        };

        var toolsUsed = new List<string>();
        var inputTokens = 0;
        var outputTokens = 0;

        for (var round = 0; ; round++)
        {
            using var response = await SendAsync(BuildBody(model, messages, toolDefinitions), cancellationToken);
            var root = response.RootElement;

            inputTokens += ReadUsage(root, "input_tokens");
            outputTokens += ReadUsage(root, "output_tokens");

            var stopReason = root.TryGetProperty("stop_reason", out var stop) ? stop.GetString() : null;
            if (stopReason != "tool_use")
            {
                return new ChatAnswer(ExtractAnswer(root, stopReason), _options.ModelTier.ToLowerInvariant(), inputTokens, outputTokens, toolsUsed);
            }

            // The model keeps asking for tools: stop at the limit and say so instead of looping on
            if (round >= _options.MaxToolRounds)
            {
                _logger.LogWarning("The model still asked for tools after {Rounds} rounds; stopping", _options.MaxToolRounds);
                return new ChatAnswer(
                    "Xin lỗi, tôi chưa tra cứu xong thông tin này. Vui lòng thử lại với câu hỏi cụ thể hơn.",
                    _options.ModelTier.ToLowerInvariant(), inputTokens, outputTokens, toolsUsed);
            }

            // The assistant turn goes back exactly as received (including any thinking blocks), then ONE user turn
            // carries every tool result
            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = JsonNode.Parse(root.GetProperty("content").GetRawText())
            });

            var results = new JsonArray();
            foreach (var block in root.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "tool_use")
                {
                    // One at a time: the tools share a scoped DbContext, which is not thread-safe
                    results.Add(await RunToolAsync(block, caller, toolsUsed, cancellationToken));
                }
            }

            messages.Add(new JsonObject { ["role"] = "user", ["content"] = results });
        }
    }

    // Runs one tool_use block and builds the matching tool_result block. A failing or unknown tool is reported to the
    // model as an error result, never as an exception, so the model can explain it to the user.
    private async Task<JsonObject> RunToolAsync(JsonElement block, ChatCaller caller, List<string> toolsUsed, CancellationToken cancellationToken)
    {
        var id = block.GetProperty("id").GetString();
        var name = block.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var input = block.TryGetProperty("input", out var i) ? i : default;

        ToolOutcome outcome;
        var tool = _tools.Find(name, caller.Roles);
        if (tool is null)
        {
            _logger.LogWarning("The model asked for an unknown or not permitted tool '{Tool}'", name);
            outcome = new ToolOutcome("Công cụ không khả dụng.", IsError: true);
        }
        else
        {
            try
            {
                outcome = await tool.ExecuteAsync(input, cancellationToken);
                if (!toolsUsed.Contains(name))
                {
                    toolsUsed.Add(name);
                }

                _logger.LogInformation("Assistant tool {Tool} finished (error: {IsError})", name, outcome.IsError);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Details stay in the log; the model only learns that the lookup failed
                _logger.LogError(ex, "Assistant tool {Tool} failed", name);
                outcome = new ToolOutcome("Không tra cứu được dữ liệu lúc này.", IsError: true);
            }
        }

        var content = outcome.Content.Length > MaxToolResultLength ? outcome.Content[..MaxToolResultLength] : outcome.Content;
        var result = new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = content };
        if (outcome.IsError)
        {
            result["is_error"] = true;
        }

        return result;
    }

    private static JsonArray BuildToolDefinitions(IReadOnlyList<IAssistantTool> tools)
    {
        var definitions = new JsonArray();
        foreach (var tool in tools)
        {
            definitions.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = JsonNode.Parse(tool.InputSchemaJson)
            });
        }

        return definitions;
    }

    private JsonObject BuildBody(string model, JsonArray messages, JsonArray toolDefinitions)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = _options.MaxTokens,
            // The rules go in the dedicated system field; user text is only ever sent as a user message, never merged into it
            ["system"] = _options.SystemPrompt!.Replace("{ShopName}", _shop.Name),
            // A copy, so the loop can keep appending to its own list while this body is serialized
            ["messages"] = JsonNode.Parse(messages.ToJsonString())
        };

        // tool_choice stays at its default (auto): the model decides when a lookup is needed
        if (toolDefinitions.Count > 0)
        {
            body["tools"] = JsonNode.Parse(toolDefinitions.ToJsonString());
        }

        // Haiku-tier models do not support the effort setting (the API would reject it)
        if (!string.IsNullOrWhiteSpace(_options.Effort) && !_options.ModelTier.Equals("haiku", StringComparison.OrdinalIgnoreCase))
        {
            body["output_config"] = new JsonObject { ["effort"] = _options.Effort };
        }

        return body;
    }

    // One request to the provider; returns the parsed JSON body, or throws a user-safe AssistantUnavailableException
    private async Task<JsonDocument> SendAsync(JsonObject body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", _options.ApiVersion);

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "The Anthropic request failed before a response arrived");
            throw new AssistantUnavailableException("Không kết nối được tới dịch vụ AI, vui lòng thử lại sau.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Status and the provider's error type/message go to the log; the caller only gets a generic text
                _logger.LogError("Anthropic returned {Status}: {Error}", (int)response.StatusCode, ExtractError(text));
                throw new AssistantUnavailableException(response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "Dịch vụ AI đang quá tải, vui lòng thử lại sau ít phút."
                    : "Dịch vụ AI tạm thời không khả dụng.");
            }

            try
            {
                return JsonDocument.Parse(text);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "The Anthropic response could not be parsed");
                throw new AssistantUnavailableException("Dịch vụ AI trả về dữ liệu không hợp lệ.", ex);
            }
        }
    }

    // The final answer: all text blocks joined. A refusal or a cut-off before any text gets a friendly message.
    private string ExtractAnswer(JsonElement root, string? stopReason)
    {
        var text = new StringBuilder();
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text" && block.TryGetProperty("text", out var t))
                {
                    text.Append(t.GetString());
                }
            }
        }

        var answer = text.ToString().Trim();
        if (answer.Length == 0)
        {
            answer = stopReason == "refusal"
                ? "Xin lỗi, tôi không thể trả lời yêu cầu này."
                : "Xin lỗi, tôi chưa tạo được câu trả lời. Vui lòng thử lại với câu hỏi ngắn gọn hơn.";
            _logger.LogWarning("The model returned no text (stop_reason {StopReason})", stopReason);
        }

        // Defence in depth: the key is never put in a prompt, but if it ever showed up in an answer it must not leave the server
        if (!string.IsNullOrEmpty(_options.ApiKey) && answer.Contains(_options.ApiKey, StringComparison.Ordinal))
        {
            _logger.LogWarning("The model answer contained the API key; it was redacted");
            answer = answer.Replace(_options.ApiKey, "[đã ẩn]", StringComparison.Ordinal);
        }

        return answer;
    }

    private static int ReadUsage(JsonElement root, string name)
    {
        return root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    // { "type": "error", "error": { "type": "...", "message": "..." } } -> "type: message"; falls back to a short raw prefix
    private static string ExtractError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                var type = error.TryGetProperty("type", out var t) ? t.GetString() : null;
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                return $"{type}: {message}";
            }
        }
        catch (JsonException)
        {
            // not JSON; fall through
        }

        return body.Length > 300 ? body[..300] : body;
    }
}
