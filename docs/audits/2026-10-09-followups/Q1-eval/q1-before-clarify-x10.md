# Eval q1-before-clarify-x10 — v1.2 (2026-10-08: V9-r2 ap_price_agrees_analogues — the price list agrees with the catalog, analogue rows in the same chunk; harness h5)

prompts: `d08c345d` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 5 scenarios / 50 runs · 162 s
pass 3 · flaky 2 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 365024 in (cached 337792) + 3497 out (reasoning 0) ≈ $0.0615 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3077 ms, p90 3766 ms, max 17147 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at d08c345d)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 (10×) |  |
| fl_recommend | flowers | auto | 1.00 (10×) |  |
| ed_price_no_details | education | auto | 0.50 (10×) | asks_for_whom_or_goal |
| pr_price_no_model | phone_repair | auto | 0.60 (10×) | asks_model, no_price |
| pr_model_not_in_list | phone_repair | auto | 1.00 (10×) |  |

## By check

| check | pass/total |
|---|---|
| asks_car | 10/10 |
| asks_for_whom_or_goal | 5/10 |
| asks_model | 9/10 |
| asks_occasion_or_budget_or_offers | 10/10 |
| lang_ru | 10/10 |
| max_questions:2 | 10/10 |
| no_closing_offer | 10/10 |
| no_markdown | 20/20 |
| no_price | 26/30 |
| numbers_grounded | 10/10 |
| offers_handoff_or_clarifies | 10/10 |
