using System.Text.RegularExpressions;

namespace AIFramework.Core.Prompts;

/// <summary>
/// A text template with {{variable}} placeholders.
/// Rendering fails loudly on missing variables — a silently empty placeholder in a prompt
/// is one of the most common (and hardest to spot) LLM bugs.
/// </summary>
public sealed partial class PromptTemplate(string template)
{
    [GeneratedRegex(@"\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    public string Template { get; } = template;

    /// <summary>Variable names referenced by the template, in order of first appearance.</summary>
    public IReadOnlyList<string> Variables { get; } =
        PlaceholderRegex().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    public string Render(IReadOnlyDictionary<string, string> values)
    {
        var missing = Variables.Where(v => !values.ContainsKey(v)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"Missing template variables: {string.Join(", ", missing)}");
        }
        return PlaceholderRegex().Replace(Template, match => values[match.Groups[1].Value]);
    }
}
