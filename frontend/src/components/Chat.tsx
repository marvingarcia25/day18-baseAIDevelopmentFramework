import { useRef, useState } from "react";
import { streamChat } from "../api/client";
import type { ChatMessage } from "../types";

/**
 * Streaming chat: tokens render as they arrive over SSE.
 * The full conversation is resent on every turn — LLM APIs are stateless.
 */
export default function Chat() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  async function send() {
    const content = input.trim();
    if (!content || busy) return;

    const history: ChatMessage[] = [...messages, { role: "user", content }];
    setMessages([...history, { role: "assistant", content: "" }]);
    setInput("");
    setBusy(true);
    setError(null);

    abortRef.current = new AbortController();
    try {
      await streamChat(
        history,
        (delta) =>
          setMessages((current) => {
            const next = [...current];
            const last = next[next.length - 1];
            next[next.length - 1] = { ...last, content: last.content + delta };
            return next;
          }),
        abortRef.current.signal,
      );
    } catch (e) {
      if ((e as Error).name !== "AbortError") setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section>
      <div className="messages">
        {messages.length === 0 && <p className="hint">Send a message to start a conversation.</p>}
        {messages.map((message, i) => (
          <div key={i} className={`message ${message.role}`}>
            <span className="role">{message.role}</span>
            <pre>{message.content || "…"}</pre>
          </div>
        ))}
      </div>
      {error && <p className="error">{error}</p>}
      <div className="composer">
        <textarea
          value={input}
          placeholder="Ask anything…"
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              void send();
            }
          }}
        />
        {busy ? (
          <button onClick={() => abortRef.current?.abort()}>Stop</button>
        ) : (
          <button onClick={() => void send()} disabled={!input.trim()}>
            Send
          </button>
        )}
      </div>
    </section>
  );
}
