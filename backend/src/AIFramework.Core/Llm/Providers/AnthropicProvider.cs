using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace AIFramework.Core.Llm.Providers;

/// <summary>
/// Adapter for the Anthropic (Claude) API, built on the official Anthropic .NET SDK.
/// </summary>
public sealed class AnthropicProvider : ILlmProvider
{
    private readonly AnthropicClient _client;

    public AnthropicProvider(string? apiKey = null, string defaultModel = "claude-opus-4-8")
    {
        // The SDK falls back to the ANTHROPIC_API_KEY environment variable when ApiKey is null.
        _client = apiKey is null ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey };
        DefaultModel = defaultModel;
    }

    public string Name => "anthropic";
    public string DefaultModel { get; }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var parameters = BuildParams(request);
        var response = await _client.Messages.Create(parameters, cancellationToken: cancellationToken);

        string? text = null;
        var toolCalls = new List<ToolCall>();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? textBlock))
            {
                text = text is null ? textBlock.Text : text + textBlock.Text;
            }
            else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
            {
                toolCalls.Add(new ToolCall(
                    toolUse.ID,
                    toolUse.Name,
                    JsonSerializer.SerializeToElement(toolUse.Input)));
            }
        }

        return new ChatResponse
        {
            Text = text,
            ToolCalls = toolCalls,
            FinishReason = MapStopReason(response.StopReason?.ToString()),
            Usage = new TokenUsage(response.Usage.InputTokens, response.Usage.OutputTokens),
            Model = response.Model.ToString() ?? parameters.Model.ToString() ?? DefaultModel,
        };
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var parameters = BuildParams(request);

        var text = new System.Text.StringBuilder();
        long inputTokens = 0, outputTokens = 0;
        string? stopReason = null;

        await foreach (var streamEvent in _client.Messages.CreateStreaming(parameters, cancellationToken: cancellationToken))
        {
            if (streamEvent.TryPickContentBlockDelta(out var blockDelta) &&
                blockDelta.Delta.TryPickText(out var textDelta))
            {
                text.Append(textDelta.Text);
                yield return new StreamEvent.TextDelta(textDelta.Text);
            }
            else if (streamEvent.TryPickStart(out var start))
            {
                inputTokens = start.Message.Usage.InputTokens;
            }
            else if (streamEvent.TryPickDelta(out var messageDelta))
            {
                outputTokens = messageDelta.Usage.OutputTokens;
                stopReason = messageDelta.Delta.StopReason?.ToString();
            }
        }

        yield return new StreamEvent.Completed(new ChatResponse
        {
            Text = text.ToString(),
            FinishReason = MapStopReason(stopReason),
            Usage = new TokenUsage(inputTokens, outputTokens),
            Model = request.Model ?? DefaultModel,
        });
    }

    private MessageCreateParams BuildParams(ChatRequest request)
    {
        var parameters = new MessageCreateParams
        {
            Model = request.Model ?? DefaultModel,
            MaxTokens = request.MaxTokens,
            Messages = ConvertMessages(request.Messages),
        };

        if (request.SystemPrompt is not null)
        {
            parameters = parameters with { System = request.SystemPrompt };
        }

        if (request.Tools is { Count: > 0 } tools)
        {
            parameters = parameters with { Tools = tools.Select(t => (ToolUnion)ConvertTool(t)).ToList() };
        }
        else
        {
            // Adaptive thinking improves quality on hard prompts. We only enable it for
            // tool-free requests: with tools, thinking blocks would have to be echoed back
            // verbatim on every turn, which our provider-agnostic ChatMessage doesn't carry.
            parameters = parameters with { Thinking = new ThinkingConfigAdaptive() };
        }

        return parameters;
    }

    private static List<MessageParam> ConvertMessages(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<MessageParam>();
        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case ChatRole.User:
                    result.Add(new MessageParam { Role = Role.User, Content = message.Content ?? "" });
                    break;

                case ChatRole.Assistant when message.ToolCalls is { Count: > 0 }:
                    List<ContentBlockParam> assistantBlocks = [];
                    if (!string.IsNullOrEmpty(message.Content))
                    {
                        assistantBlocks.Add(new TextBlockParam { Text = message.Content });
                    }
                    foreach (var call in message.ToolCalls)
                    {
                        assistantBlocks.Add(new ToolUseBlockParam
                        {
                            ID = call.Id,
                            Name = call.Name,
                            Input = ToInputDictionary(call.Arguments),
                        });
                    }
                    result.Add(new MessageParam { Role = Role.Assistant, Content = assistantBlocks });
                    break;

                case ChatRole.Assistant:
                    result.Add(new MessageParam { Role = Role.Assistant, Content = message.Content ?? "" });
                    break;

                case ChatRole.Tool:
                    // Tool results travel in a user-role message on the Anthropic API.
                    result.Add(new MessageParam
                    {
                        Role = Role.User,
                        Content = new List<ContentBlockParam>
                        {
                            new ToolResultBlockParam
                            {
                                ToolUseID = message.ToolCallId
                                    ?? throw new InvalidOperationException("Tool message requires ToolCallId."),
                                Content = message.Content ?? "",
                            },
                        },
                    });
                    break;
            }
        }
        return result;
    }

    private static Tool ConvertTool(ToolDefinition definition)
    {
        var properties = new Dictionary<string, JsonElement>();
        List<string> required = [];

        if (definition.ParametersSchema.TryGetProperty("properties", out var props))
        {
            foreach (var property in props.EnumerateObject())
            {
                properties[property.Name] = property.Value.Clone();
            }
        }
        if (definition.ParametersSchema.TryGetProperty("required", out var req))
        {
            required = req.EnumerateArray().Select(e => e.GetString()!).ToList();
        }

        return new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = new() { Properties = properties, Required = required },
        };
    }

    private static Dictionary<string, JsonElement> ToInputDictionary(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object
            ? arguments.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : [];

    private static FinishReason MapStopReason(string? stopReason) => stopReason switch
    {
        "end_turn" or "stop_sequence" => FinishReason.Stop,
        "tool_use" => FinishReason.ToolCalls,
        "max_tokens" => FinishReason.MaxTokens,
        "refusal" => FinishReason.Refusal,
        null => FinishReason.Stop,
        _ => FinishReason.Other,
    };
}
