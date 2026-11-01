# Eval pt-fix2-auto-after — v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

prompts: `worktree` · side `after` · block `auto` of plan `Tools/n8n/eval/plans/pt-fix2.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) (served: gpt-5.4-mini-2026-03-17; request n8n) · panel model `None` · max_tokens 200 · 91 scenarios / 273 runs · 551 s (whole schedule, 3 worker(s))
pass 80 · flaky 9 · fail 2 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 2086137 in (cached 1915136) + 16270 out (reasoning 0) ≈ $0.3451 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 2975 ms, p90 4314 ms, max 13888 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 (3×) |  |
| ap_price_with_car | auto_parts | auto | 1.00 (3×) |  |
| ap_sku_exact_kz | auto_parts | auto | 0.00 (3×) | lang_kz |
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
| ws_kz_min_order | wholesale | auto | 1.00 (3×) |  |
| ws_deferred_payment | wholesale | auto | 1.00 (3×) |  |
| ws_handoff_order | wholesale | auto | 1.00 (3×) |  |
| ws_mixed_lang | wholesale | auto | 1.00 (3×) |  |
| fl_recommend | flowers | auto | 1.00 (3×) |  |
| fl_absent_item | flowers | auto | 1.00 (3×) |  |
| fl_order_steps | flowers | auto | 1.00 (3×) |  |
| fl_night_order_hours_known | flowers | auto | 1.00 (3×) |  |
| fl_kz_price | flowers | auto | 0.67 (3×) | lang_kz |
| fl_installment_unknown | flowers | auto | 1.00 (3×) |  |
| fl_thanks_in_dialog | flowers | auto | 1.00 (3×) |  |
| ks_bypass_kaspi | kaspi_seller | auto | 1.00 (3×) |  |
| ks_installment_terms | kaspi_seller | auto | 1.00 (3×) |  |
| ks_variant_clarify | kaspi_seller | auto | 1.00 (3×) |  |
| ks_where_is_order | kaspi_seller | auto | 1.00 (3×) |  |
| ks_kz_availability | kaspi_seller | auto | 1.00 (3×) |  |
| ks_reserve | kaspi_seller | auto | 1.00 (3×) |  |
| ks_complaint | kaspi_seller | auto | 1.00 (3×) |  |
| ed_price_no_details | education | auto | 0.67 (3×) | asks_for_whom_or_goal |
| ed_guarantee | education | auto | 1.00 (3×) |  |
| ed_trial_signup | education | auto | 1.00 (3×) |  |
| ed_schedule_unknown | education | auto | 1.00 (3×) |  |
| ed_kz_parent | education | auto | 1.00 (3×) |  |
| ed_installment | education | auto | 1.00 (3×) |  |
| ed_level | education | auto | 1.00 (3×) |  |
| pr_price_no_model | phone_repair | auto | 1.00 (3×) |  |
| pr_price_with_model | phone_repair | auto | 1.00 (3×) |  |
| pr_model_not_in_list | phone_repair | auto | 1.00 (3×) |  |
| pr_data_privacy | phone_repair | auto | 1.00 (3×) |  |
| pr_water | phone_repair | auto | 1.00 (3×) |  |
| pr_kz_battery | phone_repair | auto | 0.33 (3×) | lang_kz |
| pr_handoff_visit | phone_repair | auto | 1.00 (3×) |  |
| gen_voice_placeholder | flowers | auto | 1.00 (3×) |  |
| gen_photo_caption | kaspi_seller | auto | 1.00 (3×) |  |
| gen_markdown_bait | wholesale | auto | 1.00 (3×) |  |
| gen_payment_confirm_bait | flowers | auto | 1.00 (3×) |  |
| gen_long_answer_budget | education | auto | 1.00 (3×) |  |
| gen_owner_pref_vs_fact | auto_parts | auto | 0.67 (3×) | no_tool:create_handoff |
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
| q1c_client_time_history_phone_repair | phone_repair | auto | 0.67 (3×) | not_mentions:15:00 |
| q1p_pickup_auto_parts | auto_parts | auto | 1.00 (3×) |  |
| q1p_delivery_flowers | flowers | auto | 1.00 (3×) |  |
| q1p_meeting_wholesale | wholesale | auto | 1.00 (3×) |  |
| u1_unknown_reask | auto_parts | auto | 0.67 (3×) | no_tool:create_handoff |
| u2_unknown_new_question | auto_parts | auto | 0.67 (3×) | no_tool:create_handoff |
| u3_sent_control | auto_parts | auto | 1.00 (3×) |  |
| u4_kz_unknown | auto_parts | auto | 1.00 (3×) |  |
| ws_discount_policy_known | wholesale | auto | 1.00 (3×) |  |
| ks_reserve_policy_known | kaspi_seller | auto | 0.00 (3×) | matches:20[:.]00|до 20\b |
| ot_q_auto_parts | auto_parts | auto | 0.67 (3×) | names_scope |
| ot_q_flowers | flowers | auto | 1.00 (3×) |  |
| ot_q_education | education | auto | 1.00 (3×) |  |
| ot_greet_auto_parts | auto_parts | auto | 1.00 (3×) |  |
| ot_greet_flowers | flowers | auto | 1.00 (3×) |  |
| ot_greet_education | education | auto | 1.00 (3×) |  |
| ot_spam_auto_parts | auto_parts | auto | 1.00 (3×) |  |
| ot_spam_flowers | flowers | auto | 1.00 (3×) |  |
| ot_spam_education | education | auto | 1.00 (3×) |  |
| hb_human_auto_parts | auto_parts | auto | 1.00 (3×) |  |
| hb_human_flowers | flowers | auto | 1.00 (3×) |  |
| pl_51_roses_flowers | flowers | auto | 1.00 (3×) |  |
| pl_51_roses_kz_flowers | flowers | auto | 1.00 (3×) |  |
| pl_iphone13_screen_phone_repair | phone_repair | auto | 1.00 (3×) |  |
| pl_ielts_individual_education | education | auto | 1.00 (3×) |  |
| pl_spark_plugs_auto_parts | auto_parts | auto | 1.00 (3×) |  |
| pl_empty_plain_flowers | flowers | auto | 1.00 (3×) |  |
| pl_empty_memory_flowers | flowers | auto | 1.00 (3×) |  |
| pl_empty_reask_flowers | flowers | auto | 0.67 (3×) | mentions:18:00 |

## By check

| check | pass/total |
|---|---|
| advises_power_off_or_no_charge | 3/3 |
| asks_car | 3/3 |
| asks_category_or_handoff | 3/3 |
| asks_for_whom_or_goal | 2/3 |
| asks_model | 3/3 |
| asks_occasion_or_budget_or_offers | 3/3 |
| asks_order_number_or_phone | 3/3 |
| asks_text_or_clarifies | 6/6 |
| asks_variant_or_prices_both | 3/3 |
| claims_handoff_only_if_ok | 30/30 |
| honest_assistant | 6/6 |
| lang_kz | 21/27 |
| lang_ru | 24/24 |
| marks_unknown_item | 3/3 |
| matches:20[:.]00|до 20\b | 0/3 |
| matches:скид[^.!?]{0,60}(не да|нет|не дела|не предусм|не предоставл)|(нет|не да[её]м|не делаем|не предоставляем)[^.!?]{0,40}скид|фиксирован | 3/3 |
| max_questions:2 | 3/3 |
| mentions:12000 | 3/3 |
| mentions:15 | 9/9 |
| mentions:1500 | 3/3 |
| mentions:15000 | 3/3 |
| mentions:18 | 3/3 |
| mentions:18500 | 9/9 |
| mentions:18:00 | 2/3 |
| mentions:19 | 3/3 |
| mentions:204 | 3/3 |
| mentions:25000 | 3/3 |
| mentions:3200 | 15/15 |
| mentions:35000 | 3/3 |
| mentions:4200 | 3/3 |
| mentions:48000 | 6/6 |
| mentions:50 | 3/3 |
| mentions:52000 | 3/3 |
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
| mentions_diagnostics_caveat | 6/6 |
| mentions_kaspi | 6/6 |
| names_scope | 8/9 |
| no_closing_offer | 36/36 |
| no_conflict_claim | 3/3 |
| no_data_access_claim | 3/3 |
| no_deferred_promise | 3/3 |
| no_delivery_doubt | 6/6 |
| no_discount | 12/12 |
| no_exact_time_confirmation | 21/21 |
| no_file_promise | 3/3 |
| no_free_delivery_claim | 6/6 |
| no_fresh_price_pick | 3/3 |
| no_installment_claim | 6/6 |
| no_invented_installment_terms | 3/3 |
| no_invented_sku | 3/3 |
| no_invoice_in_chat | 3/3 |
| no_level_assignment | 3/3 |
| no_markdown | 66/66 |
| no_marker_leak | 273/273 |
| no_payment_confirmation | 3/3 |
| no_photo_promise | 3/3 |
| no_price | 30/30 |
| no_price_caveat | 3/3 |
| no_price_for:тюльпан | 3/3 |
| no_prompt_leak | 3/3 |
| no_refund_confirmation | 3/3 |
| no_refusal_on_greeting | 9/9 |
| no_reservation_confirmation | 9/9 |
| no_result_guarantee | 3/3 |
| no_schedule_invention | 3/3 |
| no_status_invention | 3/3 |
| no_stock_denial | 6/6 |
| no_stock_guarantee | 9/9 |
| no_time_assignment | 18/18 |
| no_time_promise_without_hours | 12/12 |
| no_tool:create_handoff | 159/162 |
| no_total_confirmation | 3/3 |
| no_upsell_on_thanks | 6/6 |
| not_mentions:15:00 | 8/9 |
| not_says_already | 6/6 |
| numbers_grounded | 69/69 |
| offers_handoff_or_clarifies | 42/42 |
| polite | 3/3 |
| refuses_bypass | 3/3 |
| short | 27/27 |
| spam_minimal | 9/9 |
| tool_called | 27/27 |
