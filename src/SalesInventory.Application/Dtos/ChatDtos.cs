using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

public class ChatStreamRequestDto
{
    // The conversation to continue; omit (null) to start a new one. A client may also choose a new Guid itself.
    public Guid? ConversationId { get; set; }

    // The length limit is enforced from configuration (AiSafety:MaxQuestionLength); this only rejects a missing message early
    [Required]
    public string Message { get; set; } = string.Empty;
}

// Data of the "start" event of POST /api/chat/stream
public class ChatStreamStartDto
{
    public Guid ConversationId { get; set; }

    public List<string> Sources { get; set; } = new();

    public List<RetrievedChunkDto> Retrieval { get; set; } = new();
}

// Data of the "done" event of POST /api/chat/stream: the full answer as in /api/assistant/ask, plus the conversation it belongs to
public class ChatStreamDoneDto : AskResponseDto
{
    public Guid ConversationId { get; set; }
}

// One saved message of a conversation (GET /api/chat/{conversationId}); Role is "user" or "assistant"
public class ChatMessageDto
{
    public int Id { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

// One row of the history list (GET /api/chat)
public class ConversationSummaryDto
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }
}
