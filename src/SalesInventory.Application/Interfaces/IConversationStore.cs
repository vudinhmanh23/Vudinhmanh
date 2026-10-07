using SalesInventory.Application.Dtos;

namespace SalesInventory.Application.Interfaces;

// One earlier turn handed to the model as context; Role is "user" or "assistant"
public record ChatTurn(string Role, string Content);

// Keeps chat history per user so a conversationId carries the earlier turns forward and a page reload can show them again
public interface IConversationStore
{
    // The most recent turns (oldest first, at most AiSafety:MaxHistoryMessages) to give the model as context; empty when the id is new.
    // Throws NotFoundException when the id belongs to another user, so a stranger's id reveals nothing.
    Task<IReadOnlyList<ChatTurn>> GetHistoryAsync(Guid conversationId, string userId, CancellationToken cancellationToken = default);

    // Saves the user's message, creating the conversation on its first one. Called BEFORE the model is asked, so the question
    // survives even when the answer fails.
    Task AppendUserAsync(Guid conversationId, string userId, string message, CancellationToken cancellationToken = default);

    // Saves the assistant's finished answer. Called only after the stream completed, so a failed or cancelled answer leaves no half text.
    Task AppendAssistantAsync(Guid conversationId, string userId, string answer, CancellationToken cancellationToken = default);

    // Every message of the conversation in time order. Throws NotFoundException when it does not exist or is not the user's.
    Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(Guid conversationId, string userId, CancellationToken cancellationToken = default);

    // The user's conversations, most recently active first
    Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(string userId, int take, CancellationToken cancellationToken = default);
}
