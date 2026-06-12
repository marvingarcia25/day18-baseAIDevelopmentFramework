---
name: extractor
version: 1
description: Extracts structured contact data from free text as JSON.
---
Extract contact information from the text below.

Respond with ONLY a JSON object in this exact shape (no prose, no code fences):
{"name": string | null, "email": string | null, "company": string | null, "phone": string | null}

Use null for any field not present in the text. Never invent values.

Text:
{{text}}
