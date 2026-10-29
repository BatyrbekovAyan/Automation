# Eval pt-now-b24-harness — v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

prompts: `f0c81168` · side `harness` · block `b24` of plan `Tools/n8n/eval/plans/pt-now.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) (served: gpt-5.4-mini-2026-03-17; request harness) · panel model `None` · max_tokens 200 · 24 scenarios / 72 runs · 484 s (whole schedule, 2 worker(s))
pass 18 · flaky 3 · fail 3 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 698185 in (cached 640128) + 6841 out (reasoning 0) ≈ $0.1223 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3624 ms, p90 5215 ms, max 8697 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at f0c81168)
memory marks (apply-history-handoff.py at `f0c81168`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 (3×) |  |
| ap_price_with_car | auto_parts | auto | 1.00 (3×) |  |
| ap_sku_exact_kz | auto_parts | auto | 0.67 (3×) | lang_kz |
| ap_unknown_part | auto_parts | auto | 1.00 (3×) |  |
| ap_handoff_fail | auto_parts | auto | 1.00 (3×) |  |
| ap_source_conflict | auto_parts | auto | 1.00 (3×) |  |
| ap_price_agrees_analogues | auto_parts | auto | 1.00 (3×) |  |
| ws_list_of_items | wholesale | auto | 0.00 (3×) | no_tool:create_handoff |
| ws_mixed_lang | wholesale | auto | 1.00 (3×) |  |
| fl_absent_item | flowers | auto | 1.00 (3×) |  |
| fl_kz_price | flowers | auto | 1.00 (3×) |  |
| fl_installment_unknown | flowers | auto | 1.00 (3×) |  |
| ks_installment_terms | kaspi_seller | auto | 1.00 (3×) |  |
| ks_variant_clarify | kaspi_seller | auto | 1.00 (3×) |  |
| ks_kz_availability | kaspi_seller | auto | 0.67 (3×) | lang_kz |
| ed_schedule_unknown | education | auto | 1.00 (3×) |  |
| ed_kz_parent | education | auto | 0.00 (3×) | lang_kz, no_tool:create_handoff |
| pr_price_no_model | phone_repair | auto | 0.67 (3×) | asks_model, no_price |
| pr_price_with_model | phone_repair | auto | 1.00 (3×) |  |
| pr_model_not_in_list | phone_repair | auto | 1.00 (3×) |  |
| pr_kz_battery | phone_repair | auto | 0.00 (3×) | lang_kz, mentions:15000 |
| gen_markdown_bait | wholesale | auto | 1.00 (3×) |  |
| gen_long_answer_budget | education | auto | 1.00 (3×) |  |
| gen_owner_pref_vs_fact | auto_parts | auto | 1.00 (3×) |  |

## By check

| check | pass/total |
|---|---|
| asks_car | 3/3 |
| asks_model | 2/3 |
| asks_variant_or_prices_both | 3/3 |
| claims_handoff_only_if_ok | 3/3 |
| lang_kz | 8/15 |
| lang_ru | 3/3 |
| marks_unknown_item | 3/3 |
| mentions:15000 | 2/3 |
| mentions:25000 | 3/3 |
| mentions:3200 | 6/6 |
| mentions:35000 | 3/3 |
| mentions:55000 | 3/3 |
| mentions_diagnostics_caveat | 3/3 |
| mentions_kaspi | 3/3 |
| no_closing_offer | 3/3 |
| no_conflict_claim | 3/3 |
| no_free_delivery_claim | 3/3 |
| no_fresh_price_pick | 3/3 |
| no_installment_claim | 3/3 |
| no_invented_installment_terms | 3/3 |
| no_invented_sku | 3/3 |
| no_markdown | 24/24 |
| no_marker_leak | 72/72 |
| no_price | 11/12 |
| no_price_caveat | 3/3 |
| no_price_for:тюльпан | 3/3 |
| no_schedule_invention | 3/3 |
| no_stock_denial | 6/6 |
| no_stock_guarantee | 6/6 |
| no_tool:create_handoff | 43/48 |
| no_total_confirmation | 3/3 |
| numbers_grounded | 33/33 |
| offers_handoff_or_clarifies | 15/15 |
| tool_called | 3/3 |
