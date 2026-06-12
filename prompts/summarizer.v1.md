---
name: summarizer
version: 1
description: Summarizes a document into a fixed number of bullet points.
---
Summarize the following document in at most {{max_bullets}} bullet points.

Rules:
- Each bullet is one sentence, leading with the key fact.
- Preserve concrete numbers, names, and dates; drop opinions and filler.
- If the document contains contradictions, call them out in a final bullet.

Document:
{{document}}
