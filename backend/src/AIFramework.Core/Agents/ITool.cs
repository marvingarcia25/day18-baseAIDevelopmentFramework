using System.Text.Json;

namespace AIFramework.Core.Agents;

/// <summary>
/// A capability the agent can invoke. Implementations should:
/// - validate their inputs (the model can produce anything),
/// - return errors as readable text instead of throwing (the model can then self-correct),
/// - describe in <see cref="Description"/> *when* to call the tool, not just what it does.
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }

    /// <summary>JSON Schema for the tool's arguments.</summary>
    JsonElement ParametersSchema { get; }

    /// <summary>Execute the tool and return a result the model will read.</summary>
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}
