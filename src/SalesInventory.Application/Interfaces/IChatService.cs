namespace SalesInventory.Application.Interfaces;

// The answer of the assistant plus the tokens the call used (for cost tracking); the model id is deliberately not exposed
public record ChatAnswer(string Answer, string ModelTier, int InputTokens, int OutputTokens);

// Sends one question to an LLM provider and returns its answer. Implementations run on the server only, so API keys never reach a client
public interface IChatService
{
    // Throws ChatInputException for an empty or too long question and AssistantUnavailableException when the provider cannot be used
    Task<ChatAnswer> AskAsync(string question, CancellationToken cancellationToken = default);
}
