# Eval a6probe-head-x8 — v1.2 (2026-10-08: V9-r2 ap_price_agrees_analogues — the price list agrees with the catalog, analogue rows in the same chunk; harness h5) + a6 probe (scratch, 2026-10-08)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 3 scenarios / 24 runs · 44 s
pass 1 · flaky 1 · fail 1 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 120007 in (cached 96512) + 1078 out (reasoning 0) ≈ $0.0297 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1534 ms, p90 3241 ms, max 3739 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| a6_owner_agreement_flowers | flowers | auto | 1.00 (8×) |  |
| a6_owner_agreement_education | education | auto | 0.38 (8×) | mentions:15, mentions:Марин |
| a6_owner_agreement_phone_repair | phone_repair | auto | 0.00 (8×) | mentions:15, mentions:Марин |

## By check

| check | pass/total |
|---|---|
| mentions:15 | 11/24 |
| mentions:Марин | 11/24 |
