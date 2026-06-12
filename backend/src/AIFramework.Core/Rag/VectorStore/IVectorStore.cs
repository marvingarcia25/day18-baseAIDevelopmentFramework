namespace AIFramework.Core.Rag.VectorStore;

/// <summary>A chunk of a source document, with its embedding and provenance metadata.</summary>
public sealed record DocumentChunk(
    string Id,
    string DocumentId,
    string Text,
    float[] Embedding,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>A retrieved chunk with its similarity to the query (cosine, higher is better).</summary>
public sealed record ScoredChunk(DocumentChunk Chunk, float Score);

/// <summary>
/// Where embedded chunks live and get searched. The in-memory implementation is fine well into
/// the tens of thousands of chunks; beyond that, implement this interface over pgvector,
/// Qdrant, Azure AI Search, etc. — the RAG pipeline won't notice.
/// </summary>
public interface IVectorStore
{
    Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScoredChunk>> QueryAsync(
        float[] queryEmbedding, int topK = 5, CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
