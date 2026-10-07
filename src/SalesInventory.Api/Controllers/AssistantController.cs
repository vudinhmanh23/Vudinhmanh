using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SalesInventory.Application.Dtos;
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

    public AssistantController(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// Sends a question to the AI assistant and returns its answer. The question must not be empty and is limited
    /// to 4000 characters (configurable); returns 503 when the AI provider is not configured or unavailable.
    /// </summary>
    [HttpPost("ask")]
    [EnableRateLimiting("assistant")]
    public async Task<ActionResult<AskResponseDto>> Ask(AskRequestDto request, CancellationToken cancellationToken)
    {
        var answer = await _chatService.AskAsync(request.Question, cancellationToken);
        return Ok(new AskResponseDto
        {
            Answer = answer.Answer,
            ModelTier = answer.ModelTier,
            InputTokens = answer.InputTokens,
            OutputTokens = answer.OutputTokens
        });
    }
}
