using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SalesInventory.Api.RateLimiting;
using SalesInventory.Api.Streaming;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Identity;

namespace SalesInventory.Api.Controllers;

// Chat with the assistant, with history. The model is called from the server only: the API key lives in server
// configuration (user-secrets / ANTHROPIC_API_KEY) and is never sent to, or logged for, any client.
[ApiController]
[Route("api/chat")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.BanHang},{AppRoles.Kho}")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IConversationStore _conversations;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatService chatService, IConversationStore conversations, ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _conversations = conversations;
        _logger = logger;
    }

    /// <summary>
    /// Sends a message and streams the answer as Server-Sent Events (<c>text/event-stream</c>): many <c>delta</c> events
    /// (<c>{"text": "..."}</c>) instead of one JSON body. Events: <c>start</c> (conversationId, sources, retrieval scores),
    /// <c>delta</c>, <c>done</c> (full answer, tokens, conversationId) and <c>error</c> (failure after the stream began).
    /// Omit <c>conversationId</c> to start a conversation (its id comes back in <c>start</c>); send it again to continue,
    /// and the earlier turns are given to the model as context. The user's message is saved before the model is called and
    /// the answer once the stream is complete. The message is limited to 2000 characters (AiSafety:MaxQuestionLength; a longer one answers 400), the answer to
    /// AiSafety:MaxTokens. A conversation of another user answers 404; bad input, a missing configuration and rate limits
    /// answer 400, 503 and 429 before any event.
    /// </summary>
    [HttpPost("stream")]
    [EnableRateLimiting(RateLimitingExtensions.AssistantPolicy)]
    [Produces("text/event-stream")]
    public async Task Stream(ChatStreamRequestDto request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var message = request.Message.Trim();
        var conversationId = request.ConversationId is { } id && id != Guid.Empty ? id : Guid.NewGuid();

        // Read the earlier turns first: the new message must not appear twice (once as history, once as the question)
        var history = await _conversations.GetHistoryAsync(conversationId, userId, cancellationToken);
        var caller = new ChatCaller(User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(), userId);

        await using var events = _chatService.AskStreamAsync(message, caller, history, cancellationToken).GetAsyncEnumerator(cancellationToken);

        // The first event comes after the input checks, so those errors still become regular ProblemDetails responses,
        // and an invalid message is never saved. The model has not been called yet at this point.
        if (!await events.MoveNextAsync())
        {
            return;
        }

        // Saved BEFORE the model is asked: the question is kept even if the answer fails
        await _conversations.AppendUserAsync(conversationId, userId, message, cancellationToken);

        SseResponse.Begin(Response);

        // What has been streamed so far, so a cancelled answer can still be kept
        var partial = new System.Text.StringBuilder();
        var answerSaved = false;

        try
        {
            do
            {
                switch (events.Current)
                {
                    case ChatStreamStart start:
                        await SseResponse.WriteAsync(Response, "start", new ChatStreamStartDto
                        {
                            ConversationId = conversationId,
                            Sources = start.Sources.ToList(),
                            Retrieval = start.Retrieval.Select(ToDto).ToList()
                        }, cancellationToken);
                        break;

                    case ChatStreamDelta delta:
                        partial.Append(delta.Text);
                        await SseResponse.WriteAsync(Response, "delta", new AskStreamDeltaDto { Text = delta.Text }, cancellationToken);
                        break;

                    case ChatStreamDone done:
                        // Saved once the stream is complete and before "done" goes out; a failed or cancelled stream saves no half answer
                        await _conversations.AppendAssistantAsync(conversationId, userId, done.Answer.Answer, CancellationToken.None);
                        answerSaved = true;
                        await SseResponse.WriteAsync(Response, "done", ToDone(conversationId, done.Answer), cancellationToken);
                        break;
                }
            }
            while (await events.MoveNextAsync());
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // The client pressed "cancel" or went away. The request token also stopped the call to the model, so no more
            // text is being paid for. Whatever was already streamed is kept as the (shorter) answer, unless it was saved.
            await SavePartialAnswerAsync(conversationId, userId, partial.ToString(), answerSaved);
        }
        catch (Exception ex)
        {
            // The status line is already sent, so a failure is reported as an event. Only a fixed text goes out: the
            // exception (which could mention internals) is logged, and it never contains the API key.
            _logger.LogError(ex, "The chat stream failed");
            var text = ex is AssistantUnavailableException ? ex.Message : "Có lỗi xảy ra, vui lòng thử lại.";
            await SseResponse.WriteAsync(Response, "error", new AskStreamErrorDto { Message = text }, CancellationToken.None);
        }
    }

    /// <summary>
    /// The messages of one of the caller's conversations in time order (oldest first), so a page can show the history
    /// again after a reload. Another user's or an unknown id answers 404.
    /// </summary>
    [HttpGet("{conversationId:guid}")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetMessages(Guid conversationId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        return Ok(await _conversations.GetMessagesAsync(conversationId, userId, cancellationToken));
    }

    /// <summary>The caller's conversations, most recently active first (at most 50), for a history list.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConversationSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        return Ok(await _conversations.ListAsync(userId, 50, cancellationToken));
    }

    // Keeps what was streamed before a cancel. Never throws: the client is already gone and a failure here must not become a 500.
    private async Task SavePartialAnswerAsync(Guid conversationId, string userId, string text, bool alreadySaved)
    {
        text = text.Trim();
        if (alreadySaved || text.Length == 0)
        {
            return;
        }

        try
        {
            await _conversations.AppendAssistantAsync(conversationId, userId, text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save the partial answer of a cancelled chat stream");
        }
    }

    private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static RetrievedChunkDto ToDto(RetrievedChunk chunk) => new() { SourceTitle = chunk.SourceTitle, Score = chunk.Score };

    private static ChatStreamDoneDto ToDone(Guid conversationId, ChatAnswer answer) => new()
    {
        ConversationId = conversationId,
        Answer = answer.Answer,
        ModelTier = answer.ModelTier,
        InputTokens = answer.InputTokens,
        OutputTokens = answer.OutputTokens,
        ToolsUsed = answer.ToolsUsed.ToList(),
        Sources = (answer.Sources ?? Array.Empty<string>()).ToList(),
        Retrieval = (answer.Retrieval ?? Array.Empty<RetrievedChunk>()).Select(ToDto).ToList()
    };
}
