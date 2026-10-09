# Eval q1b-r2-c1-kzo-x10 — v1.3 (2026-10-09: Q1 — the owner's agreement in the chat history: a6_owner_agreement_* ×3 verticals, q1p_* paraphrases (other words, facts, verticals), q1c_* negative controls (no agreement / the CLIENT names the time) with not_mentions:15:00 against echoing the client's own time as agreed; harness h6)

prompts: `ae8305962856ba354852085911f1867f296da7dd` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 3 scenarios / 30 runs · 109 s
pass 0 · flaky 3 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 230670 in (cached 211584) + 2765 out (reasoning 0) ≈ $0.0426 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3785 ms, p90 4440 ms, max 5780 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at ae8305962856ba354852085911f1867f296da7dd)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_sku_exact_kz | auto_parts | auto | 0.50 (10×) | lang_kz |
| ws_kz_min_order | wholesale | auto | 0.70 (10×) | lang_kz |
| ed_kz_parent | education | auto | 0.90 (10×) | lang_kz |

## By check

| check | pass/total |
|---|---|
| lang_kz | 21/30 |
| mentions:50 | 10/10 |
| mentions:55000 | 10/10 |
| no_invented_sku | 10/10 |
| numbers_grounded | 30/30 |
