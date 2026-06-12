using System.Diagnostics;
using System.Runtime.CompilerServices;
using AIFramework.Core.Llm;

namespace AIFramework.Core.Observability;

/// <summary>
/// Decorator that adds usage tracking to any <see cref="ILlmProvider"/> without the provider
/// (or the code calling it) knowing. Wrap once at composition time:
/// <code>ILlmProvider provider = new InstrumentedLlmProvider(inner, tracker);</code>
/// </summary>
public sealed class InstrumentedLlmProvider(ILlmProvider inner, UsageTracker tracker) : ILlmProvider
{
    public string Name => inner.Name;
    public string DefaultModel => inner.DefaultModel;

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await inner.CompleteAsync(request, cancellationToken);
        tracker.Record(Name, response.Model, response.Usage, stopwatch.Elapsed.TotalMilliseconds, streamed: false);
        return response;
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await foreach (var streamEvent in inner.StreamAsync(request, cancellationToken))
        {
            if (streamEvent is StreamEvent.Completed completed)
            {
                tracker.Record(Name, completed.Response.Model, completed.Response.Usage,
                    stopwatch.Elapsed.TotalMilliseconds, streamed: true);
            }
            yield return streamEvent;
        }
    }
}
