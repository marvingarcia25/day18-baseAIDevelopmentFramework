import { useState } from "react";
import { runAgent } from "../api/client";
import type { AgentResponse } from "../types";

/**
 * Runs the backend agent and shows every step: which tools it called,
 * with what arguments, and what they returned. Seeing the loop is the point.
 */
export default function AgentPanel() {
  const [input, setInput] = useState("What is (1234 * 5678) + 42? And what day is it today?");
  const [result, setResult] = useState<AgentResponse | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run() {
    setBusy(true);
    setError(null);
    setResult(null);
    try {
      setResult(await runAgent(input));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section>
      <p className="hint">
        The agent has two tools: <code>calculator</code> and <code>get_current_time</code>. Ask
        something that needs them and watch the loop run.
      </p>
      <div className="composer">
        <textarea value={input} onChange={(e) => setInput(e.target.value)} />
        <button onClick={() => void run()} disabled={busy || !input.trim()}>
          {busy ? "Running…" : "Run agent"}
        </button>
      </div>
      {error && <p className="error">{error}</p>}
      {result && (
        <div className="steps">
          {result.steps.map((step, i) =>
            step.type === "tool" ? (
              <div key={i} className="step tool">
                <span className="badge">tool call</span>
                <code>
                  {step.tool}({step.arguments})
                </code>
                <pre>→ {step.result}</pre>
              </div>
            ) : (
              <div key={i} className="step answer">
                <span className="badge">final answer</span>
                <pre>{step.result}</pre>
              </div>
            ),
          )}
          <p className="hint">
            Tokens: {result.usage.inputTokens} in / {result.usage.outputTokens} out
          </p>
        </div>
      )}
    </section>
  );
}
