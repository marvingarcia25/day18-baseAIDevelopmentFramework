# Prompt Engineering

Prompts are the highest-leverage, lowest-cost thing you can change in an AI system. This framework
treats them as versioned, reviewable, testable artifacts — not string literals scattered in code.

## How prompts work here

Prompts live in `/prompts` as Markdown files with a frontmatter header:

```markdown
---
name: summarizer
version: 1
description: Summarizes a document into a fixed number of bullet points.
---
Summarize the following document in at most {{max_bullets}} bullet points.
...
Document:
{{document}}
```

- `PromptRegistry` loads the directory; `Get("summarizer")` returns the latest version,
  `Get("summarizer", version: 1)` pins one.
- `PromptTemplate.Render(...)` substitutes `{{variables}}` and **throws if one is missing** —
  a silently empty placeholder is the classic invisible prompt bug.
- To change a prompt, add a new file with a bumped `version` (e.g. `summarizer.v2.md`), run the
  evals against both, and delete the old one when you're confident. The PR diff shows exactly
  what changed about your system's behavior.

## Writing good prompts — the rules that pay rent

1. **Say what to do, not just what not to do.** "Answer in one paragraph of plain prose" beats
   "don't use lists, don't be long-winded."
2. **Structure beats prose.** Separate instructions / context / input with headings or tags.
   Put long documents *before* the question, and instructions where they can't be confused with
   data (see the system-prompt note below).
3. **Constrain the output format explicitly** — and show the shape: "Respond with ONLY a JSON
   object: {...}". For classification, enumerate the allowed labels and say "exactly one".
4. **Give examples for anything subtle** (few-shot). Two or three input→output pairs communicate
   tone, granularity, and edge handling far better than adjectives. Make the examples cover the
   *tricky* cases, not the easy ones.
5. **Tell it what to do when it can't comply**: "If the document doesn't contain the answer, say
   so" — otherwise you're asking for fabrication.
6. **Give the reason, not just the request.** Models do better when they know what the output is
   *for* ("this summary will be read by an on-call engineer during an incident").
7. **Tune by reading failures, not by piling on rules.** Each instruction you add fights every
   other instruction. When the model misbehaves, find the smallest edit that fixes the actual
   failure mode you observed — then lock the fix in with an eval case.

## System prompt vs. user message

- The **system prompt** sets durable behavior: role, rules, output contract, tool guidance. It's a
  trusted channel — keep *untrusted input out of it* (a user's document pasted into the system
  prompt can inject instructions).
- The **user message** carries the task and the data. In this codebase, `ChatRequest.SystemPrompt`
  is a separate field because providers place it differently on the wire.

## Prompts and caching

Providers cache prompt *prefixes*. Keep stable content (system prompt, examples) first and
byte-identical across requests; put volatile content (timestamps, user data) last. Interpolating
`DateTime.Now` into a system prompt silently disables caching for everything after it.

## The workflow

```
observe failure → smallest prompt edit → bump version → run evals → review diff → ship
```

The evals half of this loop is what separates prompt engineering from prompt guessing — see
[evals.md](evals.md).
