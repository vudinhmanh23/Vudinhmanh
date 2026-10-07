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

// AI sales assistant. The LLM is called from the server only: the API key lives in server configuration and no
// client (Blazor or browser) ever sees it or talks to the provider directly.
[ApiController]
[Route("api/assistant")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.BanHang},{AppRoles.Kho}")]
public class AssistantController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IDocumentIngestionService _ingestion;
    private readonly ILogger<AssistantController> _logger;

    public AssistantController(IChatService chatService, IDocumentIngestionService ingestion, ILogger<AssistantController> logger)
    {
        _chatService = chatService;
        _ingestion = ingestion;
        _logger = logger;
    }

    /// <summary>
    /// Sends a question to the AI assistant and returns its answer. The question must not be empty and is limited
    /// to 2000 characters (AiSafety:MaxQuestionLength; a longer one answers 400); returns 503 when the AI provider is not configured or unavailable.
    /// <c>sources</c> lists the knowledge documents the answer is based on and <c>retrieval</c> the cosine
    /// similarity (0..1) of every passage given to the model, for checking retrieval quality.
    /// </summary>
    [HttpPost("ask")]
    [EnableRateLimiting(RateLimitingExtensions.AssistantPolicy)]
    public async Task<ActionResult<AskResponseDto>> Ask(AskRequestDto request, CancellationToken cancellationToken)
    {
        var answer = await _chatService.AskAsync(request.Question, CurrentCaller(), cancellationToken);
        return Ok(ToResponse(answer));
    }

    /// <summary>
    /// Same as <c>ask</c>, but the answer is streamed as Server-Sent Events (<c>text/event-stream</c>) so text appears while
    /// it is written. Events: <c>start</c> (sources + retrieval scores), <c>delta</c> (<c>{"text": "..."}</c>, many),
    /// <c>done</c> (the full answer, as in <c>ask</c>) and <c>error</c> (the stream failed after it began).
    /// Invalid input, missing configuration and rate limits are still answered with a normal 400, 503 or 429 before any event.
    /// </summary>
    [HttpPost("ask/stream")]
    [EnableRateLimiting(RateLimitingExtensions.AssistantPolicy)]
    [Produces("text/event-stream")]
    public async Task AskStream(AskRequestDto request, CancellationToken cancellationToken)
    {
        await using var events = _chatService.AskStreamAsync(request.Question, CurrentCaller(), cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);

        // The first event comes after the input checks, so those errors still become regular ProblemDetails responses
        if (!await events.MoveNextAsync())
        {
            return;
        }

        SseResponse.Begin(Response);

        try
        {
            do
            {
                await WriteEventAsync(events.Current, cancellationToken);
            }
            while (await events.MoveNextAsync());
        }
        catch (OperationCanceledException)
        {
            // The client went away; nothing left to send
        }
        catch (Exception ex) when (ex is AssistantUnavailableException or HttpRequestException or IOException)
        {
            // The status line is already sent, so a failure is reported as an event; the text is user-safe
            _logger.LogError(ex, "The assistant stream failed");
            await SseResponse.WriteAsync(Response, "error", new AskStreamErrorDto { Message = ex is AssistantUnavailableException ? ex.Message : "Dịch vụ AI tạm thời không khả dụng." }, CancellationToken.None);
        }
    }

    /// <summary>
    /// Admin only. Reads the .md/.txt documents in the Knowledge folder, cuts them into chunks, embeds them and stores
    /// them for the assistant; running it again replaces the chunks of each document. Returns 503 when the embeddings
    /// provider is not configured or unavailable.
    /// </summary>
    [HttpPost("knowledge/ingest")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<IngestionResult>> Ingest(CancellationToken cancellationToken)
    {
        return Ok(await _ingestion.IngestAsync(cancellationToken));
    }

    private ChatCaller CurrentCaller() => new(User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(), User.FindFirstValue(ClaimTypes.NameIdentifier));

    private static AskResponseDto ToResponse(ChatAnswer answer) => new()
    {
        Answer = answer.Answer,
        ModelTier = answer.ModelTier,
        InputTokens = answer.InputTokens,
        OutputTokens = answer.OutputTokens,
        ToolsUsed = answer.ToolsUsed.ToList(),
        Sources = (answer.Sources ?? Array.Empty<string>()).ToList(),
        Retrieval = (answer.Retrieval ?? Array.Empty<RetrievedChunk>()).Select(ToDto).ToList()
    };

    private static RetrievedChunkDto ToDto(RetrievedChunk chunk) => new() { SourceTitle = chunk.SourceTitle, Score = chunk.Score };

    private Task WriteEventAsync(ChatStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        return streamEvent switch
        {
            ChatStreamStart start => SseResponse.WriteAsync(Response, "start", new AskStreamStartDto
            {
                Sources = start.Sources.ToList(),
                Retrieval = start.Retrieval.Select(ToDto).ToList()
            }, cancellationToken),
            ChatStreamDelta delta => SseResponse.WriteAsync(Response, "delta", new AskStreamDeltaDto { Text = delta.Text }, cancellationToken),
            ChatStreamDone done => SseResponse.WriteAsync(Response, "done", ToResponse(done.Answer), cancellationToken),
            _ => Task.CompletedTask
        };
    }
}
