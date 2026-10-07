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

    // Names of the tools the assistant used to look up real data for this answer (empty when none)
    public List<string> ToolsUsed { get; set; } = new();

    // Titles of the knowledge documents (policies, product guides) the answer is based on (empty when none were relevant)
    public List<string> Sources { get; set; } = new();

    // One entry per knowledge passage given to the model, best first, with its cosine similarity (0..1) to the question
    public List<RetrievedChunkDto> Retrieval { get; set; } = new();
}

public class RetrievedChunkDto
{
    public string SourceTitle { get; set; } = string.Empty;

    public double Score { get; set; }
}

// Server-Sent Events of POST /api/assistant/ask/stream. "start": sources + retrieval; "delta": a piece of the answer;
// "done": the full AskResponseDto; "error": the stream failed after it began.
public class AskStreamStartDto
{
    public List<string> Sources { get; set; } = new();

    public List<RetrievedChunkDto> Retrieval { get; set; } = new();
}

public class AskStreamDeltaDto
{
    public string Text { get; set; } = string.Empty;
}

public class AskStreamErrorDto
{
    public string Message { get; set; } = string.Empty;
}
