using AIFramework.Core.Llm;

namespace AIFramework.Core.Agents;

/// <summary>Holds the tools available to an agent and converts them to provider definitions.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    public ToolRegistry(params ITool[] tools)
    {
        foreach (var tool in tools)
        {
            Add(tool);
        }
    }

    public void Add(ITool tool)
    {
        if (!_tools.TryAdd(tool.Name, tool))
        {
            throw new ArgumentException($"A tool named '{tool.Name}' is already registered.");
        }
    }

    public ITool? Find(string name) => _tools.GetValueOrDefault(name);

    public IReadOnlyList<ToolDefinition> ToDefinitions() =>
        _tools.Values.Select(t => new ToolDefinition(t.Name, t.Description, t.ParametersSchema)).ToList();
}
