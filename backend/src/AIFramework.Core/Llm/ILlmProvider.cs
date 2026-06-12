namespace AIFramework.Core.Llm;

/// <summary>
/// The single seam between this framework and any LLM vendor.
/// Everything above this interface (agents, RAG, evals, the API) is provider-agnostic;
/// everything below it (one class per vendor) is an adapter.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Short identifier used in logs and usage reports, e.g. "anthropic".</summary>
    string Name { get; }

    /// <summary>Model used when <see cref="ChatRequest.Model"/> is null.</summary>
    string DefaultModel { get; }

    /// <summary>Run a single completion and return the full response.</summary>
    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream a completion as text deltas, ending with a <see cref="StreamEvent.Completed"/>
    /// event that carries the assembled response and token usage.
    /// </summary>
    IAsyncEnumerable<StreamEvent> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
