using System.Text.Json;

namespace AIFramework.Core.Agents.Tools;

/// <summary>
/// Returns the current date and time. Models have a training cutoff and no clock,
/// so any "today"/"now" question needs a tool like this.
/// </summary>
public sealed class ClockTool(TimeProvider? timeProvider = null) : ITool
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public string Name => "get_current_time";

    public string Description =>
        "Get the current date and time in UTC. Call this whenever the answer depends on " +
        "today's date or the current time.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
    });

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(_time.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss 'UTC' (dddd)"));
}
