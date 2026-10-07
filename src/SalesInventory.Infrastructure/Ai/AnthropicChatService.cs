using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
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

    private const string NoTextAnswer = "Xin lỗi, tôi chưa tạo được câu trả lời. Vui lòng thử lại với câu hỏi ngắn gọn hơn.";
    private const string RefusalAnswer = "Xin lỗi, tôi không thể trả lời yêu cầu này.";
    // What the user gets when the model starts to recite its own rules
    private const string LeakRefusalAnswer = "Xin lỗi, tôi không thể chia sẻ thông tin này.";
    private const string ToolLimitAnswer = "Xin lỗi, tôi chưa tra cứu xong thông tin này. Vui lòng thử lại với câu hỏi cụ thể hơn.";

    private readonly HttpClient _client;
    private readonly AnthropicOptions _options;
    private readonly AiSafetyOptions _safety;
    private readonly ShopSettings _shop;
    private readonly AssistantToolRegistry _tools;
    private readonly IKnowledgeRetriever _retriever;
    private readonly KnowledgeOptions _knowledge;
    private readonly PromptGuard _guard;
    private readonly ILogger<AnthropicChatService> _logger;

    public AnthropicChatService(
        HttpClient client,
        IOptions<AnthropicOptions> options,
        IOptions<AiSafetyOptions> safety,
        IOptions<ShopSettings> shop,
        AssistantToolRegistry tools,
        IKnowledgeRetriever retriever,
        IOptions<KnowledgeOptions> knowledge,
        PromptGuard guard,
        ILogger<AnthropicChatService> logger)
    {
        _client = client;
        _options = options.Value;
        _safety = safety.Value;
        _shop = shop.Value;
        _tools = tools;
        _retriever = retriever;
        _knowledge = knowledge.Value;
        _guard = guard;
        _logger = logger;
    }

    public async Task<ChatAnswer> AskAsync(string question, ChatCaller caller, CancellationToken cancellationToken = default)
    {
        var (trimmed, tier, model) = ValidateRequest(question);
        question = trimmed;
        var userId = caller.UserId ?? "unknown";

        // Only the tools this caller's roles allow are offered to the model
        var toolDefinitions = BuildToolDefinitions(_tools.GetFor(caller.Roles));

        // RAG: the closest knowledge passages travel with the question, so policy answers come from real documents
        var hits = await RetrieveAsync(question, cancellationToken);
        var sources = hits.Select(h => h.SourceTitle).Distinct().ToList();
        var retrieval = ToRetrieved(hits);

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = BuildUserMessage(question, hits) }
        };

        var system = BuildSystemPrompt();
        var toolsUsed = new List<string>();
        var inputTokens = 0;
        var outputTokens = 0;

        for (var round = 0; ; round++)
        {
            using var response = await SendAsync(BuildBody(tier, model, messages, toolDefinitions), cancellationToken);
            var root = response.RootElement;

            inputTokens += ReadUsage(root, "input_tokens");
            outputTokens += ReadUsage(root, "output_tokens");

            var stopReason = root.TryGetProperty("stop_reason", out var stop) ? stop.GetString() : null;
            if (stopReason != "tool_use")
            {
                LogUsage(userId, tier, question, inputTokens, outputTokens, round + 1, toolsUsed.Count, "completed");
                return new ChatAnswer(ExtractAnswer(root, stopReason, system), tier, inputTokens, outputTokens, toolsUsed, sources, retrieval);
            }

            // The model keeps asking for tools: stop at the limit and say so instead of looping on
            if (round >= _safety.MaxToolRounds)
            {
                _logger.LogWarning("The model still asked for tools after {Rounds} rounds; stopping", _safety.MaxToolRounds);
                LogUsage(userId, tier, question, inputTokens, outputTokens, round + 1, toolsUsed.Count, "tool-limit");
                return new ChatAnswer(ToolLimitAnswer, tier, inputTokens, outputTokens, toolsUsed, sources, retrieval);
            }

            // The assistant turn goes back exactly as received (including any thinking blocks), then ONE user turn
            // carries every tool result
            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = JsonNode.Parse(root.GetProperty("content").GetRawText())
            });
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = await RunToolsAsync(root.GetProperty("content"), caller, toolsUsed, cancellationToken) });
        }
    }

    // Streaming version of AskAsync: the same checks, retrieval and tool loop, but text is passed on as the model writes it
    // (Messages API with "stream": true, Server-Sent Events). The key is never part of a streamed piece: see HeldBackLength.
    public async IAsyncEnumerable<ChatStreamEvent> AskStreamAsync(
        string question, ChatCaller caller, IReadOnlyList<ChatTurn>? history = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (trimmed, tier, model) = ValidateRequest(question);
        question = trimmed;
        var userId = caller.UserId ?? "unknown";

        var toolDefinitions = BuildToolDefinitions(_tools.GetFor(caller.Roles));
        var hits = await RetrieveAsync(question, cancellationToken);
        var sources = hits.Select(h => h.SourceTitle).Distinct().ToList();
        var retrieval = ToRetrieved(hits);

        yield return new ChatStreamStart(sources, retrieval);

        // Earlier turns go first (what was really asked and answered), then this question with its documents. Earlier questions
        // are user text too, so they get the same "untrusted" marking and secret filtering as the new one.
        var messages = new JsonArray();
        foreach (var turn in history ?? Array.Empty<ChatTurn>())
        {
            var text = _guard.Scrub(turn.Content);
            messages.Add(new JsonObject { ["role"] = turn.Role, ["content"] = turn.Role == "user" ? WrapQuestion(text) : text });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = BuildUserMessage(question, hits) });

        var system = BuildSystemPrompt();
        var toolsUsed = new List<string>();
        var answer = new StringBuilder();
        var inputTokens = 0;
        var outputTokens = 0;
        var rounds = 0;
        var outcome = "cancelled-or-failed";

        // The usage line is written however the stream ends (done, cancelled by the client, or failed), because the input
        // tokens of a request that was already sent are billed in every case. Tokens of a turn that was cut off before it
        // finished are not known yet and are not included.
        try
        {
            for (var round = 0; ; round++)
            {
                rounds = round + 1;
                var body = BuildBody(tier, model, messages, toolDefinitions);
                body["stream"] = true;

                var turn = new StreamedTurn();
                var pending = new StringBuilder();
                var roundHadText = false;

                await foreach (var piece in StreamTurnAsync(body, turn, cancellationToken))
                {
                    pending.Append(piece);

                    // Secrets are masked at once. A run copied from the system prompt is held back while it grows: if it reaches
                    // LeakWindow characters the model is reciting its rules, so the answer is cut off here instead of sent on.
                    var safe = _guard.Scrub(pending.ToString());
                    if (_guard.LeaksSystemPrompt(safe, system))
                    {
                        outcome = "leak-blocked";
                        _logger.LogWarning("The model started to repeat the system prompt; the answer was cut off");
                        var cut = answer.Length > 0 ? "\n\n" + LeakRefusalAnswer : LeakRefusalAnswer;
                        answer.Append(cut);
                        yield return new ChatStreamDelta(cut);
                        yield return new ChatStreamDone(new ChatAnswer(answer.ToString().Trim(), tier, inputTokens, outputTokens, toolsUsed, sources, retrieval));
                        yield break; // leaving the loop also closes the request to the model
                    }

                    // Keep back only a tail that could be the start of a secret or of a copied run; the rest is safe to send
                    var hold = _guard.HeldBackLength(safe, system);
                    var emit = safe[..^hold];
                    pending.Clear().Append(safe[^hold..]);
                    if (emit.Length > 0)
                    {
                        yield return new ChatStreamDelta(Separate(answer, ref roundHadText, emit));
                    }
                }

                // End of the turn: whatever was held back did not turn out to be a secret or a leak, so it can go out now
                var rest = _guard.Scrub(pending.ToString());
                if (rest.Length > 0)
                {
                    yield return new ChatStreamDelta(Separate(answer, ref roundHadText, rest));
                }

                inputTokens += turn.InputTokens;
                outputTokens += turn.OutputTokens;

                if (turn.StopReason != "tool_use")
                {
                    if (answer.Length == 0)
                    {
                        var fallback = turn.StopReason == "refusal" ? RefusalAnswer : NoTextAnswer;
                        _logger.LogWarning("The model returned no text (stop_reason {StopReason})", turn.StopReason);
                        answer.Append(fallback);
                        yield return new ChatStreamDelta(fallback);
                    }

                    outcome = "completed";
                    yield return new ChatStreamDone(new ChatAnswer(answer.ToString().Trim(), tier, inputTokens, outputTokens, toolsUsed, sources, retrieval));
                    yield break;
                }

                if (round >= _safety.MaxToolRounds)
                {
                    _logger.LogWarning("The model still asked for tools after {Rounds} rounds; stopping", _safety.MaxToolRounds);
                    var message = answer.Length > 0 ? "\n\n" + ToolLimitAnswer : ToolLimitAnswer;
                    answer.Append(message);
                    outcome = "tool-limit";
                    yield return new ChatStreamDelta(message);
                    yield return new ChatStreamDone(new ChatAnswer(answer.ToString().Trim(), tier, inputTokens, outputTokens, toolsUsed, sources, retrieval));
                    yield break;
                }

                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = turn.Content.DeepClone() });
                using var content = JsonDocument.Parse(turn.Content.ToJsonString());
                messages.Add(new JsonObject { ["role"] = "user", ["content"] = await RunToolsAsync(content.RootElement, caller, toolsUsed, cancellationToken) });
            }
        }
        finally
        {
            LogUsage(userId, tier, question, inputTokens, outputTokens, rounds, toolsUsed.Count, outcome);
        }
    }

    // The standing rules, with the shop name filled in. They go in the dedicated "system" field and nothing else is ever added to it.
    private string BuildSystemPrompt() => _options.SystemPrompt!.Replace("{ShopName}", _shop.Name);

    // One line per request so the cost can be followed in the logs: who (an opaque user id), which tier, the tokens and the cost
    // estimated from the prices in AiSafety:Pricing. It carries counts only: never the question, the answer or any key.
    private void LogUsage(string userId, string tier, string question, int inputTokens, int outputTokens, int rounds, int toolCount, string outcome)
    {
        var cost = _safety.EstimateCost(tier, inputTokens, outputTokens);
        _logger.LogInformation(
            "Assistant usage: user={UserId} tier={Tier} inputTokens={InputTokens} outputTokens={OutputTokens} totalTokens={TotalTokens} estimatedCost={EstimatedCost} {Currency} rounds={Rounds} tools={ToolCount} questionChars={QuestionChars} outcome={Outcome}",
            userId, tier, inputTokens, outputTokens, inputTokens + outputTokens,
            cost?.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown", _safety.Currency,
            rounds, toolCount, question.Length, outcome);
    }

    // Text written after a tool call starts on its own paragraph. Also records what was sent, so the final answer matches the stream.
    private static string Separate(StringBuilder answer, ref bool roundHadText, string text)
    {
        var piece = !roundHadText && answer.Length > 0 ? "\n\n" + text : text;
        roundHadText = true;
        answer.Append(piece);
        return piece;
    }

    // Everything checked before a provider call is made; an oversized or empty input never costs money
    private (string Question, string Tier, string Model) ValidateRequest(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ChatInputException("Câu hỏi không được để trống.");
        }

        question = question.Trim();
        if (question.Length > _safety.MaxQuestionLength)
        {
            throw new ChatInputException($"Câu hỏi quá dài (tối đa {_safety.MaxQuestionLength} ký tự).");
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

        // The rules are configuration text; a key or a connection string pasted into them would be sent to the model on every call
        if (_guard.ContainsSecret(BuildSystemPrompt()))
        {
            _logger.LogError("Anthropic:SystemPrompt contains a configured secret; refusing to send it to the model");
            throw new AssistantUnavailableException("Trợ lý AI chưa được cấu hình.");
        }

        // Anything that looks like a key or a connection string is masked before it can reach a prompt
        question = _guard.Scrub(question);

        // Short questions go to the cheaper tier; the model id itself only ever comes from configuration
        var tier = _safety.TierFor(question, _options.ModelTier);
        var model = _options.ResolveModel(tier);
        if (model is null)
        {
            _logger.LogError("No model is configured for tier '{Tier}' under Anthropic:Models", tier);
            throw new AssistantUnavailableException("Trợ lý AI chưa được cấu hình.");
        }

        return (question, tier, model);
    }

    private static IReadOnlyList<RetrievedChunk> ToRetrieved(IReadOnlyList<KnowledgeHit> hits)
    {
        return hits.Select(h => new RetrievedChunk(h.SourceTitle, Math.Round(h.Score, 4))).ToList();
    }

    // The passages for this question. Retrieval is best-effort: when embeddings are not configured or the provider fails,
    // the assistant still answers (from its tools) and, for policy questions, says it does not know.
    private async Task<IReadOnlyList<KnowledgeHit>> RetrieveAsync(string question, CancellationToken cancellationToken)
    {
        if (_knowledge.TopK <= 0)
        {
            return Array.Empty<KnowledgeHit>();
        }

        try
        {
            return await _retriever.SearchAsync(question, _knowledge.TopK, cancellationToken);
        }
        catch (Exception ex) when (ex is AssistantUnavailableException or HttpRequestException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Knowledge retrieval failed; answering without documents");
            return Array.Empty<KnowledgeHit>();
        }
    }

    // The user turn. The question is ALWAYS wrapped in a tag marked untrusted, and the system prompt says that nothing inside
    // it is an instruction. The text is XML-escaped, so it cannot close the tag and pose as a new section or as the rules.
    // Retrieved passages come first in their own tag; they are escaped and secret-filtered the same way.
    internal string BuildUserMessage(string question, IReadOnlyList<KnowledgeHit> hits)
    {
        if (hits.Count == 0)
        {
            return WrapQuestion(question);
        }

        var message = new StringBuilder();
        message.Append("<documents>\n");
        foreach (var hit in hits)
        {
            message.Append("<document source=\"").Append(Escape(hit.SourceTitle)).Append("\">\n")
                .Append(Escape(_guard.Scrub(hit.Content))).Append("\n</document>\n");
        }

        message.Append("</documents>\n\n").Append(WrapQuestion(question));
        return message.ToString();
    }

    private static string WrapQuestion(string question) => "<question trust=\"untrusted\">" + Escape(question) + "</question>";

    private static string Escape(string text) => System.Security.SecurityElement.Escape(text) ?? string.Empty;

    // Runs every tool_use block of one assistant turn and returns the tool_result blocks for the next user turn
    private async Task<JsonArray> RunToolsAsync(JsonElement content, ChatCaller caller, List<string> toolsUsed, CancellationToken cancellationToken)
    {
        var results = new JsonArray();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "tool_use")
            {
                // One at a time: the tools share a scoped DbContext, which is not thread-safe
                results.Add(await RunToolAsync(block, caller, toolsUsed, cancellationToken));
            }
        }

        return results;
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

        var content = _guard.Scrub(outcome.Content.Length > MaxToolResultLength ? outcome.Content[..MaxToolResultLength] : outcome.Content);
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

    private JsonObject BuildBody(string tier, string model, JsonArray messages, JsonArray toolDefinitions)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = _safety.MaxTokens,
            // The rules go in the dedicated system field; user text is only ever sent as a user message, never merged into it
            ["system"] = BuildSystemPrompt(),
            // A copy, so the loop can keep appending to its own list while this body is serialized
            ["messages"] = JsonNode.Parse(messages.ToJsonString())
        };

        // tool_choice stays at its default (auto): the model decides when a lookup is needed
        if (toolDefinitions.Count > 0)
        {
            body["tools"] = JsonNode.Parse(toolDefinitions.ToJsonString());
        }

        // Haiku-tier models do not support the effort setting (the API would reject it)
        if (!string.IsNullOrWhiteSpace(_options.Effort) && !tier.Equals("haiku", StringComparison.OrdinalIgnoreCase))
        {
            body["output_config"] = new JsonObject { ["effort"] = _options.Effort };
        }

        return body;
    }

    private HttpRequestMessage CreateRequest(JsonObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", _options.ApiVersion);
        return request;
    }

    // Sends the request; connection problems become a user-safe AssistantUnavailableException
    private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.SendAsync(request, completion, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "The Anthropic request failed before a response arrived");
            throw new AssistantUnavailableException("Không kết nối được tới dịch vụ AI, vui lòng thử lại sau.", ex);
        }
    }

    // Status and the provider's error type/message go to the log; the caller only gets a generic text
    private AssistantUnavailableException ProviderFailure(HttpStatusCode status, string body)
    {
        _logger.LogError("Anthropic returned {Status}: {Error}", (int)status, ExtractError(body));
        return new AssistantUnavailableException(status == HttpStatusCode.TooManyRequests
            ? "Dịch vụ AI đang quá tải, vui lòng thử lại sau ít phút."
            : "Dịch vụ AI tạm thời không khả dụng.");
    }

    // One request to the provider; returns the parsed JSON body, or throws a user-safe AssistantUnavailableException
    private async Task<JsonDocument> SendAsync(JsonObject body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(body);
        using var response = await SendRequestAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ProviderFailure(response.StatusCode, text);
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

    // What one streamed assistant turn turned out to be: its content blocks (for the next request), why it stopped, its tokens
    private sealed class StreamedTurn
    {
        public JsonArray Content { get; } = new();
        public string? StopReason { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    // A content block while its pieces are still arriving
    private sealed class BlockBuilder
    {
        public string Type = string.Empty;
        public string? Id;
        public string? Name;
        public string? Data;
        public readonly StringBuilder Text = new();
        public readonly StringBuilder Thinking = new();
        public readonly StringBuilder Json = new();
        public string? Signature;
    }

    // One streaming request: yields each piece of answer text as it arrives and fills `turn` when the message is complete.
    // The stream is a series of "data: {json}" lines; message_start carries the input tokens, content_block_* the blocks,
    // message_delta the stop reason and output tokens.
    private async IAsyncEnumerable<string> StreamTurnAsync(
        JsonObject body, StreamedTurn turn, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = CreateRequest(body);
        using var response = await SendRequestAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ProviderFailure(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var blocks = new SortedDictionary<int, BlockBuilder>();
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            using var json = TryParse(line[5..].Trim());
            if (json is null)
            {
                continue;
            }

            var root = json.RootElement;
            switch (Str(root, "type"))
            {
                case "message_start":
                    turn.InputTokens = ReadUsage(root.GetProperty("message"), "input_tokens");
                    break;

                case "content_block_start":
                    var start = root.GetProperty("content_block");
                    blocks[root.GetProperty("index").GetInt32()] = new BlockBuilder
                    {
                        Type = Str(start, "type") ?? string.Empty,
                        Id = Str(start, "id"),
                        Name = Str(start, "name"),
                        Data = Str(start, "data")
                    };
                    break;

                case "content_block_delta":
                    if (!blocks.TryGetValue(root.GetProperty("index").GetInt32(), out var block))
                    {
                        break;
                    }

                    var delta = root.GetProperty("delta");
                    switch (Str(delta, "type"))
                    {
                        case "text_delta":
                            var text = Str(delta, "text") ?? string.Empty;
                            block.Text.Append(text);
                            if (text.Length > 0)
                            {
                                yield return text;
                            }

                            break;
                        case "thinking_delta":
                            block.Thinking.Append(Str(delta, "thinking"));
                            break;
                        case "signature_delta":
                            block.Signature = Str(delta, "signature");
                            break;
                        case "input_json_delta":
                            block.Json.Append(Str(delta, "partial_json"));
                            break;
                    }

                    break;

                case "message_delta":
                    if (root.TryGetProperty("delta", out var messageDelta))
                    {
                        turn.StopReason = Str(messageDelta, "stop_reason") ?? turn.StopReason;
                    }

                    turn.OutputTokens = ReadUsage(root, "output_tokens");
                    break;

                case "error":
                    var error = root.TryGetProperty("error", out var e) ? $"{Str(e, "type")}: {Str(e, "message")}" : "unknown";
                    _logger.LogError("Anthropic stream error: {Error}", error);
                    throw new AssistantUnavailableException("Dịch vụ AI tạm thời không khả dụng.");
            }
        }

        // Back to blocks in the form the next request needs; the assistant turn is replayed exactly as it was produced
        foreach (var block in blocks.Values)
        {
            var node = ToContentBlock(block);
            if (node is not null)
            {
                turn.Content.Add(node);
            }
        }
    }

    private static JsonObject? ToContentBlock(BlockBuilder block)
    {
        switch (block.Type)
        {
            case "text":
                return new JsonObject { ["type"] = "text", ["text"] = block.Text.ToString() };
            case "thinking":
                return new JsonObject { ["type"] = "thinking", ["thinking"] = block.Thinking.ToString(), ["signature"] = block.Signature ?? string.Empty };
            case "redacted_thinking":
                return new JsonObject { ["type"] = "redacted_thinking", ["data"] = block.Data ?? string.Empty };
            case "tool_use":
                var input = block.Json.Length == 0 ? new JsonObject() : JsonNode.Parse(block.Json.ToString());
                return new JsonObject { ["type"] = "tool_use", ["id"] = block.Id, ["name"] = block.Name, ["input"] = input };
            default:
                return null;
        }
    }

    private static JsonDocument? TryParse(string data)
    {
        if (data.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    // The final answer: all text blocks joined. A refusal or a cut-off before any text gets a friendly message.
    private string ExtractAnswer(JsonElement root, string? stopReason, string system)
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
            answer = stopReason == "refusal" ? RefusalAnswer : NoTextAnswer;
            _logger.LogWarning("The model returned no text (stop_reason {StopReason})", stopReason);
        }

        // Defence in depth: whatever the model was talked into, no secret and no copy of the rules leaves the server
        answer = _guard.Scrub(answer);
        if (_guard.LeaksSystemPrompt(answer, system))
        {
            _logger.LogWarning("The model repeated the system prompt; the answer was replaced");
            return LeakRefusalAnswer;
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
