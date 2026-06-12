using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIFramework.Core.Llm.Providers;

/// <summary>
/// Adapter for any endpoint that speaks the OpenAI Chat Completions wire format:
/// OpenAI itself, Ollama (http://localhost:11434/v1), vLLM, Groq, Mistral, and others.
/// Implemented with raw HTTP on purpose — it shows exactly what goes over the wire.
/// </summary>
public sealed class OpenAICompatibleProvider : ILlmProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly Uri _chatCompletionsUri;

    /// <param name="name">Identifier for logs/usage reports, e.g. "openai" or "ollama".</param>
    /// <param name="baseUrl">API root, e.g. "https://api.openai.com/v1" or "http://localhost:11434/v1".</param>
    /// <param name="apiKey">Bearer token; null for local servers that don't need one.</param>
    public OpenAICompatibleProvider(HttpClient http, string name, string baseUrl, string defaultModel, string? apiKey = null)
    {
        _http = http;
        Name = name;
        DefaultModel = defaultModel;
        _chatCompletionsUri = new Uri(baseUrl.TrimEnd('/') + "/chat/completions");
        if (apiKey is not null)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public string Name { get; }
    public string DefaultModel { get; }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var body = BuildBody(request, stream: false);
        using var httpResponse = await PostAsync(body, cancellationToken);
        var json = await JsonDocument.ParseAsync(
            await httpResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        var choice = json.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        var toolCalls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var rawToolCalls) && rawToolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var rawCall in rawToolCalls.EnumerateArray())
            {
                var function = rawCall.GetProperty("function");
                // The wire format carries arguments as a JSON *string* — parse to an object.
                var arguments = JsonDocument.Parse(function.GetProperty("arguments").GetString() ?? "{}");
                toolCalls.Add(new ToolCall(
                    rawCall.GetProperty("id").GetString()!,
                    function.GetProperty("name").GetString()!,
                    arguments.RootElement.Clone()));
            }
        }

        var usage = json.RootElement.TryGetProperty("usage", out var rawUsage)
            ? new TokenUsage(
                rawUsage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt64() : 0,
                rawUsage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt64() : 0)
            : TokenUsage.Zero;

        return new ChatResponse
        {
            Text = message.TryGetProperty("content", out var content) ? content.GetString() : null,
            ToolCalls = toolCalls,
            FinishReason = MapFinishReason(choice.GetProperty("finish_reason").GetString()),
            Usage = usage,
            Model = json.RootElement.TryGetProperty("model", out var model)
                ? model.GetString() ?? DefaultModel
                : DefaultModel,
        };
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildBody(request, stream: true);
        using var httpResponse = await PostAsync(body, cancellationToken);
        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var text = new StringBuilder();
        var usage = TokenUsage.Zero;
        var finishReason = FinishReason.Stop;

        // The response is Server-Sent Events: lines of "data: {json}", terminated by "data: [DONE]".
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }
            var payload = line["data: ".Length..];
            if (payload == "[DONE]")
            {
                break;
            }

            using var chunk = JsonDocument.Parse(payload);
            var root = chunk.RootElement;

            if (root.TryGetProperty("usage", out var rawUsage) && rawUsage.ValueKind == JsonValueKind.Object)
            {
                usage = new TokenUsage(
                    rawUsage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt64() : 0,
                    rawUsage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt64() : 0);
            }

            if (root.GetProperty("choices").GetArrayLength() == 0)
            {
                continue; // final usage-only chunk on some servers
            }
            var choice = root.GetProperty("choices")[0];

            if (choice.TryGetProperty("finish_reason", out var rawFinish) && rawFinish.ValueKind == JsonValueKind.String)
            {
                finishReason = MapFinishReason(rawFinish.GetString());
            }

            if (choice.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var deltaContent) &&
                deltaContent.ValueKind == JsonValueKind.String &&
                deltaContent.GetString() is { Length: > 0 } deltaText)
            {
                text.Append(deltaText);
                yield return new StreamEvent.TextDelta(deltaText);
            }
        }

        yield return new StreamEvent.Completed(new ChatResponse
        {
            Text = text.ToString(),
            FinishReason = finishReason,
            Usage = usage,
            Model = request.Model ?? DefaultModel,
        });
    }

    private JsonObject BuildBody(ChatRequest request, bool stream)
    {
        var messages = new JsonArray();
        if (request.SystemPrompt is not null)
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt });
        }

        foreach (var message in request.Messages)
        {
            var entry = new JsonObject
            {
                ["role"] = message.Role switch
                {
                    ChatRole.User => "user",
                    ChatRole.Assistant => "assistant",
                    ChatRole.Tool => "tool",
                    _ => throw new ArgumentOutOfRangeException(nameof(request)),
                },
                ["content"] = message.Content,
            };
            if (message.Role == ChatRole.Tool)
            {
                entry["tool_call_id"] = message.ToolCallId;
            }
            if (message.ToolCalls is { Count: > 0 })
            {
                var calls = new JsonArray();
                foreach (var call in message.ToolCalls)
                {
                    calls.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = call.Arguments.GetRawText(),
                        },
                    });
                }
                entry["tool_calls"] = calls;
            }
            messages.Add(entry);
        }

        var body = new JsonObject
        {
            ["model"] = request.Model ?? DefaultModel,
            ["max_tokens"] = request.MaxTokens,
            ["messages"] = messages,
        };
        if (stream)
        {
            body["stream"] = true;
            // Ask OpenAI-compatible servers to include token usage in the final chunk.
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        if (request.Tools is { Count: > 0 })
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.ParametersSchema.GetRawText()),
                    },
                });
            }
            body["tools"] = tools;
        }

        return body;
    }

    private async Task<HttpResponseMessage> PostAsync(JsonObject body, CancellationToken cancellationToken)
    {
        var content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(_chatCompletionsUri, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw new HttpRequestException($"{Name} request failed ({(int)response.StatusCode}): {error}");
        }
        return response;
    }

    private static FinishReason MapFinishReason(string? finishReason) => finishReason switch
    {
        "stop" => FinishReason.Stop,
        "tool_calls" => FinishReason.ToolCalls,
        "length" => FinishReason.MaxTokens,
        "content_filter" => FinishReason.Refusal,
        null => FinishReason.Stop,
        _ => FinishReason.Other,
    };
}
