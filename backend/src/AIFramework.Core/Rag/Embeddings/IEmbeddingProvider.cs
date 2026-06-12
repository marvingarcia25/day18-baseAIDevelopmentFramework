namespace AIFramework.Core.Rag.Embeddings;

/// <summary>
/// Turns text into vectors. Like <see cref="Llm.ILlmProvider"/>, this is the seam that keeps
/// the RAG pipeline vendor-agnostic. Note: embeddings from different providers/models live in
/// different vector spaces — never mix them in one index.
/// </summary>
public interface IEmbeddingProvider
{
    string Name { get; }

    /// <summary>Embedding dimension, so stores can validate vectors up front.</summary>
    int Dimensions { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}
