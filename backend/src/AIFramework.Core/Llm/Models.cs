using System.Text.Json;

namespace AIFramework.Core.Llm;

/// <summary>Who authored a message. "Tool" carries the result of a tool call back to the model.</summary>
public enum ChatRole
{
    User,
    Assistant,
    Tool,
}

/// <summary>
/// A provider-agnostic chat message. Providers translate this to their own wire format.
/// - A plain user/assistant message has only <see cref="Content"/>.
/// - An assistant message that requested tools has <see cref="ToolCalls"/>.
/// - A tool result message has Role=Tool, <see cref="ToolCallId"/> and <see cref="Content"/> (the result).
/// </summary>
public sealed record ChatMessage(
    ChatRole Role,
    string? Content,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    public static ChatMessage User(string content) => new(ChatRole.User, content);
    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);
    public static ChatMessage ToolResult(string toolCallId, string result) =>
        new(ChatRole.Tool, result, ToolCallId: toolCallId);
}

/// <summary>The model asking us to run a tool. Arguments is the raw JSON object the model produced.</summary>
public sealed record ToolCall(string Id, string Name, JsonElement Arguments);

/// <summary>
/// A tool the model is allowed to call. <see cref="ParametersSchema"/> is a JSON Schema object,
/// e.g. {"type":"object","properties":{...},"required":[...]}.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, JsonElement ParametersSchema);

/// <summary>A provider-agnostic chat completion request.</summary>
public sealed record ChatRequest
{
    /// <summary>Provider-specific model id. Null means the provider's configured default.</summary>
    public string? Model { get; init; }

    /// <summary>System prompt, kept separate from messages because providers place it differently.</summary>
    public string? SystemPrompt { get; init; }

    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    public IReadOnlyList<ToolDefinition>? Tools { get; init; }

    public int MaxTokens { get; init; } = 4096;
}

/// <summary>Why the model stopped generating.</summary>
public enum FinishReason
{
    /// <summary>Natural end of the response.</summary>
    Stop,
    /// <summary>The model wants one or more tools executed; see <see cref="ChatResponse.ToolCalls"/>.</summary>
    ToolCalls,
    /// <summary>Hit the MaxTokens limit — the response is truncated.</summary>
    MaxTokens,
    /// <summary>The provider declined to answer (safety refusal).</summary>
    Refusal,
    Other,
}

public sealed record TokenUsage(long InputTokens, long OutputTokens)
{
    public static readonly TokenUsage Zero = new(0, 0);
    public static TokenUsage operator +(TokenUsage a, TokenUsage b) =>
        new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens);
}

/// <summary>A provider-agnostic chat completion response.</summary>
public sealed record ChatResponse
{
    public string? Text { get; init; }
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];
    public required FinishReason FinishReason { get; init; }
    public required TokenUsage Usage { get; init; }
    /// <summary>The model that actually served the request.</summary>
    public required string Model { get; init; }
}

/// <summary>Events emitted while streaming a response.</summary>
public abstract record StreamEvent
{
    /// <summary>A chunk of assistant text.</summary>
    public sealed record TextDelta(string Text) : StreamEvent;

    /// <summary>Terminal event carrying the assembled response (text, usage, finish reason).</summary>
    public sealed record Completed(ChatResponse Response) : StreamEvent;
}
