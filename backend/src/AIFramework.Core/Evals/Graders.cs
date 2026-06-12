using System.Text.Json;
using System.Text.RegularExpressions;
using AIFramework.Core.Llm;

namespace AIFramework.Core.Evals;

/// <summary>A single test case: an input, and what a good output looks like.</summary>
public sealed record EvalCase(
    string Id,
    string Input,
    string Expected,
    string? SystemPrompt = null);

public sealed record GradeResult(bool Passed, double Score, string Reasoning);

/// <summary>
/// Scores a model output against an expectation. Start with cheap deterministic graders
/// (exact/contains/regex); reach for the LLM judge only when correctness is subjective.
/// </summary>
public interface IGrader
{
    string Name { get; }
    Task<GradeResult> GradeAsync(EvalCase evalCase, string actualOutput, CancellationToken cancellationToken = default);
}

/// <summary>Pass iff the output equals the expectation (after trimming). For closed-form answers.</summary>
public sealed class ExactMatchGrader : IGrader
{
    public string Name => "exact_match";

    public Task<GradeResult> GradeAsync(EvalCase evalCase, string actualOutput, CancellationToken cancellationToken = default)
    {
        var passed = string.Equals(actualOutput.Trim(), evalCase.Expected.Trim(), StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(new GradeResult(passed, passed ? 1 : 0,
            passed ? "Exact match." : $"Expected \"{evalCase.Expected}\", got \"{Truncate(actualOutput)}\"."));
    }

    private static string Truncate(string s) => s.Length <= 120 ? s : s[..120] + "…";
}

/// <summary>Pass iff the output contains the expected substring. For "must mention X" checks.</summary>
public sealed class ContainsGrader : IGrader
{
    public string Name => "contains";

    public Task<GradeResult> GradeAsync(EvalCase evalCase, string actualOutput, CancellationToken cancellationToken = default)
    {
        var passed = actualOutput.Contains(evalCase.Expected, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(new GradeResult(passed, passed ? 1 : 0,
            passed ? $"Output contains \"{evalCase.Expected}\"." : $"Output does not contain \"{evalCase.Expected}\"."));
    }
}

/// <summary>Pass iff the output matches the expected regular expression.</summary>
public sealed class RegexGrader : IGrader
{
    public string Name => "regex";

    public Task<GradeResult> GradeAsync(EvalCase evalCase, string actualOutput, CancellationToken cancellationToken = default)
    {
        var passed = Regex.IsMatch(actualOutput, evalCase.Expected,
            RegexOptions.Singleline, TimeSpan.FromSeconds(2));
        return Task.FromResult(new GradeResult(passed, passed ? 1 : 0,
            passed ? "Pattern matched." : $"Pattern /{evalCase.Expected}/ did not match."));
    }
}

/// <summary>
/// LLM-as-judge: a second model scores the output against the expectation on a 0–1 scale.
/// Use for open-ended outputs (summaries, explanations) where string matching can't work.
/// Caveats: judges have biases (verbosity, self-preference) — spot-check their grades,
/// and prefer a different/stronger model than the one being evaluated.
/// </summary>
public sealed class LlmJudgeGrader(ILlmProvider judge, double passThreshold = 0.7) : IGrader
{
    public string Name => "llm_judge";

    public async Task<GradeResult> GradeAsync(
        EvalCase evalCase, string actualOutput, CancellationToken cancellationToken = default)
    {
        var response = await judge.CompleteAsync(new ChatRequest
        {
            SystemPrompt =
                "You are grading an AI assistant's answer. Compare it to the reference. " +
                "Judge correctness and completeness, not style or length. " +
                "Respond with ONLY a JSON object: {\"score\": <0.0-1.0>, \"reasoning\": \"<one sentence>\"}",
            Messages =
            [
                ChatMessage.User(
                    $"Question:\n{evalCase.Input}\n\nReference answer:\n{evalCase.Expected}\n\nAnswer to grade:\n{actualOutput}"),
            ],
            MaxTokens = 512,
        }, cancellationToken);

        try
        {
            var text = response.Text ?? "";
            // Tolerate judges that wrap the JSON in prose or code fences.
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            using var json = JsonDocument.Parse(text[start..(end + 1)]);
            var score = json.RootElement.GetProperty("score").GetDouble();
            var reasoning = json.RootElement.GetProperty("reasoning").GetString() ?? "";
            return new GradeResult(score >= passThreshold, score, reasoning);
        }
        catch (Exception)
        {
            return new GradeResult(false, 0, $"Judge returned unparseable output: {response.Text}");
        }
    }
}
