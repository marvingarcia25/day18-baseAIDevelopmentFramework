using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AIFramework.Core.Rag.Embeddings;

/// <summary>
/// A deterministic, offline embedding based on hashed bag-of-words (the "hashing trick").
/// It captures word overlap, not meaning — "car" and "automobile" are unrelated to it —
/// so it is NOT a substitute for real embeddings in production.
/// It exists so the RAG pipeline runs with zero API keys: demos, tests, CI.
/// </summary>
public sealed partial class HashingEmbeddingProvider(int dimensions = 256) : IEmbeddingProvider
{
    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex TokenRegex();

    public string Name => "hashing-local";
    public int Dimensions { get; } = dimensions;

    public Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<float[]> result = texts.Select(Embed).ToList();
        return Task.FromResult(result);
    }

    private float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (Match token in TokenRegex().Matches(text.ToLowerInvariant()))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token.Value));
            var bucket = (int)(BitConverter.ToUInt32(hash, 0) % (uint)Dimensions);
            var sign = (hash[4] & 1) == 0 ? 1f : -1f; // signed buckets reduce hash collisions' bias
            vector[bucket] += sign;
        }

        // L2-normalize so cosine similarity behaves.
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }
        return vector;
    }
}
