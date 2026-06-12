using AIFramework.Core.Evals;
using AIFramework.Core.Llm;
using AIFramework.Core.Observability;

namespace AIFramework.Core.Tests;

public class GraderTests
{
    private static readonly EvalCase Case = new("t1", "What is the capital of France?", "Paris");

    [Theory]
    [InlineData("Paris", true)]
    [InlineData("  paris ", true)]
    [InlineData("The capital is Paris.", false)]
    public async Task ExactMatch_compares_trimmed_case_insensitive(string output, bool expected)
    {
        var grade = await new ExactMatchGrader().GradeAsync(Case, output);
        Assert.Equal(expected, grade.Passed);
    }

    [Theory]
    [InlineData("The capital is Paris.", true)]
    [InlineData("I don't know.", false)]
    public async Task Contains_checks_for_substring(string output, bool expected)
    {
        var grade = await new ContainsGrader().GradeAsync(Case, output);
        Assert.Equal(expected, grade.Passed);
    }

    [Fact]
    public async Task LlmJudge_parses_score_json_even_with_surrounding_prose()
    {
        var judge = new FakeLlmProvider(FakeLlmProvider.TextResponse(
            "Here is my grade: {\"score\": 0.9, \"reasoning\": \"Correct and complete.\"}"));
        var grade = await new LlmJudgeGrader(judge).GradeAsync(Case, "Paris");

        Assert.True(grade.Passed);
        Assert.Equal(0.9, grade.Score);
    }

    [Fact]
    public async Task LlmJudge_fails_safe_on_unparseable_output()
    {
        var judge = new FakeLlmProvider(FakeLlmProvider.TextResponse("I refuse to answer in JSON"));
        var grade = await new LlmJudgeGrader(judge).GradeAsync(Case, "Paris");

        Assert.False(grade.Passed);
        Assert.Equal(0, grade.Score);
    }
}

public class EvalRunnerTests
{
    [Fact]
    public async Task Runner_aggregates_pass_rate_and_usage()
    {
        var provider = new FakeLlmProvider(
            FakeLlmProvider.TextResponse("Paris"),
            FakeLlmProvider.TextResponse("wrong"));
        var runner = new EvalRunner(provider, new ContainsGrader());

        var report = await runner.RunAsync(
        [
            new EvalCase("a", "Capital of France?", "Paris"),
            new EvalCase("b", "Capital of Spain?", "Madrid"),
        ]);

        Assert.Equal(2, report.Total);
        Assert.Equal(1, report.Passed);
        Assert.Equal(0.5, report.PassRate);
        Assert.Equal(new TokenUsage(20, 10), report.TotalUsage);
    }
}

public class ObservabilityTests
{
    [Fact]
    public async Task Instrumented_provider_records_every_call()
    {
        var tracker = new UsageTracker();
        var provider = new InstrumentedLlmProvider(
            new FakeLlmProvider(FakeLlmProvider.TextResponse("hi"), FakeLlmProvider.TextResponse("again")),
            tracker);

        await provider.CompleteAsync(new ChatRequest { Messages = [ChatMessage.User("x")] });
        await foreach (var _ in provider.StreamAsync(new ChatRequest { Messages = [ChatMessage.User("y")] }))
        {
        }

        var report = tracker.GetReport();
        Assert.Equal(2, report.TotalCalls);
        Assert.Equal(new TokenUsage(20, 10), report.TotalUsage);
        Assert.Contains(report.Calls, call => call.Streamed);
    }

    [Fact]
    public void Pricing_estimates_known_models_and_zeroes_unknown_ones()
    {
        var usage = new TokenUsage(1_000_000, 1_000_000);

        Assert.Equal(30.00m, ModelPricing.EstimateCostUsd("claude-opus-4-8", usage));
        Assert.Equal(6.00m, ModelPricing.EstimateCostUsd("claude-haiku-4-5-20251001", usage)); // prefix match
        Assert.Equal(0m, ModelPricing.EstimateCostUsd("some-unknown-model", usage));
    }
}
