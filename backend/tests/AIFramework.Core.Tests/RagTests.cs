using AIFramework.Core.Rag;
using AIFramework.Core.Rag.Embeddings;
using AIFramework.Core.Rag.VectorStore;

namespace AIFramework.Core.Tests;

public class TextChunkerTests
{
    [Fact]
    public void Short_text_is_a_single_chunk()
    {
        var chunks = new TextChunker(maxChunkChars: 100).Chunk("Hello world.");

        Assert.Equal(["Hello world."], chunks);
    }

    [Fact]
    public void Paragraphs_are_packed_up_to_the_limit()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 10).Select(i => $"Paragraph number {i} with some words."));
        var chunker = new TextChunker(maxChunkChars: 120, overlapChars: 0);

        var chunks = chunker.Chunk(text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 120));
        // No content lost: every paragraph appears somewhere.
        for (var i = 1; i <= 10; i++)
        {
            Assert.Contains(chunks, c => c.Contains($"Paragraph number {i}"));
        }
    }

    [Fact]
    public void Oversized_paragraph_is_split()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 200)); // single huge paragraph
        var chunks = new TextChunker(maxChunkChars: 150, overlapChars: 0).Chunk(text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 150));
    }

    [Fact]
    public void Empty_input_yields_no_chunks()
    {
        Assert.Empty(new TextChunker().Chunk("   \n  "));
    }
}

public class InMemoryVectorStoreTests
{
    [Fact]
    public async Task Query_returns_most_similar_chunks_first()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(
        [
            new DocumentChunk("a", "doc", "about cats", [1f, 0f, 0f]),
            new DocumentChunk("b", "doc", "about dogs", [0f, 1f, 0f]),
            new DocumentChunk("c", "doc", "about cats too", [0.9f, 0.1f, 0f]),
        ]);

        var results = await store.QueryAsync([1f, 0f, 0f], topK: 2);

        Assert.Equal(["a", "c"], results.Select(r => r.Chunk.Id));
        Assert.True(results[0].Score >= results[1].Score);
    }

    [Fact]
    public async Task Upsert_replaces_existing_chunk_and_delete_removes_document()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync([new DocumentChunk("a", "doc1", "v1", [1f, 0f])]);
        await store.UpsertAsync([new DocumentChunk("a", "doc1", "v2", [1f, 0f])]);

        Assert.Equal(1, await store.CountAsync());

        await store.DeleteDocumentAsync("doc1");
        Assert.Equal(0, await store.CountAsync());
    }

    [Fact]
    public void Mismatched_dimensions_throw_a_clear_error()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            InMemoryVectorStore.CosineSimilarity([1f, 0f], [1f, 0f, 0f]));
        Assert.Contains("dimensions differ", exception.Message);
    }
}

public class HashingEmbeddingProviderTests
{
    [Fact]
    public async Task Embeddings_are_deterministic_and_normalized()
    {
        var provider = new HashingEmbeddingProvider(64);

        var first = (await provider.EmbedAsync(["the quick brown fox"]))[0];
        var second = (await provider.EmbedAsync(["the quick brown fox"]))[0];

        Assert.Equal(first, second);
        Assert.Equal(1f, MathF.Sqrt(first.Sum(v => v * v)), precision: 3);
    }

    [Fact]
    public async Task Overlapping_texts_score_higher_than_unrelated_ones()
    {
        var provider = new HashingEmbeddingProvider(256);
        var vectors = await provider.EmbedAsync(
        [
            "the cat sat on the mat",
            "a cat sat on a mat",
            "quantum chromodynamics lagrangian",
        ]);

        var similar = InMemoryVectorStore.CosineSimilarity(vectors[0], vectors[1]);
        var unrelated = InMemoryVectorStore.CosineSimilarity(vectors[0], vectors[2]);

        Assert.True(similar > unrelated);
    }
}

public class RagPipelineTests
{
    [Fact]
    public async Task Ask_retrieves_relevant_chunks_and_passes_them_to_the_model()
    {
        var llm = new FakeLlmProvider(FakeLlmProvider.TextResponse("Returns are accepted within 30 days [1]."));
        var pipeline = new RagPipeline(llm, new HashingEmbeddingProvider(256), new InMemoryVectorStore());

        await pipeline.IngestAsync("policy", "Our return policy: items can be returned within 30 days of purchase.");
        await pipeline.IngestAsync("shipping", "Shipping: orders ship within 2 business days via ground freight.");

        var answer = await pipeline.AskAsync("How long do I have to return an item?");

        Assert.Contains("30 days", answer.Answer);
        Assert.NotEmpty(answer.Sources);
        // The grounded prompt sent to the model must contain the retrieved context.
        var sentPrompt = llm.Requests.Single().Messages.Single().Content;
        Assert.Contains("return policy", sentPrompt);
    }

    [Fact]
    public async Task Ask_with_empty_index_does_not_call_the_model()
    {
        var llm = new FakeLlmProvider();
        var pipeline = new RagPipeline(llm, new HashingEmbeddingProvider(64), new InMemoryVectorStore());

        var answer = await pipeline.AskAsync("anything");

        Assert.Empty(llm.Requests);
        Assert.Empty(answer.Sources);
    }
}
