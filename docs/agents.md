# Agents and Tool Use

An "agent" is a model in a loop with tools. That's it. The loop in
`backend/src/AIFramework.Core/Agents/Agent.cs`:

```
1. Send conversation + tool definitions to the model.
2. Model answers with text            → done, return it.
3. Model answers with tool calls      → execute them, append the results, go to 1.
   (Plus a MaxIterations safety valve.)
```

Two mechanics trip everyone up the first time:

- **The model never executes anything.** It emits a structured request ("call `calculator` with
  `{expression: "2+3"}`"); *your code* runs the tool and sends the result back as a message. You
  own the security boundary.
- **The assistant's tool-call turn must be echoed back** before the tool results, and every tool
  call id needs a matching result. Providers reject the conversation otherwise. The `Agent` class
  handles this ordering.

## Writing good tools

The model decides when to call a tool **purely from its name, description, and schema** — those
three strings are a prompt. Rules of thumb, all visible in `Tools/CalculatorTool.cs`:

1. **Describe when to use it, not just what it does.** "Call this for any calculation instead of
   computing it yourself" measurably raises correct-usage rates.
2. **Validate inputs like they're hostile** — the model can produce anything. The calculator
   whitelists characters before evaluating.
3. **Return errors as readable text instead of throwing.** The model reads `"Error: unknown
   tool"` and self-corrects; an exception just kills the run. The `Agent` class converts
   uncaught tool exceptions into error text for the same reason.
4. **Keep results compact.** Tool output goes into the context window and you pay tokens for it.
   Return what the model needs, not a full database dump.
5. **Prefer a few well-described tools over many vague ones.** Tool selection accuracy drops as
   the toolset grows.

## Designing the system prompt for an agent

Tell the agent how to behave *around* tools — when to use them, when to answer directly, and what
to do when tools fail. Avoid "you MUST always use X": modern models follow instructions literally,
and aggressive language causes tool calls on requests that don't need them.

## Safety valves you should keep

- **`MaxIterations`** (here: 10) — a confused model can loop forever; each loop costs money.
- **Confirmation for irreversible actions.** Anything that sends, deletes, or pays should be
  gated on a human or a policy check in *your* tool code, not on trusting the model.
- **Budget awareness** — `AgentResult.TotalUsage` aggregates tokens across the loop; alert on
  outliers (the Usage tab shows per-call records).

## Where to go from here

- **More tools**: implement `ITool`, register it in `Program.cs`. Start with read-only tools
  (search, lookup) before write tools.
- **Streaming agents**: stream the final answer while running tools non-streamed (the common UX).
- **Multi-agent**: an orchestrating agent whose "tool" runs another `Agent`. Useful when subtasks
  are independent and parallel — not as a default architecture.
- **Memory**: persist distilled facts between runs (a file or table the agent reads/writes via
  tools) instead of replaying ever-growing transcripts.
