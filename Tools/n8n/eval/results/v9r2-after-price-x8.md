# Eval v9r2-after-price-x8 — v1.2 (2026-10-08: V9-r2 ap_price_agrees_analogues — the price list agrees with the catalog, analogue rows in the same chunk; harness h5)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 2 scenarios / 16 runs · 56 s
pass 2 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 138928 in (cached 135168) + 1453 out (reasoning 0) ≈ $0.0195 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3514 ms, p90 3774 ms, max 4087 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at the ref)

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
