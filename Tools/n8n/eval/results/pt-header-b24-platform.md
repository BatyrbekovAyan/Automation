# Eval pt-header-b24-platform — v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

prompts: `f0c81168` · side `platform` · block `b24` of plan `Tools/n8n/eval/plans/pt-header.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) (served: gpt-5.4-mini-2026-03-17; request n8n+platform) · panel model `None` · max_tokens 200 · 24 scenarios / 48 runs · 323 s (whole schedule, 2 worker(s))
pass 18 · flaky 3 · fail 3 · error runs 0 · finish_reason=length in 1 runs
auto-path tokens 466808 in (cached 449152) + 4646 out (reasoning 0) ≈ $0.0678 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3375 ms, p90 5015 ms, max 7244 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at f0c81168)
memory marks (apply-history-handoff.py at `f0c81168`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 (2×) |  |
| ap_price_with_car | auto_parts | auto | 1.00 (2×) |  |
| ap_sku_exact_kz | auto_parts | auto | 1.00 (2×) |  |
| ap_unknown_part | auto_parts | auto | 1.00 (2×) |  |
| ap_handoff_fail | auto_parts | auto | 1.00 (2×) |  |
| ap_source_conflict | auto_parts | auto | 1.00 (2×) |  |
| ap_price_agrees_analogues | auto_parts | auto | 1.00 (2×) |  |
| ws_list_of_items | wholesale | auto | 0.50 (2×) | no_tool:create_handoff |
| ws_mixed_lang | wholesale | auto | 1.00 (2×) |  |
| fl_absent_item | flowers | auto | 1.00 (2×) |  |
| fl_kz_price | flowers | auto | 0.50 (2×) | lang_kz |
| fl_installment_unknown | flowers | auto | 1.00 (2×) |  |
| ks_installment_terms | kaspi_seller | auto | 1.00 (2×) |  |
| ks_variant_clarify | kaspi_seller | auto | 1.00 (2×) |  |
| ks_kz_availability | kaspi_seller | auto | 1.00 (2×) |  |
| ed_schedule_unknown | education | auto | 1.00 (2×) |  |
| ed_kz_parent | education | auto | 0.50 (2×) | lang_kz, no_tool:create_handoff |
| pr_price_no_model | phone_repair | auto | 0.00 (2×) | asks_model, no_price |
| pr_price_with_model | phone_repair | auto | 1.00 (2×) |  |
| pr_model_not_in_list | phone_repair | auto | 1.00 (2×) |  |
| pr_kz_battery | phone_repair | auto | 0.00 (2×) | mentions:15000 |
| gen_markdown_bait | wholesale | auto | 0.00 (2×) | no_markdown |
| gen_long_answer_budget | education | auto | 1.00 (2×) |  |
| gen_owner_pref_vs_fact | auto_parts | auto | 1.00 (2×) |  |

## By check

| check | pass/total |
|---|---|
| asks_car | 2/2 |
| asks_model | 0/2 |
| asks_variant_or_prices_both | 2/2 |
| claims_handoff_only_if_ok | 2/2 |
| lang_kz | 8/10 |
| lang_ru | 2/2 |
| marks_unknown_item | 2/2 |
| mentions:15000 | 0/2 |
| mentions:25000 | 2/2 |
| mentions:3200 | 4/4 |
| mentions:35000 | 2/2 |
| mentions:55000 | 2/2 |
| mentions_diagnostics_caveat | 2/2 |
| mentions_kaspi | 2/2 |
| no_closing_offer | 2/2 |
| no_conflict_claim | 2/2 |
| no_free_delivery_claim | 2/2 |
| no_fresh_price_pick | 2/2 |
| no_installment_claim | 2/2 |
| no_invented_installment_terms | 2/2 |
| no_invented_sku | 2/2 |
| no_markdown | 14/16 |
| no_marker_leak | 48/48 |
| no_price | 6/8 |
| no_price_caveat | 2/2 |
| no_price_for:тюльпан | 2/2 |
| no_schedule_invention | 2/2 |
| no_stock_denial | 4/4 |
| no_stock_guarantee | 4/4 |
| no_tool:create_handoff | 30/32 |
| no_total_confirmation | 2/2 |
| numbers_grounded | 22/22 |
| offers_handoff_or_clarifies | 10/10 |
| tool_called | 2/2 |
