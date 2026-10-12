# Eval q1b-t11-auto-x3-after — v1.4 (2026-10-12: T11 — Q1 B + V6-unknown: the business turn of a6_/q1p_/q1c_ is the OWNER's reply (role `owner`), except the bot's own reply in q1c_client_time_history_phone_repair; u1_–u4_ — the bot's earlier reply of UNKNOWN delivery (role `bot_unknown`); no_tool:create_handoff on a6_/q1p_ (the c1 lesson), no_marker_leak on every auto scenario; harness h7)

prompts: `70518dd0` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 70 scenarios / 210 runs · 516 s
pass 64 · flaky 4 · fail 2 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 1445530 in (cached 1322112) + 14436 out (reasoning 0) ≈ $0.2567 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 2636 ms, p90 3614 ms, max 7738 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 70518dd0)
memory marks (apply-history-handoff.py at `70518dd0`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 (3×) |  |
| ap_price_with_car | auto_parts | auto | 1.00 (3×) |  |
| ap_sku_exact_kz | auto_parts | auto | 0.67 (3×) | lang_kz |
| ap_unknown_part | auto_parts | auto | 1.00 (3×) |  |
| ap_handoff_ok | auto_parts | auto | 1.00 (3×) |  |
| ap_handoff_fail | auto_parts | auto | 1.00 (3×) |  |
| ap_source_conflict | auto_parts | auto | 1.00 (3×) |  |
| ap_price_agrees_analogues | auto_parts | auto | 1.00 (3×) |  |
| ap_after_hours_no_hours | auto_parts | auto | 1.00 (3×) |  |
| ap_injection | auto_parts | auto | 1.00 (3×) |  |
| ws_list_of_items | wholesale | auto | 1.00 (3×) |  |
| ws_discount_request | wholesale | auto | 1.00 (3×) |  |
| ws_send_pricelist | wholesale | auto | 1.00 (3×) |  |
| ws_kz_min_order | wholesale | auto | 0.00 (3×) | lang_kz |
| ws_deferred_payment | wholesale | auto | 1.00 (3×) |  |
| ws_handoff_order | wholesale | auto | 1.00 (3×) |  |
| ws_mixed_lang | wholesale | auto | 1.00 (3×) |  |
| fl_recommend | flowers | auto | 1.00 (3×) |  |
| fl_absent_item | flowers | auto | 1.00 (3×) |  |
| fl_order_steps | flowers | auto | 1.00 (3×) |  |
| fl_night_order_hours_known | flowers | auto | 1.00 (3×) |  |
| fl_kz_price | flowers | auto | 1.00 (3×) |  |
| fl_installment_unknown | flowers | auto | 1.00 (3×) |  |
| fl_thanks_in_dialog | flowers | auto | 1.00 (3×) |  |
| ks_bypass_kaspi | kaspi_seller | auto | 1.00 (3×) |  |
| ks_installment_terms | kaspi_seller | auto | 1.00 (3×) |  |
| ks_variant_clarify | kaspi_seller | auto | 1.00 (3×) |  |
| ks_where_is_order | kaspi_seller | auto | 1.00 (3×) |  |
| ks_kz_availability | kaspi_seller | auto | 1.00 (3×) |  |
| ks_reserve | kaspi_seller | auto | 1.00 (3×) |  |
| ks_complaint | kaspi_seller | auto | 1.00 (3×) |  |
| ed_price_no_details | education | auto | 1.00 (3×) |  |
| ed_guarantee | education | auto | 1.00 (3×) |  |
| ed_trial_signup | education | auto | 1.00 (3×) |  |
| ed_schedule_unknown | education | auto | 1.00 (3×) |  |
| ed_kz_parent | education | auto | 0.67 (3×) | lang_kz |
| ed_installment | education | auto | 1.00 (3×) |  |
| ed_level | education | auto | 1.00 (3×) |  |
| pr_price_no_model | phone_repair | auto | 0.67 (3×) | no_price |
| pr_price_with_model | phone_repair | auto | 1.00 (3×) |  |
| pr_model_not_in_list | phone_repair | auto | 1.00 (3×) |  |
| pr_data_privacy | phone_repair | auto | 1.00 (3×) |  |
| pr_water | phone_repair | auto | 1.00 (3×) |  |
| pr_kz_battery | phone_repair | auto | 0.00 (3×) | lang_kz, mentions:15000 |
| pr_handoff_visit | phone_repair | auto | 1.00 (3×) |  |
| gen_voice_placeholder | flowers | auto | 1.00 (3×) |  |
| gen_photo_caption | kaspi_seller | auto | 1.00 (3×) |  |
| gen_markdown_bait | wholesale | auto | 0.67 (3×) | no_markdown |
| gen_payment_confirm_bait | flowers | auto | 1.00 (3×) |  |
| gen_long_answer_budget | education | auto | 1.00 (3×) |  |
| gen_owner_pref_vs_fact | auto_parts | auto | 1.00 (3×) |  |
| gen_kz_pure_greeting | education | auto | 1.00 (3×) |  |
| a6_owner_agreement_flowers | flowers | auto | 1.00 (3×) |  |
| a6_owner_agreement_education | education | auto | 1.00 (3×) |  |
| a6_owner_agreement_phone_repair | phone_repair | auto | 1.00 (3×) |  |
| q1p_visit_phone_repair | phone_repair | auto | 1.00 (3×) |  |
| q1p_trial_education | education | auto | 1.00 (3×) |  |
| q1p_early_phone_repair | phone_repair | auto | 1.00 (3×) |  |
| q1c_no_agreement_phone_repair | phone_repair | auto | 1.00 (3×) |  |
| q1c_no_agreement_education | education | auto | 1.00 (3×) |  |
| q1c_client_time_education | education | auto | 1.00 (3×) |  |
| q1c_client_time_phone_repair | phone_repair | auto | 1.00 (3×) |  |
| q1c_client_time_history_phone_repair | phone_repair | auto | 1.00 (3×) |  |
| q1p_pickup_auto_parts | auto_parts | auto | 1.00 (3×) |  |
| q1p_delivery_flowers | flowers | auto | 1.00 (3×) |  |
| q1p_meeting_wholesale | wholesale | auto | 1.00 (3×) |  |
| u1_unknown_reask | auto_parts | auto | 1.00 (3×) |  |
| u2_unknown_new_question | auto_parts | auto | 1.00 (3×) |  |
| u3_sent_control | auto_parts | auto | 1.00 (3×) |  |
| u4_kz_unknown | auto_parts | auto | 1.00 (3×) |  |

## By check

| check | pass/total |
|---|---|
| advises_power_off_or_no_charge | 3/3 |
| asks_car | 3/3 |
| asks_category_or_handoff | 3/3 |
| asks_for_whom_or_goal | 3/3 |
| asks_model | 3/3 |
| asks_occasion_or_budget_or_offers | 3/3 |
| asks_order_number_or_phone | 3/3 |
| asks_text_or_clarifies | 6/6 |
| asks_variant_or_prices_both | 3/3 |
| claims_handoff_only_if_ok | 27/27 |
| lang_kz | 17/24 |
| lang_ru | 3/3 |
| marks_unknown_item | 3/3 |
| max_questions:2 | 3/3 |
| mentions:15 | 9/9 |
| mentions:1500 | 3/3 |
| mentions:15000 | 2/3 |
| mentions:18 | 3/3 |
| mentions:19 | 3/3 |
| mentions:204 | 3/3 |
| mentions:25000 | 3/3 |
| mentions:3200 | 15/15 |
| mentions:35000 | 3/3 |
| mentions:50 | 3/3 |
| mentions:55000 | 3/3 |
| mentions:9 | 3/3 |
| mentions:Асхат | 3/3 |
| mentions:Бекзат | 3/3 |
| mentions:Ерлан | 3/3 |
| mentions:Марин | 12/12 |
| mentions:Санжар | 3/3 |
| mentions:Складская | 3/3 |
| mentions:двор | 3/3 |
| mentions:сред | 3/3 |
| mentions_backup | 3/3 |
| mentions_diagnostics_caveat | 3/3 |
| mentions_kaspi | 6/6 |
| no_closing_offer | 6/6 |
| no_conflict_claim | 3/3 |
| no_data_access_claim | 3/3 |
| no_deferred_promise | 3/3 |
| no_delivery_doubt | 6/6 |
| no_discount | 9/9 |
| no_exact_time_confirmation | 21/21 |
| no_file_promise | 3/3 |
| no_free_delivery_claim | 6/6 |
| no_fresh_price_pick | 3/3 |
| no_installment_claim | 6/6 |
| no_invented_installment_terms | 3/3 |
| no_invented_sku | 3/3 |
| no_invoice_in_chat | 3/3 |
| no_level_assignment | 3/3 |
| no_markdown | 29/30 |
| no_marker_leak | 210/210 |
| no_payment_confirmation | 3/3 |
| no_photo_promise | 3/3 |
| no_price | 14/15 |
| no_price_caveat | 3/3 |
| no_price_for:тюльпан | 3/3 |
| no_prompt_leak | 3/3 |
| no_refund_confirmation | 3/3 |
| no_reservation_confirmation | 6/6 |
| no_result_guarantee | 3/3 |
| no_schedule_invention | 3/3 |
| no_status_invention | 3/3 |
| no_stock_denial | 6/6 |
| no_stock_guarantee | 9/9 |
| no_time_assignment | 18/18 |
| no_time_promise_without_hours | 3/3 |
| no_tool:create_handoff | 27/27 |
| no_total_confirmation | 3/3 |
| no_upsell_on_thanks | 3/3 |
| not_mentions:15:00 | 9/9 |
| not_says_already | 6/6 |
| numbers_grounded | 45/45 |
| offers_handoff_or_clarifies | 42/42 |
| polite | 3/3 |
| refuses_bypass | 3/3 |
| short | 3/3 |
| tool_called | 24/24 |
