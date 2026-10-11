# Eval q1c-A-flowers-x12 — v1.3 (2026-10-09: Q1 — the owner's agreement in the chat history: a6_owner_agreement_* ×3 verticals, q1p_* paraphrases (other words, facts, verticals), q1c_* negative controls (no agreement / the CLIENT names the time) with not_mentions:15:00 against echoing the client's own time as agreed; harness h6)

prompts: `f02b6674` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 2 scenarios / 24 runs · 28 s
pass 1 · flaky 1 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 102780 in (cached 100864) + 527 out (reasoning 0) ≈ $0.0114 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1132 ms, p90 1393 ms, max 1680 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at f02b6674)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| a6_owner_agreement_flowers | flowers | auto | 1.00 (12×) |  |
| q1p_delivery_flowers | flowers | auto | 0.08 (12×) | mentions:Санжар |

## By check

| check | pass/total |
|---|---|
| mentions:15 | 12/12 |
| mentions:19 | 12/12 |
| mentions:Марин | 12/12 |
| mentions:Санжар | 1/12 |
