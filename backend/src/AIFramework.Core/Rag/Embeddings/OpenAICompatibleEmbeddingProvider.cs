using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIFramework.Core.Rag.Embeddings;

/// <summary>
/// Embeddings via the OpenAI-compatible /embeddings endpoint — works for OpenAI
/// (text-embedding-3-small, 1536 dims) and Ollama (e.g. nomic-embed-text, 768 dims).
/// Anthropic does not offer an embeddings API; pair Claude for generation with one of these
/// (or Voyage AI) for retrieval.
/// </summary>
public sealed class OpenAICompatibleEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly Uri _embeddingsUri;
    private readonly string _model;

    public OpenAICompatibleEmbeddingProvider(
        HttpClient http, string name, string baseUrl, string model, int dimensions, string? apiKey = null)
    {
        _http = http;
        Name = name;
        _model = model;
        Dimensions = dimensions;
        _embeddingsUri = new Uri(baseUrl.TrimEnd('/') + "/embeddings");
        if (apiKey is not null)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public string Name { get; }
    public int Dimensions { get; }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["input"] = new JsonArray(texts.Select(t => (JsonNode)t!).ToArray()),
        };

        var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(_embeddingsUri, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"{Name} embeddings failed ({(int)response.StatusCode}): {error}");
        }

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        // Results may arrive out of order; the API contract is to sort by "index".
        return json.RootElement.GetProperty("data")
            .EnumerateArray()
            .OrderBy(item => item.GetProperty("index").GetInt32())
            .Select(item => item.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray())
            .ToList();
    }
}
