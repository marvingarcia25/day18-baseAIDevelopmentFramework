import { useEffect, useState } from "react";
import { getUsage } from "../api/client";
import type { UsageReport } from "../types";

/** Cost and token telemetry recorded by the backend's InstrumentedLlmProvider. */
export default function UsagePanel() {
  const [report, setReport] = useState<UsageReport | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    try {
      setReport(await getUsage());
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  return (
    <section>
      <button onClick={() => void refresh()}>Refresh</button>
      {error && <p className="error">{error}</p>}
      {report && (
        <>
          <p className="hint">
            {report.totalCalls} calls · {report.totalUsage.inputTokens} tokens in /{" "}
            {report.totalUsage.outputTokens} out · est. ${report.totalEstimatedCostUsd.toFixed(4)}
          </p>
          <table>
            <thead>
              <tr>
                <th>Time</th>
                <th>Provider</th>
                <th>Model</th>
                <th>In</th>
                <th>Out</th>
                <th>Cost</th>
                <th>ms</th>
              </tr>
            </thead>
            <tbody>
              {report.calls.map((call, i) => (
                <tr key={i}>
                  <td>{new Date(call.timestamp).toLocaleTimeString()}</td>
                  <td>{call.provider}</td>
                  <td>{call.model}</td>
                  <td>{call.usage.inputTokens}</td>
                  <td>{call.usage.outputTokens}</td>
                  <td>${call.estimatedCostUsd.toFixed(4)}</td>
                  <td>{Math.round(call.durationMs)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </section>
  );
}
