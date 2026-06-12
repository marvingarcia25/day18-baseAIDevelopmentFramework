namespace AIFramework.Core.Rag.Embeddings;

public sealed record EmbeddingOptions
{
    /// <summary>"hashing" (offline, no key), "openai", or "ollama".</summary>
    public string Provider { get; init; } = "hashing";

    public string? Model { get; init; }
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public int? Dimensions { get; init; }
}

public static class EmbeddingProviderFactory
{
    public static IEmbeddingProvider Create(EmbeddingOptions options, HttpClient? http = null)
    {
        // Configuration binding turns JSON nulls into empty strings — treat both as "not set".
        var model = Normalize(options.Model);
        var apiKey = Normalize(options.ApiKey);
        var baseUrl = Normalize(options.BaseUrl);

        return options.Provider.ToLowerInvariant() switch
        {
            "hashing" => new HashingEmbeddingProvider(options.Dimensions ?? 256),

            "openai" => new OpenAICompatibleEmbeddingProvider(
                http ?? new HttpClient(),
                name: "openai-embeddings",
                baseUrl: baseUrl ?? "https://api.openai.com/v1",
                model: model ?? "text-embedding-3-small",
                dimensions: options.Dimensions ?? 1536,
                apiKey: apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")),

            "ollama" => new OpenAICompatibleEmbeddingProvider(
                http ?? new HttpClient(),
                name: "ollama-embeddings",
                baseUrl: baseUrl ?? "http://localhost:11434/v1",
                model: model ?? "nomic-embed-text",
                dimensions: options.Dimensions ?? 768),

            _ => throw new ArgumentException(
                $"Unknown embedding provider '{options.Provider}'. Expected: hashing, openai, ollama."),
        };
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
