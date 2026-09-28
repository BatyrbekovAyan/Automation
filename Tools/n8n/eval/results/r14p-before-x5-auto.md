# Eval r14p-before-x5-auto — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `44d71f84` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 14 scenarios / 70 runs · 235 s
pass 6 · flaky 5 · fail 3 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 496414 in (cached 434432) + 6737 out (reasoning 0) ≈ $0.1094 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3218 ms, p90 3971 ms, max 7004 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 0.00 (5×) | no_price |
| ap_price_with_car | auto_parts | auto | 1.00 (5×) |  |
| ap_sku_exact_kz | auto_parts | auto | 0.40 (5×) | lang_kz |
| ap_source_conflict | auto_parts | auto | 0.00 (5×) | no_fresh_price_pick |
| ws_list_of_items | wholesale | auto | 1.00 (5×) |  |
| fl_absent_item | flowers | auto | 0.80 (5×) | offers_handoff_or_clarifies |
| ks_installment_terms | kaspi_seller | auto | 1.00 (5×) |  |
| ks_where_is_order | kaspi_seller | auto | 1.00 (5×) |  |
| ks_kz_availability | kaspi_seller | auto | 0.60 (5×) | lang_kz |
| ed_kz_parent | education | auto | 1.00 (5×) |  |
| pr_price_no_model | phone_repair | auto | 0.80 (5×) | no_price |
| pr_data_privacy | phone_repair | auto | 0.60 (5×) | no_data_access_claim |
| pr_kz_battery | phone_repair | auto | 0.00 (5×) | mentions:15000 |
| gen_owner_pref_vs_fact | auto_parts | auto | 1.00 (5×) |  |

## By check

| check | pass/total |
|---|---|
| asks_car | 5/5 |
| asks_model | 5/5 |
| asks_order_number_or_phone | 5/5 |
| lang_kz | 15/20 |
| lang_ru | 5/5 |
| marks_unknown_item | 5/5 |
| mentions:15000 | 0/5 |
| mentions:3200 | 5/5 |
| mentions:55000 | 5/5 |
| mentions_backup | 5/5 |
| mentions_kaspi | 5/5 |
| no_closing_offer | 5/5 |
| no_data_access_claim | 3/5 |
| no_free_delivery_claim | 5/5 |
| no_fresh_price_pick | 0/5 |
| no_invented_installment_terms | 5/5 |
| no_invented_sku | 5/5 |
| no_markdown | 20/20 |
| no_price | 4/10 |
| no_price_for:тюльпан | 5/5 |
| no_status_invention | 5/5 |
| no_stock_denial | 5/5 |
| no_stock_guarantee | 10/10 |
| no_total_confirmation | 5/5 |
| numbers_grounded | 25/25 |
| offers_handoff_or_clarifies | 4/5 |
