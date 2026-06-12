# Retrieval-Augmented Generation (RAG)

Models only know their training data and your prompt. RAG closes the gap for *your* data: store
documents as vectors, retrieve the relevant pieces per question, and let the model answer **only
from what was retrieved** — with citations, and with "I don't know" as a legal answer.

## The pipeline (`backend/src/AIFramework.Core/Rag`)

```
Ingest:  document ──► TextChunker ──► IEmbeddingProvider ──► IVectorStore
Ask:     question ──► embed ──► top-K similar chunks ──► grounded prompt ──► answer + citations
```

`RagPipeline.cs` is the orchestration; each stage hides behind an interface so you can upgrade
them independently.

### Chunking (`TextChunker`)

Documents must be split because (a) embeddings represent *one idea* well and *twenty* poorly, and
(b) you can't afford to stuff whole documents into every prompt. The chunker packs whole
paragraphs up to a size limit with a small overlap so facts that straddle boundaries stay
retrievable. Chunk size is the highest-impact RAG knob: too small loses context, too big dilutes
the embedding. Start around 1,000–2,000 characters and tune against real queries.

### Embeddings (`IEmbeddingProvider`)

An embedding maps text to a vector where semantic similarity ≈ geometric closeness. Three
implementations ship:

| Provider | Use for | Note |
|---|---|---|
| `hashing` (default) | demos, tests, CI | Offline and deterministic, but it's word overlap, **not meaning** — don't judge RAG quality on it |
| `openai` | production | `text-embedding-3-small`, 1536 dims |
| `ollama` | local/private data | `nomic-embed-text`, 768 dims, free |

Anthropic doesn't offer an embeddings API — pairing Claude (generation) with OpenAI/Ollama/Voyage
(embeddings) is normal. **Never mix embeddings from different models in one index**; the store
throws on dimension mismatch, but same-dimension different-model vectors fail silently — re-ingest
everything when you switch.

### Vector store (`IVectorStore`)

The in-memory store does brute-force cosine similarity — entirely adequate into the tens of
thousands of chunks, and zero infrastructure. When you outgrow it, implement the same four-method
interface over pgvector/Qdrant/Azure AI Search. Your data model (`DocumentChunk` with provenance
metadata) already matches what those systems want.

### The grounded prompt

`RagPipeline.AskAsync` numbers each retrieved chunk and instructs the model to answer *only* from
them, cite by `[number]`, and say so when the context doesn't contain the answer. That last
instruction is the anti-hallucination workhorse — without it the model fills gaps from training
data and sounds confident doing it.

## Debugging RAG (it's almost always retrieval)

When answers are bad, look at the retrieved chunks *before* blaming the model — the API response
includes them as `sources` with scores:

1. **Right chunks, bad answer** → prompt problem. Tighten the grounding instructions.
2. **Wrong chunks** → retrieval problem. Better embeddings, different chunk size, more/fewer
   chunks (`TopK`), or a `MinScore` floor to drop noise.
3. **Right document never retrieved** → ingestion problem. Was it chunked sensibly? Does its
   vocabulary match how users ask? (Consider adding titles/headings into each chunk's text.)

Build a small eval set of (question → expected source chunk) pairs and measure retrieval hit-rate
separately from answer quality — it isolates which half is broken.

## Upgrades, in the order that usually pays off

1. Real embeddings (if you're still on `hashing`).
2. Metadata filters (per-tenant, per-source) — the `DocumentChunk.Metadata` field is there.
3. Hybrid retrieval: combine vector similarity with keyword search (BM25) and merge.
4. Reranking: over-retrieve (say 25), then have a cheap model score relevance and keep the top 5.
5. Query rewriting: have the model turn a conversational question into a standalone search query.
