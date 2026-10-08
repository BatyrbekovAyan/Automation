# Eval v9r2-before-price-x8 — v1.2 (2026-10-08: V9-r2 ap_price_agrees_analogues — the price list agrees with the catalog, analogue rows in the same chunk; harness h5)

prompts: `5b0a222d` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 2 scenarios / 16 runs · 54 s
pass 1 · flaky 0 · fail 1 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 138510 in (cached 135168) + 1331 out (reasoning 0) ≈ $0.0186 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3335 ms, p90 3829 ms, max 4097 ms
simulated price-list search: raw documents (retrieve-as-tool at 5b0a222d)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_source_conflict | auto_parts | auto | 0.00 (8×) | no_fresh_price_pick |
| ap_price_agrees_analogues | auto_parts | auto | 1.00 (8×) |  |

## By check

| check | pass/total |
|---|---|
| mentions:3200 | 8/8 |
| no_conflict_claim | 8/8 |
| no_fresh_price_pick | 0/8 |
| no_markdown | 16/16 |
| no_price_caveat | 8/8 |
