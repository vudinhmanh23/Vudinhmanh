namespace SalesInventory.Web.Models;

/// <summary>A knowledge passage the answer was based on, with its cosine similarity (0..1) to the question.</summary>
public record AssistantChunk(string SourceTitle, double Score);

public enum AssistantEventKind
{
    /// <summary>Sources and scores found for the question (comes first).</summary>
    Start,

    /// <summary>A piece of the answer text.</summary>
    Delta,

    /// <summary>The answer is complete; carries the final text and the token totals.</summary>
    Done,

    /// <summary>The stream failed after it began.</summary>
    Error
}

/// <summary>One Server-Sent Event of POST /api/chat/stream; only the fields of its kind are filled.</summary>
public class AssistantStreamEvent
{
    public AssistantEventKind Kind { get; init; }

    /// <summary>Delta: the new text. Done: the full answer. Error: the message.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Start and Done: the conversation this exchange belongs to (new on the first message).</summary>
    public Guid? ConversationId { get; init; }

    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    public IReadOnlyList<AssistantChunk> Retrieval { get; init; } = Array.Empty<AssistantChunk>();

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }
}

/// <summary>The API refused the question (empty, too long, rate limited, not configured); the message is safe to show.</summary>
public class AssistantRequestException : Exception
{
    public AssistantRequestException(string message) : base(message)
    {
    }
}

/// <summary>One saved message of a conversation; Role is "user" or "assistant".</summary>
public record ChatHistoryMessage(string Role, string Content, DateTime CreatedAt);

/// <summary>One row of the conversation list.</summary>
public record ConversationSummary(Guid Id, string Title, DateTime UpdatedAt);
