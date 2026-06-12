import type {
  AgentResponse,
  ChatDone,
  ChatMessage,
  HealthInfo,
  RagAnswer,
  UsageReport,
} from "../types";

// In dev, Vite proxies /api to the backend (see vite.config.ts).
// Set VITE_API_BASE for deployments where the API lives elsewhere.
const API_BASE = import.meta.env.VITE_API_BASE ?? "";

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`${path} failed (${response.status}): ${await response.text()}`);
  }
  return (await response.json()) as T;
}

export function getHealth(): Promise<HealthInfo> {
  return fetch(`${API_BASE}/api/health`).then((r) => r.json());
}

export function getUsage(): Promise<UsageReport> {
  return fetch(`${API_BASE}/api/usage`).then((r) => r.json());
}

export function runAgent(message: string): Promise<AgentResponse> {
  return postJson("/api/agent", { message });
}

export function ingestDocument(documentId: string, text: string): Promise<{ chunks: number }> {
  return postJson("/api/rag/documents", { documentId, text });
}

export function askRag(question: string): Promise<RagAnswer> {
  return postJson("/api/rag/ask", { question });
}

/**
 * Streams a chat completion. The backend sends Server-Sent Events:
 *   event: delta  data: {"text": "..."}     — repeated
 *   event: done   data: {model, finishReason, usage}
 *
 * fetch + ReadableStream is used instead of EventSource because EventSource
 * cannot send POST bodies.
 */
export async function streamChat(
  messages: ChatMessage[],
  onDelta: (text: string) => void,
  signal?: AbortSignal,
): Promise<ChatDone | null> {
  const response = await fetch(`${API_BASE}/api/chat/stream`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ messages }),
    signal,
  });
  if (!response.ok || !response.body) {
    throw new Error(`chat stream failed (${response.status}): ${await response.text()}`);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let done: ChatDone | null = null;

  for (;;) {
    const { value, done: streamEnded } = await reader.read();
    if (streamEnded) break;
    buffer += decoder.decode(value, { stream: true });

    // SSE messages are separated by a blank line; the last element may be incomplete.
    const messagesRaw = buffer.split("\n\n");
    buffer = messagesRaw.pop() ?? "";

    for (const raw of messagesRaw) {
      let eventName = "message";
      let data = "";
      for (const line of raw.split("\n")) {
        if (line.startsWith("event: ")) eventName = line.slice(7);
        else if (line.startsWith("data: ")) data += line.slice(6);
      }
      if (!data) continue;
      if (eventName === "delta") onDelta(JSON.parse(data).text as string);
      else if (eventName === "done") done = JSON.parse(data) as ChatDone;
    }
  }
  return done;
}
