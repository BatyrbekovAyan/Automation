# Eval a6probe-prod-x8 — v1.2 (2026-10-08: V9-r2 ap_price_agrees_analogues — the price list agrees with the catalog, analogue rows in the same chunk; harness h5) + a6 probe (scratch, 2026-10-08)

prompts: `f08546f1` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 3 scenarios / 24 runs · 36 s
pass 1 · flaky 1 · fail 1 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 89992 in (cached 74368) + 786 out (reasoning 0) ≈ $0.0208 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1493 ms, p90 1898 ms, max 2170 ms
simulated price-list search: raw documents (retrieve-as-tool at f08546f1)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| a6_owner_agreement_flowers | flowers | auto | 1.00 (8×) |  |
| a6_owner_agreement_education | education | auto | 0.62 (8×) | mentions:15, mentions:Марин |
| a6_owner_agreement_phone_repair | phone_repair | auto | 0.00 (8×) | mentions:15, mentions:Марин |

## By check

| check | pass/total |
|---|---|
| mentions:15 | 13/24 |
| mentions:Марин | 13/24 |
