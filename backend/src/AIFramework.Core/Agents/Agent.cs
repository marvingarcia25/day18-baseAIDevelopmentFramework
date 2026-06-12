using AIFramework.Core.Llm;

namespace AIFramework.Core.Agents;

public sealed record AgentOptions
{
    public string SystemPrompt { get; init; } =
        "You are a helpful assistant. Use the available tools when they would improve your answer, " +
        "and answer directly when they would not.";

    /// <summary>Safety valve: maximum model round-trips before the agent gives up.</summary>
    public int MaxIterations { get; init; } = 10;

    public string? Model { get; init; }
    public int MaxTokens { get; init; } = 4096;
}

/// <summary>One observable step of an agent run, for logs and UIs.</summary>
public abstract record AgentStep
{
    /// <summary>The model asked for a tool; we ran it.</summary>
    public sealed record ToolExecution(string ToolName, string Arguments, string Result) : AgentStep;

    /// <summary>The model produced its final text answer.</summary>
    public sealed record FinalAnswer(string Text) : AgentStep;
}

public sealed record AgentResult(string Answer, IReadOnlyList<AgentStep> Steps, TokenUsage TotalUsage);

/// <summary>
/// The core agent loop, written out explicitly so you can see exactly how agents work:
///
///   1. Send the conversation + tool definitions to the model.
///   2. If the model answers with text → done.
///   3. If the model requests tool calls → execute them, append the results, go to 1.
///
/// Everything else in agent frameworks (planning, multi-agent, memory) is layered on this loop.
/// </summary>
public sealed class Agent(ILlmProvider provider, ToolRegistry tools, AgentOptions? options = null)
{
    private readonly AgentOptions _options = options ?? new AgentOptions();

    public Task<AgentResult> RunAsync(string userMessage, CancellationToken cancellationToken = default) =>
        RunAsync([ChatMessage.User(userMessage)], cancellationToken);

    public async Task<AgentResult> RunAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        var messages = conversation.ToList();
        var steps = new List<AgentStep>();
        var totalUsage = TokenUsage.Zero;
        var toolDefinitions = tools.ToDefinitions();

        for (var iteration = 0; iteration < _options.MaxIterations; iteration++)
        {
            var response = await provider.CompleteAsync(new ChatRequest
            {
                Model = _options.Model,
                SystemPrompt = _options.SystemPrompt,
                Messages = messages,
                Tools = toolDefinitions,
                MaxTokens = _options.MaxTokens,
            }, cancellationToken);

            totalUsage += response.Usage;

            if (response.FinishReason != FinishReason.ToolCalls)
            {
                var answer = response.Text ?? "";
                steps.Add(new AgentStep.FinalAnswer(answer));
                return new AgentResult(answer, steps, totalUsage);
            }

            // Echo the assistant turn (including its tool calls) before appending results —
            // providers reject tool results that don't follow a matching tool call.
            messages.Add(new ChatMessage(ChatRole.Assistant, response.Text, response.ToolCalls));

            foreach (var call in response.ToolCalls)
            {
                var result = await ExecuteToolAsync(call, cancellationToken);
                steps.Add(new AgentStep.ToolExecution(call.Name, call.Arguments.GetRawText(), result));
                messages.Add(ChatMessage.ToolResult(call.Id, result));
            }
        }

        var fallback = $"Stopped after {_options.MaxIterations} iterations without a final answer.";
        steps.Add(new AgentStep.FinalAnswer(fallback));
        return new AgentResult(fallback, steps, totalUsage);
    }

    private async Task<string> ExecuteToolAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var tool = tools.Find(call.Name);
        if (tool is null)
        {
            return $"Error: unknown tool '{call.Name}'.";
        }
        try
        {
            return await tool.ExecuteAsync(call.Arguments, cancellationToken);
        }
        catch (Exception exception)
        {
            // Feed failures back as text so the model can recover (retry, different tool, ask the user).
            return $"Error executing {call.Name}: {exception.Message}";
        }
    }
}
