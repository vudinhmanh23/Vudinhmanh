namespace SalesInventory.Application.Interfaces;

// Who is asking: decides which tools the assistant may use for this question
public record ChatCaller(IReadOnlyCollection<string> Roles);

// The answer of the assistant, the tools it consulted and the tokens the call used (for cost tracking);
// the model id is deliberately not exposed
public record ChatAnswer(string Answer, string ModelTier, int InputTokens, int OutputTokens, IReadOnlyList<string> ToolsUsed);

// Sends one question to an LLM provider (which may call the assistant's tools to read real data) and returns its answer.
// Implementations run on the server only, so API keys never reach a client
public interface IChatService
{
    // Throws ChatInputException for an empty or too long question and AssistantUnavailableException when the provider cannot be used
    Task<ChatAnswer> AskAsync(string question, ChatCaller caller, CancellationToken cancellationToken = default);
}
