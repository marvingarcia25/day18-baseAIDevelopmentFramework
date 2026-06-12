using AIFramework.Core.Evals;
using AIFramework.Core.Llm;

// Usage:
//   dotnet run --project src/AIFramework.Evals -- datasets/sample.jsonl [grader] [model]
//
//   grader: contains (default) | exact | regex | judge
//   model:  overrides the provider's default model
//
// Provider selection mirrors the API: env vars LLM_PROVIDER (anthropic|openai|ollama),
// LLM_MODEL, plus the provider's own key env var (ANTHROPIC_API_KEY / OPENAI_API_KEY).

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run -- <dataset.jsonl> [contains|exact|regex|judge] [model]");
    return 1;
}

var datasetPath = args[0];
var graderName = args.Length > 1 ? args[1] : "contains";
var model = args.Length > 2 ? args[2] : null;

var provider = LlmProviderFactory.Create(new LlmOptions
{
    Provider = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "anthropic",
    Model = Environment.GetEnvironmentVariable("LLM_MODEL"),
});

IGrader grader = graderName switch
{
    "exact" => new ExactMatchGrader(),
    "contains" => new ContainsGrader(),
    "regex" => new RegexGrader(),
    "judge" => new LlmJudgeGrader(provider),
    _ => throw new ArgumentException($"Unknown grader '{graderName}'."),
};

var cases = EvalRunner.LoadJsonl(datasetPath);
Console.WriteLine($"Running {cases.Count} cases against {provider.Name} ({model ?? provider.DefaultModel}) with grader '{grader.Name}'...\n");

var report = await new EvalRunner(provider, grader).RunAsync(cases, model);

foreach (var result in report.Results)
{
    var mark = result.Grade.Passed ? "PASS" : "FAIL";
    Console.WriteLine($"[{mark}] {result.Case.Id}  score={result.Grade.Score:0.00}  ({result.DurationMs:0}ms)");
    if (!result.Grade.Passed)
    {
        Console.WriteLine($"       {result.Grade.Reasoning}");
    }
}

Console.WriteLine($"\nPassed {report.Passed}/{report.Total} ({report.PassRate:P0})  avg score {report.AverageScore:0.00}");
Console.WriteLine($"Tokens: {report.TotalUsage.InputTokens} in / {report.TotalUsage.OutputTokens} out");

// Write a machine-readable report next to the dataset (handy for CI artifacts and diffing runs).
var reportPath = Path.ChangeExtension(datasetPath, ".results.json");
await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(report,
    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Report written to {reportPath}");

return report.Passed == report.Total ? 0 : 1;
