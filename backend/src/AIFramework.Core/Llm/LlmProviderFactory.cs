using AIFramework.Core.Llm.Providers;

namespace AIFramework.Core.Llm;

/// <summary>Configuration for picking and connecting to an LLM provider.</summary>
public sealed record LlmOptions
{
    /// <summary>"anthropic", "openai", or "ollama".</summary>
    public string Provider { get; init; } = "anthropic";

    /// <summary>Default model id; null uses the provider's built-in default.</summary>
    public string? Model { get; init; }

    /// <summary>API key. Null falls back to the provider's environment variable.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Override the API root (self-hosted gateways, proxies, alternative hosts).</summary>
    public string? BaseUrl { get; init; }
}

public static class LlmProviderFactory
{
    /// <summary>
    /// Build a provider from options. Adding a vendor means adding a case here and one adapter class —
    /// nothing above <see cref="ILlmProvider"/> changes.
    /// </summary>
    public static ILlmProvider Create(LlmOptions options, HttpClient? http = null)
    {
        // Configuration binding turns JSON nulls into empty strings — treat both as "not set".
        var model = Normalize(options.Model);
        var apiKey = Normalize(options.ApiKey);
        var baseUrl = Normalize(options.BaseUrl);

        return options.Provider.ToLowerInvariant() switch
        {
            "anthropic" => new AnthropicProvider(
                apiKey,
                model ?? "claude-opus-4-8"),

            "openai" => new OpenAICompatibleProvider(
                http ?? new HttpClient(),
                name: "openai",
                baseUrl: baseUrl ?? "https://api.openai.com/v1",
                defaultModel: model ?? "gpt-4o",
                apiKey: apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")),

            "ollama" => new OpenAICompatibleProvider(
                http ?? new HttpClient(),
                name: "ollama",
                baseUrl: baseUrl ?? "http://localhost:11434/v1",
                defaultModel: model ?? "llama3.1",
                apiKey: null),

            _ => throw new ArgumentException(
                $"Unknown provider '{options.Provider}'. Expected: anthropic, openai, ollama."),
        };
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
