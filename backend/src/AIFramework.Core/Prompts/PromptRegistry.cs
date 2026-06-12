namespace AIFramework.Core.Prompts;

/// <summary>A prompt loaded from disk: metadata from frontmatter plus the template body.</summary>
public sealed record Prompt(string Name, int Version, string Description, PromptTemplate Template);

/// <summary>
/// Loads prompts from a directory of Markdown files with a small frontmatter header:
///
///   ---
///   name: summarizer
///   version: 2
///   description: Summarizes a document into bullet points.
///   ---
///   Summarize the following document...
///
/// Why files instead of string constants in code?
/// - Prompts are reviewed in pull requests like any other change (they ARE behavior).
/// - Non-engineers can read and edit them.
/// - Versions are explicit, so evals can compare v1 vs v2.
/// Multiple versions of a name may coexist (e.g. summarizer.v1.md, summarizer.v2.md);
/// Get(name) returns the highest version unless one is pinned.
/// </summary>
public sealed class PromptRegistry
{
    private readonly Dictionary<(string Name, int Version), Prompt> _prompts = [];

    public static PromptRegistry LoadFromDirectory(string directory)
    {
        var registry = new PromptRegistry();
        foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories))
        {
            registry.Add(Parse(File.ReadAllText(file), file));
        }
        return registry;
    }

    public void Add(Prompt prompt) => _prompts[(prompt.Name, prompt.Version)] = prompt;

    /// <summary>Get a prompt by name — the latest version, or an exact version when pinned.</summary>
    public Prompt Get(string name, int? version = null)
    {
        if (version is { } pinned)
        {
            return _prompts.TryGetValue((name, pinned), out var exact)
                ? exact
                : throw new KeyNotFoundException($"Prompt '{name}' v{pinned} not found.");
        }

        var latest = _prompts.Values.Where(p => p.Name == name).MaxBy(p => p.Version);
        return latest ?? throw new KeyNotFoundException($"Prompt '{name}' not found.");
    }

    public IReadOnlyList<Prompt> All() => _prompts.Values.OrderBy(p => p.Name).ThenBy(p => p.Version).ToList();

    internal static Prompt Parse(string content, string sourcePath)
    {
        content = content.Replace("\r\n", "\n");
        if (!content.StartsWith("---\n", StringComparison.Ordinal))
        {
            throw new FormatException($"{sourcePath}: prompt files must start with '---' frontmatter.");
        }

        var end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new FormatException($"{sourcePath}: unterminated frontmatter (missing closing '---').");
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in content[4..end].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                metadata[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        if (!metadata.TryGetValue("name", out var name))
        {
            throw new FormatException($"{sourcePath}: frontmatter is missing required key 'name'.");
        }

        var body = content[(end + 4)..].TrimStart('\n').TrimEnd();
        return new Prompt(
            name,
            metadata.TryGetValue("version", out var rawVersion) && int.TryParse(rawVersion, out var version) ? version : 1,
            metadata.GetValueOrDefault("description", ""),
            new PromptTemplate(body));
    }
}
