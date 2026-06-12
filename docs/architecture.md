# Architecture

```
┌────────────────────────────────────────────────────────────┐
│  frontend/ (React + TS)                                    │
│  Chat (SSE) · Agent steps · RAG playground · Usage         │
└──────────────────────────┬─────────────────────────────────┘
                           │ HTTP + Server-Sent Events
┌──────────────────────────▼─────────────────────────────────┐
│  AIFramework.Api (ASP.NET minimal API)                     │
│  DTOs + endpoints; composition root wires everything up    │
└──────────────────────────┬─────────────────────────────────┘
┌──────────────────────────▼─────────────────────────────────┐
│  AIFramework.Core                                          │
│                                                            │
│   Agents ──► uses ──► Llm ◄── uses ── Rag ◄── Evals        │
│   (loop, tools)        │       (chunk, embed, store)       │
│                        │                                   │
│   Prompts (files)      │      Observability (decorator)    │
│                        ▼                                   │
│            ILlmProvider  /  IEmbeddingProvider             │
│           ┌──────────────┬──────────────────┐              │
│           │ Anthropic    │ OpenAI-compatible│              │
│           │ (official    │ (raw HTTP: OpenAI│              │
│           │  .NET SDK)   │  Ollama, vLLM…)  │              │
│           └──────────────┴──────────────────┘              │
└────────────────────────────────────────────────────────────┘
```

## The one decision that matters: the provider seam

`ILlmProvider` is deliberately small — complete, stream, nothing else:

```csharp
Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct);
IAsyncEnumerable<StreamEvent> StreamAsync(ChatRequest request, CancellationToken ct);
```

Everything above it (agents, RAG, evals, HTTP API, UI) is written against this interface and has
no idea which vendor is in use. The payoffs:

- **Swap vendors with config**, not a rewrite. `appsettings.json` decides.
- **Test everything offline.** `FakeLlmProvider` in the test project scripts responses; the agent
  loop, RAG pipeline, graders, and usage tracking are all unit-tested with zero network calls.
- **Cross-cut without touching call sites.** `InstrumentedLlmProvider` wraps any provider to
  record tokens/cost/latency. Retry, caching, or rate limiting would be decorators too.

The cost: the request/response types are a common denominator. Provider-specific features
(Anthropic's adaptive thinking, prompt caching, structured outputs) either get a sensible default
inside the adapter or aren't exposed. When you need one deeply, extend `ChatRequest` or talk to
that SDK directly at the edge — don't leak vendor types upward.

The same pattern repeats at the data layer: `IEmbeddingProvider` (who makes vectors) and
`IVectorStore` (where they live and how they're searched) keep RAG portable from "in-memory demo"
to "pgvector in production" without touching `RagPipeline`.

## Why the implementations are deliberately simple

This is a learning-first codebase. Each module is the smallest correct version of the real thing,
with the production upgrade path documented:

| Module | Here | In production |
|---|---|---|
| Vector store | In-memory, brute-force cosine | pgvector / Qdrant / Azure AI Search behind the same interface |
| Embeddings | Offline hashing (for dev) or API embeddings | A real embedding model, always |
| Usage tracking | In-memory list | OpenTelemetry metrics + your dashboard |
| Prompt registry | Files in the repo | Often still files in the repo (that's a feature) |
| Eval runner | Sequential console app | Parallel runs, baselines, dashboards — same graders |
| Agent | Single agent, sequential tools | Parallel tool calls, sub-agents — same loop at the core |

## Conventions

- **DTOs live at the edge.** `AIFramework.Api/Endpoints.cs` owns the HTTP contract; core types
  never serialize directly to clients. This lets the core evolve without breaking the frontend.
- **The composition root is `Program.cs`.** It is the only place that calls the factories. If a
  class needs a provider, it receives one — nothing constructs vendors ad hoc.
- **Errors flow to the model as text** inside agent tool execution (so it can self-correct), and
  to humans as exceptions everywhere else.
- **Token budgets are explicit.** Every request carries `MaxTokens`; every response reports usage.
