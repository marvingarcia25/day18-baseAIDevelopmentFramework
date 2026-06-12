using AIFramework.Core.Llm;
using AIFramework.Core.Rag.Embeddings;
using AIFramework.Core.Rag.VectorStore;

namespace AIFramework.Core.Rag;

public sealed record RagOptions
{
    public int TopK { get; init; } = 5;

    /// <summary>Chunks scoring below this are dropped — retrieving noise is worse than retrieving less.</summary>
    public float MinScore { get; init; } = 0.0f;

    public string SystemPrompt { get; init; } =
        "You answer questions using ONLY the provided context. " +
        "Cite the sources you used by their [number]. " +
        "If the context does not contain the answer, say so plainly — do not guess.";
}

public sealed record RagAnswer(
    string Answer,
    IReadOnlyList<ScoredChunk> Sources,
    TokenUsage Usage);

/// <summary>
/// Retrieval-Augmented Generation, end to end:
///   ingest: document → chunks → embeddings → vector store
///   ask:    question → embedding → top-K chunks → grounded prompt → LLM answer with citations
/// </summary>
public sealed class RagPipeline(
    ILlmProvider llm,
    IEmbeddingProvider embeddings,
    IVectorStore store,
    TextChunker? chunker = null,
    RagOptions? options = null)
{
    private readonly TextChunker _chunker = chunker ?? new TextChunker();
    private readonly RagOptions _options = options ?? new RagOptions();

    /// <summary>Chunk, embed, and index a document. Returns the number of chunks stored.</summary>
    public async Task<int> IngestAsync(
        string documentId,
        string text,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        // Re-ingesting a document replaces it rather than duplicating chunks.
        await store.DeleteDocumentAsync(documentId, cancellationToken);

        var chunks = _chunker.Chunk(text);
        if (chunks.Count == 0)
        {
            return 0;
        }

        var vectors = await embeddings.EmbedAsync(chunks, cancellationToken);
        var documents = chunks
            .Select((chunk, i) => new DocumentChunk($"{documentId}#{i}", documentId, chunk, vectors[i], metadata))
            .ToList();

        await store.UpsertAsync(documents, cancellationToken);
        return documents.Count;
    }

    public async Task<RagAnswer> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var queryVector = (await embeddings.EmbedAsync([question], cancellationToken))[0];
        var retrieved = (await store.QueryAsync(queryVector, _options.TopK, cancellationToken))
            .Where(scored => scored.Score >= _options.MinScore)
            .ToList();

        if (retrieved.Count == 0)
        {
            return new RagAnswer("I couldn't find anything relevant in the indexed documents.", [], TokenUsage.Zero);
        }

        var context = string.Join("\n\n", retrieved.Select((scored, i) =>
            $"[{i + 1}] (from {scored.Chunk.DocumentId})\n{scored.Chunk.Text}"));

        var response = await llm.CompleteAsync(new ChatRequest
        {
            SystemPrompt = _options.SystemPrompt,
            Messages =
            [
                ChatMessage.User($"Context:\n{context}\n\nQuestion: {question}"),
            ],
        }, cancellationToken);

        return new RagAnswer(response.Text ?? "", retrieved, response.Usage);
    }
}
