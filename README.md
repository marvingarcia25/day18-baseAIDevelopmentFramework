# Base AI Development Framework

A batteries-included starting point for building AI applications, designed for developers who are
new to AI engineering. It implements the core building blocks of modern LLM apps **from scratch,
with minimal dependencies**, so you can read every line and understand how things actually work —
then swap pieces for production-grade services as you grow.

**Stack:** C# (.NET 8) backend · TypeScript (React + Vite) frontend · provider-agnostic LLM layer
(Anthropic / OpenAI / Ollama and any OpenAI-compatible server).

## What's inside

| Capability | Where | What you get |
|---|---|---|
| **Provider abstraction** | `backend/src/AIFramework.Core/Llm` | One `ILlmProvider` interface; adapters for Anthropic (official SDK) and any OpenAI-compatible API (raw HTTP, so you see the wire format). Streaming + tool calling on both. |
| **Agents & tool use** | `backend/src/AIFramework.Core/Agents` | The agent loop written out explicitly (~60 lines), a tool interface, a registry, and example tools. |
| **Prompt management** | `backend/src/AIFramework.Core/Prompts` + `/prompts` | Versioned prompt files with frontmatter, a registry, and fail-loud `{{variable}}` templates. |
| **RAG pipeline** | `backend/src/AIFramework.Core/Rag` | Chunking → embeddings → vector store → grounded answers with citations. Includes an offline embedding so it runs with zero API keys. |
| **Evals** | `backend/src/AIFramework.Core/Evals` + `AIFramework.Evals` | A test harness for prompts/models: deterministic graders, LLM-as-judge, JSONL datasets, CI-friendly exit codes. |
| **Observability** | `backend/src/AIFramework.Core/Observability` | Token, latency, and cost tracking for every LLM call via a provider decorator. |
| **HTTP API** | `backend/src/AIFramework.Api` | Minimal API exposing chat (with SSE streaming), agent, RAG, and usage endpoints. |
| **Web UI** | `frontend/` | React chat with token streaming, agent step visualization, RAG playground, usage dashboard. |
| **Docs** | `docs/` | Guides on architecture, prompt engineering, agents, RAG, and evals — written for newcomers. |

## Quickstart

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Node.js 20+](https://nodejs.org).

```bash
# 1. Backend — pick a provider via environment variables (Anthropic is the default)
export ANTHROPIC_API_KEY=sk-ant-...
cd backend
dotnet run --project src/AIFramework.Api        # → http://localhost:5080

# 2. Frontend (separate terminal)
cd frontend
npm install
npm run dev                                      # → http://localhost:5173
```

Open http://localhost:5173 and try the four tabs: **Chat** (streaming), **Agent** (watch tool
calls), **RAG** (ingest a document, ask grounded questions), **Usage** (cost telemetry).

### Switching providers

Edit `backend/src/AIFramework.Api/appsettings.json` (or use env vars like `Llm__Provider`):

```jsonc
"Llm": { "Provider": "anthropic" }                          // default; uses ANTHROPIC_API_KEY
"Llm": { "Provider": "openai" }                             // uses OPENAI_API_KEY
"Llm": { "Provider": "ollama", "Model": "llama3.1" }        // local, no key needed
```

RAG embeddings are configured separately under `"Embeddings"` — the default (`hashing`) is a
deterministic offline embedding so the whole stack runs without any API key (e.g. with Ollama).
For real semantic retrieval switch to `openai` or `ollama` embeddings (see `docs/rag.md`).

### Run the tests and evals

```bash
cd backend
dotnet test                                                  # unit tests, no API key needed

# Evals hit a real model — set a key first
dotnet run --project src/AIFramework.Evals -- src/AIFramework.Evals/datasets/sample.jsonl contains
```

## Repository layout

```
├── backend/
│   ├── src/
│   │   ├── AIFramework.Core/      # the framework: Llm, Agents, Prompts, Rag, Evals, Observability
│   │   ├── AIFramework.Api/       # ASP.NET minimal API (composition root + HTTP contract)
│   │   └── AIFramework.Evals/     # console eval runner
│   └── tests/AIFramework.Core.Tests/
├── frontend/                      # React + Vite + TypeScript UI
├── prompts/                       # versioned prompt templates (shared by API and evals)
└── docs/                          # start with docs/getting-started.md
```

## Where to start reading

1. `docs/getting-started.md` — run it, then follow a request through the stack.
2. `docs/architecture.md` — why the layers are shaped this way.
3. `backend/src/AIFramework.Core/Agents/Agent.cs` — the agent loop; everything agentic builds on it.
4. `docs/prompt-engineering.md`, `docs/rag.md`, `docs/evals.md` — one topic each.

## Design principles

- **One seam per vendor decision.** `ILlmProvider`, `IEmbeddingProvider`, and `IVectorStore` are
  the only places vendors appear. Everything above them is yours and portable.
- **Readable over clever.** Each building block is implemented in the simplest correct way and is
  meant to be read. Production swaps (pgvector, OpenTelemetry, a queue for ingestion) keep the
  same interfaces.
- **Testable without keys.** The `FakeLlmProvider` and hashing embeddings make agents, RAG, and
  evals run deterministically in CI.
- **Prompts are code.** They live in files, carry versions, get reviewed in PRs, and are guarded
  by evals.
