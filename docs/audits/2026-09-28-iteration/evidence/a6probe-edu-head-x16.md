# Eval a6probe-edu-head-x16 — v1.2 (2026-10-08: V9-r2 ap_price_agrees_analogues — the price list agrees with the catalog, analogue rows in the same chunk; harness h5) + a6 probe (scratch, 2026-10-08)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 1 scenarios / 16 runs · 39 s
pass 0 · flaky 1 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 98272 in (cached 89088) + 991 out (reasoning 0) ≈ $0.018 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 2857 ms, p90 3579 ms, max 3661 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| a6_owner_agreement_education | education | auto | 0.19 (16×) | mentions:15, mentions:Марин |

## By check

| check | pass/total |
|---|---|
| mentions:15 | 3/16 |
| mentions:Марин | 3/16 |
