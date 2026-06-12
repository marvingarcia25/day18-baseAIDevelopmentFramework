using System.Collections.Concurrent;

namespace AIFramework.Core.Rag.VectorStore;

/// <summary>Brute-force cosine-similarity search over an in-memory dictionary.</summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, DocumentChunk> _chunks = new();

    public Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        foreach (var chunk in chunks)
        {
            _chunks[chunk.Id] = chunk;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScoredChunk>> QueryAsync(
        float[] queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ScoredChunk> results = _chunks.Values
            .Select(chunk => new ScoredChunk(chunk, CosineSimilarity(queryEmbedding, chunk.Embedding)))
            .OrderByDescending(scored => scored.Score)
            .Take(topK)
            .ToList();
        return Task.FromResult(results);
    }

    public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        foreach (var id in _chunks.Where(p => p.Value.DocumentId == documentId).Select(p => p.Key).ToList())
        {
            _chunks.TryRemove(id, out _);
        }
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_chunks.Count);

    internal static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException(
                $"Embedding dimensions differ ({a.Length} vs {b.Length}) — were they produced by different models?");
        }

        float dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return normA == 0 || normB == 0 ? 0 : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
