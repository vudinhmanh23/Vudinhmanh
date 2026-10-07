using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesInventory.Application;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Infrastructure.Ai;

// Calls the Anthropic Messages API (POST /v1/messages) over HttpClient. Runs on the server only: the API key is read
// from configuration here and is never logged, returned or sent anywhere except the Anthropic request header.
public sealed class AnthropicChatService : IChatService
{
    private readonly HttpClient _client;
    private readonly AnthropicOptions _options;
    private readonly ShopSettings _shop;
    private readonly ILogger<AnthropicChatService> _logger;

    public AnthropicChatService(HttpClient client, IOptions<AnthropicOptions> options, IOptions<ShopSettings> shop, ILogger<AnthropicChatService> logger)
    {
        _client = client;
        _options = options.Value;
        _shop = shop.Value;
        _logger = logger;
    }

    public async Task<ChatAnswer> AskAsync(string question, CancellationToken cancellationToken = default)
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(BuildBody(model, question))
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
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Status and the provider's error type/message go to the log; the caller only gets a generic text
                _logger.LogError("Anthropic returned {Status}: {Error}", (int)response.StatusCode, ExtractError(body));
                throw new AssistantUnavailableException(response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "Dịch vụ AI đang quá tải, vui lòng thử lại sau ít phút."
                    : "Dịch vụ AI tạm thời không khả dụng.");
            }

            return ParseAnswer(body);
        }
    }

    private object BuildBody(string model, string question)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["max_tokens"] = _options.MaxTokens,
            // The rules go in the dedicated system field; the user text is only ever sent as a user message, never merged into it
            ["system"] = _options.SystemPrompt!.Replace("{ShopName}", _shop.Name),
            ["messages"] = new[] { new { role = "user", content = question } }
        };

        // Haiku-tier models do not support the effort setting (the API would reject it)
        if (!string.IsNullOrWhiteSpace(_options.Effort) && !_options.ModelTier.Equals("haiku", StringComparison.OrdinalIgnoreCase))
        {
            body["output_config"] = new { effort = _options.Effort };
        }

        return body;
    }

    private ChatAnswer ParseAnswer(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

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

            var stopReason = root.TryGetProperty("stop_reason", out var stop) ? stop.GetString() : null;
            var answer = text.ToString().Trim();
            if (answer.Length == 0)
            {
                // A refusal or a cut-off before any text: say so instead of returning an empty answer
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

            var usage = root.TryGetProperty("usage", out var u) ? u : default;
            return new ChatAnswer(answer, _options.ModelTier.ToLowerInvariant(), ReadInt(usage, "input_tokens"), ReadInt(usage, "output_tokens"));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "The Anthropic response could not be parsed");
            throw new AssistantUnavailableException("Dịch vụ AI trả về dữ liệu không hợp lệ.", ex);
        }
    }

    private static int ReadInt(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
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
