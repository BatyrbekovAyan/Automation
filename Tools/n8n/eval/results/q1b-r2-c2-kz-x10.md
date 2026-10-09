# Eval q1b-r2-c2-kz-x10 — v1.3 (2026-10-09: Q1 — the owner's agreement in the chat history: a6_owner_agreement_* ×3 verticals, q1p_* paraphrases (other words, facts, verticals), q1c_* negative controls (no agreement / the CLIENT names the time) with not_mentions:15:00 against echoing the client's own time as agreed; harness h6)

prompts: `1fe4f97b2b455acda33fa06834d14090304a5dfd` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 2 scenarios / 20 runs · 51 s
pass 0 · flaky 1 · fail 1 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 134549 in (cached 126720) + 1588 out (reasoning 0) ≈ $0.0225 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 2671 ms, p90 3392 ms, max 3406 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 1fe4f97b2b455acda33fa06834d14090304a5dfd)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ks_kz_availability | kaspi_seller | auto | 0.60 (10×) | lang_kz |
| pr_kz_battery | phone_repair | auto | 0.00 (10×) | lang_kz, mentions:15000 |

## By check

| check | pass/total |
|---|---|
| lang_kz | 14/20 |
| mentions:15000 | 1/10 |
| no_stock_guarantee | 10/10 |
| numbers_grounded | 10/10 |
