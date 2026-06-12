# Getting Started

This guide takes you from clone to understanding, in that order: run the stack, then follow one
request through every layer.

## 1. Run it

```bash
# Backend (terminal 1) — defaults to Anthropic
export ANTHROPIC_API_KEY=sk-ant-...
cd backend
dotnet run --project src/AIFramework.Api

# Frontend (terminal 2)
cd frontend
npm install
npm run dev
```

Open http://localhost:5173.

No API key? Install [Ollama](https://ollama.com), pull a model (`ollama pull llama3.1`), and set
`"Llm": { "Provider": "ollama" }` in `backend/src/AIFramework.Api/appsettings.json`. The default
RAG embedding is already offline, so the entire stack then runs locally for free.

## 2. Follow a chat message through the stack

Type "hello" in the Chat tab and trace it:

1. **`frontend/src/components/Chat.tsx`** keeps the conversation in React state and calls
   `streamChat()`. Note that it sends the *entire* history every turn — LLM APIs are stateless;
   "memory" is just resending messages.
2. **`frontend/src/api/client.ts` → `streamChat`** POSTs to `/api/chat/stream` and parses the
   Server-Sent Events with `fetch` + `ReadableStream` (EventSource can't POST).
3. **`backend/src/AIFramework.Api/Endpoints.cs`** converts the DTOs to core types and forwards to
   `ILlmProvider.StreamAsync`, writing each `TextDelta` back as an SSE event.
4. **`backend/src/AIFramework.Core/Llm/Providers/AnthropicProvider.cs`** translates the
   provider-agnostic request into the Anthropic SDK's types and maps the streamed events back.
   This is the *only* file that knows Anthropic exists.
5. On the way out, **`InstrumentedLlmProvider`** records tokens, latency, and estimated cost —
   check the Usage tab.

## 3. Watch the agent loop

In the Agent tab, run the default question. The UI shows each loop iteration: the model asks for
`calculator`, gets `7006652` back, asks for `get_current_time`, then writes its final answer.

Now read `backend/src/AIFramework.Core/Agents/Agent.cs`. The whole "agent" is:

```
loop:
  response = model(messages + tool definitions)
  if no tool calls: return response.text
  run the tools, append results to messages
```

Every agent product you've seen is this loop plus better tools, prompts, and UI.

## 4. Ground the model with RAG

In the RAG tab, ingest the sample document and ask the sample question. Then read
`backend/src/AIFramework.Core/Rag/RagPipeline.cs` — ingest (chunk → embed → store) and ask
(embed → retrieve → grounded prompt → answer with citations) are ~40 lines each.

## 5. Make a change safely

1. Edit a prompt in `/prompts` (say, make the summarizer terser). Bump its `version`.
2. Add a case to `backend/src/AIFramework.Evals/datasets/sample.jsonl` that would catch a
   regression.
3. Run the evals: `dotnet run --project src/AIFramework.Evals -- src/AIFramework.Evals/datasets/sample.jsonl contains`

That edit → eval → ship cycle is the core workflow of AI engineering. The rest of the docs go
deeper: [architecture](architecture.md), [prompt engineering](prompt-engineering.md),
[agents](agents.md), [RAG](rag.md), [evals](evals.md).

## Common issues

| Symptom | Cause |
|---|---|
| `401` from the backend | API key env var not set in the shell that runs `dotnet run`. |
| Frontend says "backend offline" | API isn't running on :5080, or you changed `Urls` in appsettings. |
| RAG retrieves the wrong chunks | You're on the offline `hashing` embedding (word overlap only). Switch to real embeddings — see `docs/rag.md`. |
| Agent loops until MaxIterations | The model can't satisfy the request with the registered tools. Check the tool descriptions and the request. |
