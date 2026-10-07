using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

public class AskRequestDto
{
    // The length limit is enforced by the chat service from configuration; this only rejects a missing question early
    [Required]
    public string Question { get; set; } = string.Empty;
}

public class AskResponseDto
{
    public string Answer { get; set; } = string.Empty;

    // "haiku", "sonnet" or "opus"; the concrete model id stays on the server
    public string ModelTier { get; set; } = string.Empty;

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }
}
