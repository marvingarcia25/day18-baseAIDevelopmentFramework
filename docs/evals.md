# Evals

LLMs are non-deterministic and prompts interact in non-obvious ways, so "I tried three examples
and it looked fine" is how regressions ship. Evals are unit tests for model behavior: a dataset of
cases, a grader, and a pass rate you can compare across prompts, models, and time.

## Running them

```bash
cd backend
dotnet run --project src/AIFramework.Evals -- src/AIFramework.Evals/datasets/sample.jsonl contains
# args: <dataset.jsonl> [contains|exact|regex|judge] [model-override]
# env:  LLM_PROVIDER / LLM_MODEL select the provider (same as the API)
```

The runner prints per-case results, writes a `*.results.json` report next to the dataset, and
exits non-zero when any case fails — wire it into CI as-is.

Datasets are JSONL, one case per line:

```json
{"id": "capital-france", "input": "What is the capital of France? Answer with just the city name.", "expected": "Paris", "systemPrompt": "optional"}
```

## Choosing a grader

Work down this list; stop at the first one that fits. Deterministic graders are free, instant,
and never wrong about what they measure.

| Grader | Use when | Watch out |
|---|---|---|
| `exact` | Closed-form answers (labels, numbers) | Phrase the prompt to force a bare answer ("Answer with just the city name") |
| `contains` | The output must mention a key fact | Passes even if surrounded by nonsense |
| `regex` | Format checks (JSON shape, date formats) | Validates format, not truth |
| `judge` (LLM-as-judge) | Open-ended outputs: summaries, explanations, tone | Costs tokens; has biases — see below |

**LLM-as-judge caveats** (implemented in `LlmJudgeGrader`): judges favor verbose answers, favor
their own model family, and drift with judge-model upgrades. Mitigations: give the judge a
reference answer to compare against (this harness does), use a stronger/different model than the
one under test, spot-check its grades by hand, and fail safe when its output doesn't parse (this
harness scores unparseable grades as 0).

## Building a dataset that earns its keep

- **Start from real failures.** Every bug you fix by editing a prompt becomes a case — that's the
  regression suite writing itself.
- **Cover the edges**: empty input, adversarial input ("ignore previous instructions"),
  questions whose correct answer is "I don't know", inputs in the wrong language.
- **20 good cases beat 500 lazy ones**, but track pass *rate*, not individual flakes — a case
  that flickers between runs is telling you the behavior is genuinely unstable.
- **Keep datasets per feature** (one for the classifier, one for the summarizer) so a change is
  judged by the eval that covers it.

## The workflow this enables

```
change prompt/model → run evals → compare pass rate to the last report → ship or iterate
```

Concretely: prompts in `/prompts` carry versions, so you can run the same dataset against
`Get("summarizer", 1)` and `Get("summarizer", 2)` and let the numbers decide. Comparing two
*models* is the third CLI argument (`... contains claude-haiku-4-5`) — useful for "is the cheap
model good enough for this route?" decisions, which is one of the biggest cost levers you have.
