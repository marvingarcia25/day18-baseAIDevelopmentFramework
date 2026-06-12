using System.Data;
using System.Text.Json;

namespace AIFramework.Core.Agents.Tools;

/// <summary>
/// Evaluates basic arithmetic. Models are unreliable at arithmetic, so this is the classic
/// first tool: cheap, deterministic, and easy to verify end-to-end.
/// </summary>
public sealed class CalculatorTool : ITool
{
    public string Name => "calculator";

    public string Description =>
        "Evaluate an arithmetic expression with +, -, *, /, % and parentheses. " +
        "Call this for any calculation instead of computing it yourself.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            expression = new
            {
                type = "string",
                description = "The expression to evaluate, e.g. \"(12.5 * 4) / 3\"",
            },
        },
        required = new[] { "expression" },
    });

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var expression = arguments.GetProperty("expression").GetString() ?? "";

        // Whitelist characters before handing to DataTable.Compute, which accepts more than arithmetic.
        if (expression.Any(c => !"0123456789.+-*/%() ".Contains(c)))
        {
            return Task.FromResult("Error: expression may only contain numbers, + - * / % ( ) and spaces.");
        }

        try
        {
            var result = new DataTable().Compute(expression, null);
            return Task.FromResult(Convert.ToDouble(result).ToString("G15"));
        }
        catch (Exception exception)
        {
            return Task.FromResult($"Error: could not evaluate expression: {exception.Message}");
        }
    }
}
