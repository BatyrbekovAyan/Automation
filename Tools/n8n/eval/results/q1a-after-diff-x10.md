# Eval q1a-after-diff-x10 — v1.3 (2026-10-09: Q1 — the owner's agreement in the chat history: a6_owner_agreement_* ×3 verticals, q1p_* paraphrases (other words, facts, verticals), q1c_* negative controls (no agreement / the CLIENT names the time) with not_mentions:15:00 against echoing the client's own time as agreed; harness h6)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 5 scenarios / 50 runs · 159 s
pass 0 · flaky 4 · fail 1 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 433506 in (cached 396416) + 4149 out (reasoning 0) ≈ $0.0762 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3146 ms, p90 3678 ms, max 3899 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| fl_absent_item | flowers | auto | 0.70 (10×) | offers_handoff_or_clarifies |
| ks_kz_availability | kaspi_seller | auto | 0.40 (10×) | lang_kz |
| ed_price_no_details | education | auto | 0.70 (10×) | asks_for_whom_or_goal |
| pr_price_no_model | phone_repair | auto | 0.80 (10×) | asks_model, no_price |
| pr_kz_battery | phone_repair | auto | 0.00 (10×) | lang_kz |

## By check

| check | pass/total |
|---|---|
| asks_for_whom_or_goal | 7/10 |
| asks_model | 9/10 |
| lang_kz | 4/20 |
| mentions:15000 | 10/10 |
| no_markdown | 10/10 |
| no_price | 8/10 |
| no_price_for:тюльпан | 10/10 |
| no_stock_denial | 10/10 |
| no_stock_guarantee | 10/10 |
| numbers_grounded | 10/10 |
| offers_handoff_or_clarifies | 7/10 |
