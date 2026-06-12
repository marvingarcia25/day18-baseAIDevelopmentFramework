import { useState } from "react";
import { askRag, ingestDocument } from "../api/client";
import type { RagAnswer } from "../types";

/** Ingest documents into the vector store, then ask grounded questions with citations. */
export default function RagPanel() {
  const [documentId, setDocumentId] = useState("handbook");
  const [documentText, setDocumentText] = useState(
    "Vacation policy: full-time employees accrue 20 days of paid vacation per year. " +
      "Unused days roll over, capped at 10.\n\n" +
      "Remote work: employees may work remotely up to 3 days per week with manager approval.",
  );
  const [ingestStatus, setIngestStatus] = useState<string | null>(null);

  const [question, setQuestion] = useState("How many vacation days do I get?");
  const [answer, setAnswer] = useState<RagAnswer | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function ingest() {
    setError(null);
    try {
      const result = await ingestDocument(documentId, documentText);
      setIngestStatus(`Indexed "${documentId}" as ${result.chunks} chunk(s).`);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function ask() {
    setBusy(true);
    setError(null);
    setAnswer(null);
    try {
      setAnswer(await askRag(question));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section>
      <h2>1 — Ingest a document</h2>
      <input
        value={documentId}
        onChange={(e) => setDocumentId(e.target.value)}
        placeholder="document id"
      />
      <textarea
        className="tall"
        value={documentText}
        onChange={(e) => setDocumentText(e.target.value)}
      />
      <button onClick={() => void ingest()} disabled={!documentId.trim() || !documentText.trim()}>
        Ingest
      </button>
      {ingestStatus && <p className="hint">{ingestStatus}</p>}

      <h2>2 — Ask a grounded question</h2>
      <div className="composer">
        <textarea value={question} onChange={(e) => setQuestion(e.target.value)} />
        <button onClick={() => void ask()} disabled={busy || !question.trim()}>
          {busy ? "Thinking…" : "Ask"}
        </button>
      </div>
      {error && <p className="error">{error}</p>}
      {answer && (
        <div className="steps">
          <div className="step answer">
            <span className="badge">answer</span>
            <pre>{answer.answer}</pre>
          </div>
          {answer.sources.map((source) => (
            <div key={source.reference} className="step tool">
              <span className="badge">
                [{source.reference}] {source.documentId} · score {source.score.toFixed(3)}
              </span>
              <pre>{source.excerpt}</pre>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
