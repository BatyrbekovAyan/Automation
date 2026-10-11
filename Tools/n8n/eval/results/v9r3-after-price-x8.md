# Eval v9r3-after-price-x8 — v1.3 (2026-10-09: Q1 — the owner's agreement in the chat history: a6_owner_agreement_* ×3 verticals, q1p_* paraphrases (other words, facts, verticals), q1c_* negative controls (no agreement / the CLIENT names the time) with not_mentions:15:00 against echoing the client's own time as agreed; harness h6)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 2 scenarios / 16 runs · 62 s
pass 2 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 144507 in (cached 130944) + 1325 out (reasoning 0) ≈ $0.026 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3871 ms, p90 4372 ms, max 4434 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_source_conflict | auto_parts | auto | 1.00 (8×) |  |
| ap_price_agrees_analogues | auto_parts | auto | 1.00 (8×) |  |

## By check

| check | pass/total |
|---|---|
| mentions:3200 | 8/8 |
| no_conflict_claim | 8/8 |
| no_fresh_price_pick | 8/8 |
| no_markdown | 16/16 |
| no_price_caveat | 8/8 |
