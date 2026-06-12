using System.Text.Json;
using AIFramework.Core.Llm;

namespace AIFramework.Core.Evals;

public sealed record EvalCaseResult(
    EvalCase Case,
    string ActualOutput,
    GradeResult Grade,
    TokenUsage Usage,
    double DurationMs);

public sealed record EvalReport(
    string GraderName,
    int Total,
    int Passed,
    double AverageScore,
    TokenUsage TotalUsage,
    IReadOnlyList<EvalCaseResult> Results)
{
    public double PassRate => Total == 0 ? 0 : (double)Passed / Total;
}

/// <summary>
/// Runs a dataset of <see cref="EvalCase"/>s against a provider and grades the outputs.
/// Run this on every prompt or model change — "it looked fine on the three examples I tried"
/// is how LLM regressions ship.
/// </summary>
public sealed class EvalRunner(ILlmProvider provider, IGrader grader)
{
    public async Task<EvalReport> RunAsync(
        IReadOnlyList<EvalCase> cases,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<EvalCaseResult>();

        foreach (var evalCase in cases)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await provider.CompleteAsync(new ChatRequest
            {
                Model = model,
                SystemPrompt = evalCase.SystemPrompt,
                Messages = [ChatMessage.User(evalCase.Input)],
            }, cancellationToken);

            var output = response.Text ?? "";
            var grade = await grader.GradeAsync(evalCase, output, cancellationToken);
            results.Add(new EvalCaseResult(evalCase, output, grade, response.Usage, stopwatch.Elapsed.TotalMilliseconds));
        }

        return new EvalReport(
            grader.Name,
            results.Count,
            results.Count(r => r.Grade.Passed),
            results.Count == 0 ? 0 : results.Average(r => r.Grade.Score),
            results.Aggregate(TokenUsage.Zero, (acc, r) => acc + r.Usage),
            results);
    }

    /// <summary>
    /// Loads cases from a JSONL file — one JSON object per line:
    /// {"id": "case-1", "input": "...", "expected": "...", "systemPrompt": "..."}
    /// </summary>
    public static IReadOnlyList<EvalCase> LoadJsonl(string path)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select((line, i) => JsonSerializer.Deserialize<EvalCase>(line, options)
                ?? throw new FormatException($"{path}:{i + 1}: not a valid eval case"))
            .ToList();
    }
}
