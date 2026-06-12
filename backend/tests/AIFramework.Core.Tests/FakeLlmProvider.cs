using AIFramework.Core.Llm;

namespace AIFramework.Core.Tests;

/// <summary>
/// A scripted ILlmProvider for tests: returns canned responses in order.
/// This is the payoff of the provider abstraction — agents, RAG, and evals
/// are all testable offline, deterministically, for free.
/// </summary>
public sealed class FakeLlmProvider(params ChatResponse[] responses) : ILlmProvider
{
    private int _next;

    public string Name => "fake";
    public string DefaultModel => "fake-model";
    public List<ChatRequest> Requests { get; } = [];

    public static ChatResponse TextResponse(string text) => new()
    {
        Text = text,
        FinishReason = FinishReason.Stop,
        Usage = new TokenUsage(10, 5),
        Model = "fake-model",
    };

    public static ChatResponse ToolCallResponse(params ToolCall[] calls) => new()
    {
        ToolCalls = calls,
        FinishReason = FinishReason.ToolCalls,
        Usage = new TokenUsage(10, 5),
        Model = "fake-model",
    };

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (_next >= responses.Length)
        {
            throw new InvalidOperationException("FakeLlmProvider ran out of scripted responses.");
        }
        return Task.FromResult(responses[_next++]);
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await CompleteAsync(request, cancellationToken);
        foreach (var chunk in (response.Text ?? "").Chunk(4))
        {
            yield return new StreamEvent.TextDelta(new string(chunk));
        }
        yield return new StreamEvent.Completed(response);
    }
}
