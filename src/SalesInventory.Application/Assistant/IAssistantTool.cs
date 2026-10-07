using System.Text.Json;

namespace SalesInventory.Application.Assistant;

// What a tool hands back to the model: JSON text (or a short error message) taken from real data
public record ToolOutcome(string Content, bool IsError = false);

// A function the AI assistant may call. Provider-neutral: the provider adapter turns Name/Description/InputSchemaJson
// into its own tool format and feeds ExecuteAsync the model-written arguments, which must be treated as untrusted input.
public interface IAssistantTool
{
    string Name { get; }

    string Description { get; }

    // JSON Schema of the arguments, as JSON text
    string InputSchemaJson { get; }

    // Roles that may use the tool; the assistant offers it to the model only to these roles and re-checks on execution
    IReadOnlyCollection<string> AllowedRoles { get; }

    Task<ToolOutcome> ExecuteAsync(JsonElement input, CancellationToken cancellationToken);
}
