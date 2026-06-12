// Mirrors the backend HTTP contract (backend/src/AIFramework.Api/Endpoints.cs).

export interface ChatMessage {
  role: "user" | "assistant";
  content: string;
}

export interface TokenUsage {
  inputTokens: number;
  outputTokens: number;
}

export interface ChatDone {
  model: string;
  finishReason: string;
  usage: TokenUsage;
}

export interface AgentStep {
  type: "tool" | "answer";
  tool: string | null;
  arguments: string | null;
  result: string;
}

export interface AgentResponse {
  answer: string;
  usage: TokenUsage;
  steps: AgentStep[];
}

export interface RagSource {
  reference: number;
  documentId: string;
  score: number;
  excerpt: string;
}

export interface RagAnswer {
  answer: string;
  usage: TokenUsage;
  sources: RagSource[];
}

export interface UsageRecord {
  timestamp: string;
  provider: string;
  model: string;
  usage: TokenUsage;
  estimatedCostUsd: number;
  durationMs: number;
  streamed: boolean;
}

export interface UsageReport {
  totalCalls: number;
  totalUsage: TokenUsage;
  totalEstimatedCostUsd: number;
  calls: UsageRecord[];
}

export interface HealthInfo {
  status: string;
  provider: string;
  model: string;
}
