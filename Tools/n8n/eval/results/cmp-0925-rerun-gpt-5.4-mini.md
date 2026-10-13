# Eval cmp-0925-rerun-gpt-5.4-mini — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 7 scenarios / 35 runs · 110 s
pass 2 · flaky 4 · fail 1 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 265451 in (cached 241280) + 3250 out (reasoning 0) ≈ $0.0508 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3142 ms, p90 3664 ms, max 3935 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 0.00 (5×) | no_price |
| ap_source_conflict | auto_parts | auto | 0.80 (5×) | no_fresh_price_pick |
| ws_discount_request | wholesale | auto | 1.00 (5×) |  |
| fl_absent_item | flowers | auto | 0.80 (5×) | offers_handoff_or_clarifies |
| pr_price_no_model | phone_repair | auto | 0.80 (5×) | no_price |
| pr_kz_battery | phone_repair | auto | 0.60 (5×) | lang_kz, mentions:15000 |
| gen_markdown_bait | wholesale | auto | 1.00 (5×) |  |

## By check

| check | pass/total |
|---|---|
| asks_car | 5/5 |
| asks_model | 5/5 |
| lang_kz | 4/5 |
| lang_ru | 5/5 |
| mentions:15000 | 4/5 |
| no_closing_offer | 5/5 |
| no_discount | 5/5 |
| no_fresh_price_pick | 4/5 |
| no_markdown | 15/15 |
| no_price | 4/10 |
| no_price_for:тюльпан | 5/5 |
| no_stock_denial | 5/5 |
| numbers_grounded | 10/10 |
| offers_handoff_or_clarifies | 9/10 |
