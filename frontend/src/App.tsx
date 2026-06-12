import { useEffect, useState } from "react";
import { getHealth } from "./api/client";
import type { HealthInfo } from "./types";
import Chat from "./components/Chat";
import AgentPanel from "./components/AgentPanel";
import RagPanel from "./components/RagPanel";
import UsagePanel from "./components/UsagePanel";

const TABS = ["Chat", "Agent", "RAG", "Usage"] as const;
type Tab = (typeof TABS)[number];

export default function App() {
  const [tab, setTab] = useState<Tab>("Chat");
  const [health, setHealth] = useState<HealthInfo | null>(null);

  useEffect(() => {
    getHealth()
      .then(setHealth)
      .catch(() => setHealth(null));
  }, []);

  return (
    <div className="app">
      <header>
        <h1>AI Development Framework</h1>
        <span className="health">
          {health ? `${health.provider} · ${health.model}` : "backend offline"}
        </span>
      </header>

      <nav>
        {TABS.map((name) => (
          <button
            key={name}
            className={tab === name ? "active" : ""}
            onClick={() => setTab(name)}
          >
            {name}
          </button>
        ))}
      </nav>

      <main>
        {tab === "Chat" && <Chat />}
        {tab === "Agent" && <AgentPanel />}
        {tab === "RAG" && <RagPanel />}
        {tab === "Usage" && <UsagePanel />}
      </main>
    </div>
  );
}
