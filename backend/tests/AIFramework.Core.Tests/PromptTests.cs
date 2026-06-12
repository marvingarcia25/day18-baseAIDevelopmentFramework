using AIFramework.Core.Prompts;

namespace AIFramework.Core.Tests;

public class PromptTemplateTests
{
    [Fact]
    public void Render_substitutes_variables()
    {
        var template = new PromptTemplate("Summarize {{document}} in {{count}} bullets.");

        var rendered = template.Render(new Dictionary<string, string>
        {
            ["document"] = "the report",
            ["count"] = "3",
        });

        Assert.Equal("Summarize the report in 3 bullets.", rendered);
        Assert.Equal(["document", "count"], template.Variables);
    }

    [Fact]
    public void Render_throws_on_missing_variable()
    {
        var template = new PromptTemplate("Hello {{name}}");

        var exception = Assert.Throws<ArgumentException>(() =>
            template.Render(new Dictionary<string, string>()));
        Assert.Contains("name", exception.Message);
    }

    [Fact]
    public void Whitespace_inside_braces_is_tolerated()
    {
        var template = new PromptTemplate("{{ name }} and {{name}}");

        Assert.Equal("A and A", template.Render(new Dictionary<string, string> { ["name"] = "A" }));
    }
}

public class PromptRegistryTests
{
    private const string SampleFile = """
        ---
        name: summarizer
        version: 2
        description: Summarizes documents.
        ---
        Summarize this: {{document}}
        """;

    [Fact]
    public void Parse_reads_frontmatter_and_body()
    {
        var prompt = PromptRegistry.Parse(SampleFile, "test.md");

        Assert.Equal("summarizer", prompt.Name);
        Assert.Equal(2, prompt.Version);
        Assert.Equal("Summarizes documents.", prompt.Description);
        Assert.Equal("Summarize this: {{document}}", prompt.Template.Template);
    }

    [Fact]
    public void Get_returns_latest_version_unless_pinned()
    {
        var registry = new PromptRegistry();
        registry.Add(new Prompt("p", 1, "", new PromptTemplate("v1")));
        registry.Add(new Prompt("p", 2, "", new PromptTemplate("v2")));

        Assert.Equal("v2", registry.Get("p").Template.Template);
        Assert.Equal("v1", registry.Get("p", version: 1).Template.Template);
    }

    [Fact]
    public void Parse_rejects_file_without_frontmatter()
    {
        Assert.Throws<FormatException>(() => PromptRegistry.Parse("no frontmatter here", "bad.md"));
    }
}
