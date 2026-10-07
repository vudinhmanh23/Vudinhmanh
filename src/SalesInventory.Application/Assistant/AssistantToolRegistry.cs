namespace SalesInventory.Application.Assistant;

// The set of tools the assistant knows, filtered by the caller's roles
public class AssistantToolRegistry
{
    private readonly IReadOnlyList<IAssistantTool> _tools;

    public AssistantToolRegistry(IEnumerable<IAssistantTool> tools)
    {
        _tools = tools.ToList();
    }

    // Tools this caller may use (any role match grants access)
    public IReadOnlyList<IAssistantTool> GetFor(IReadOnlyCollection<string> roles)
    {
        return _tools.Where(t => t.AllowedRoles.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase))).ToList();
    }

    // Same rule, by name; null when the tool does not exist or the roles do not allow it
    public IAssistantTool? Find(string name, IReadOnlyCollection<string> roles)
    {
        return GetFor(roles).FirstOrDefault(t => t.Name == name);
    }
}
