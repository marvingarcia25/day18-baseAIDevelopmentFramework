using System.Collections.Concurrent;
using AIFramework.Core.Llm;

namespace AIFramework.Core.Observability;

/// <summary>One completed LLM call.</summary>
public sealed record LlmCallRecord(
    DateTimeOffset Timestamp,
    string Provider,
    string Model,
    TokenUsage Usage,
    decimal EstimatedCostUsd,
    double DurationMs,
    bool Streamed);

/// <summary>Aggregated view over all recorded calls.</summary>
public sealed record UsageReport(
    int TotalCalls,
    TokenUsage TotalUsage,
    decimal TotalEstimatedCostUsd,
    IReadOnlyList<LlmCallRecord> Calls);

/// <summary>
/// Thread-safe, in-memory record of every LLM call: tokens, latency, and estimated cost.
/// Swap this for OpenTelemetry/your metrics pipeline in production — the interface stays the same.
/// </summary>
public sealed class UsageTracker
{
    private readonly ConcurrentQueue<LlmCallRecord> _calls = new();

    public void Record(string provider, string model, TokenUsage usage, double durationMs, bool streamed)
    {
        _calls.Enqueue(new LlmCallRecord(
            DateTimeOffset.UtcNow, provider, model, usage,
            ModelPricing.EstimateCostUsd(model, usage), durationMs, streamed));
    }

    public UsageReport GetReport()
    {
        var calls = _calls.ToList();
        var total = calls.Aggregate(TokenUsage.Zero, (acc, call) => acc + call.Usage);
        return new UsageReport(calls.Count, total, calls.Sum(c => c.EstimatedCostUsd), calls);
    }
}

/// <summary>
/// Approximate per-million-token prices for cost estimates in logs and dashboards.
/// Keep this current with your providers' pricing pages; unknown models report $0.
/// </summary>
public static class ModelPricing
{
    private sealed record Price(decimal InputPerMTok, decimal OutputPerMTok);

    private static readonly Dictionary<string, Price> Prices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-4-8"] = new(5.00m, 25.00m),
        ["claude-sonnet-4-6"] = new(3.00m, 15.00m),
        ["claude-haiku-4-5"] = new(1.00m, 5.00m),
        ["gpt-4o"] = new(2.50m, 10.00m),
        ["gpt-4o-mini"] = new(0.15m, 0.60m),
    };

    public static decimal EstimateCostUsd(string model, TokenUsage usage)
    {
        // Match on prefix so dated/full model ids (e.g. "claude-haiku-4-5-20251001") still resolve.
        var price = Prices.FirstOrDefault(p => model.StartsWith(p.Key, StringComparison.OrdinalIgnoreCase)).Value;
        if (price is null)
        {
            return 0m;
        }
        return (usage.InputTokens * price.InputPerMTok + usage.OutputTokens * price.OutputPerMTok) / 1_000_000m;
    }
}
