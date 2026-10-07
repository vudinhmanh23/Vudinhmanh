namespace SalesInventory.Application.Interfaces;

// Who is asking: the roles decide which tools the assistant may use; the user id (an opaque Identity id, never an e-mail or a
// name) is only used to attribute the cost of a request in the usage log
public record ChatCaller(IReadOnlyCollection<string> Roles, string? UserId = null);

// A knowledge passage given to the model and its cosine similarity to the question (for checking retrieval quality)
public record RetrievedChunk(string SourceTitle, double Score);

// The answer of the assistant, the tools it consulted and the tokens the call used (for cost tracking);
// the model id is deliberately not exposed. Sources = titles of the knowledge documents given to the model for this answer;
// Retrieval = one entry per passage (best first) with its similarity score.
public record ChatAnswer(string Answer, string ModelTier, int InputTokens, int OutputTokens, IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<string>? Sources = null, IReadOnlyList<RetrievedChunk>? Retrieval = null);

// One step of a streamed answer: Start (once, with the retrieved sources), Delta (a piece of text, many times), Done (once, with the totals)
public abstract record ChatStreamEvent;

public sealed record ChatStreamStart(IReadOnlyList<string> Sources, IReadOnlyList<RetrievedChunk> Retrieval) : ChatStreamEvent;

public sealed record ChatStreamDelta(string Text) : ChatStreamEvent;

public sealed record ChatStreamDone(ChatAnswer Answer) : ChatStreamEvent;

// Sends one question to an LLM provider (which may call the assistant's tools to read real data) and returns its answer.
// Implementations run on the server only, so API keys never reach a client
public interface IChatService
{
    // Throws ChatInputException for an empty or too long question and AssistantUnavailableException when the provider cannot be used
    Task<ChatAnswer> AskAsync(string question, ChatCaller caller, CancellationToken cancellationToken = default);

    // Same as AskAsync, but the answer arrives piece by piece. Input and configuration errors are thrown when the first event is
    // requested (before any text exists); a provider failure later in the stream is thrown from a later MoveNext.
    // `history` = earlier turns of the same conversation (oldest first), sent before the question.
    IAsyncEnumerable<ChatStreamEvent> AskStreamAsync(string question, ChatCaller caller, IReadOnlyList<ChatTurn>? history = null, CancellationToken cancellationToken = default);
}
