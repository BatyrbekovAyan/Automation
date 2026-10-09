# Eval q1b-r2-pre-kzo-x10 — v1.3 (2026-10-09: Q1 — the owner's agreement in the chat history: a6_owner_agreement_* ×3 verticals, q1p_* paraphrases (other words, facts, verticals), q1c_* negative controls (no agreement / the CLIENT names the time) with not_mentions:15:00 against echoing the client's own time as agreed; harness h6)

prompts: `c468306a` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 3 scenarios / 30 runs · 108 s
pass 0 · flaky 3 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 212995 in (cached 199552) + 2696 out (reasoning 0) ≈ $0.0372 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3825 ms, p90 4672 ms, max 4950 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at c468306a)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_sku_exact_kz | auto_parts | auto | 0.70 (10×) | lang_kz |
| ws_kz_min_order | wholesale | auto | 0.90 (10×) | lang_kz |
| ed_kz_parent | education | auto | 0.90 (10×) | lang_kz |

## By check

| check | pass/total |
|---|---|
| lang_kz | 25/30 |
| mentions:50 | 10/10 |
| mentions:55000 | 10/10 |
| no_invented_sku | 10/10 |
| numbers_grounded | 30/30 |
