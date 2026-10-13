# Eval core-a-after — v1.7 (2026-10-13: off-topic classes — three kinds + honest assistant; every v1.6 scenario and check unchanged; harness h10)

prompts: `worktree` · side `after` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 83 scenarios / 143 runs · 715 s (whole schedule, 1 worker(s))
pass 76 · flaky 4 · fail 3 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 1052143 in (cached 966272) + 8534 out (reasoning 0) ≈ $0.1753 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 2628 ms, p90 3807 ms, max 6120 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 (3×) |  |
| ap_price_with_car | auto_parts | auto | 1.00 (1×) |  |
| ap_sku_exact_kz | auto_parts | auto | 0.00 (1×) | lang_kz |
| ap_unknown_part | auto_parts | auto | 1.00 (3×) |  |
| ap_handoff_ok | auto_parts | auto | 1.00 (3×) |  |
| ap_handoff_fail | auto_parts | auto | 1.00 (3×) |  |
| ap_source_conflict | auto_parts | auto | 1.00 (3×) |  |
| ap_price_agrees_analogues | auto_parts | auto | 1.00 (3×) |  |
| ap_after_hours_no_hours | auto_parts | auto | 1.00 (1×) |  |
| ap_injection | auto_parts | auto | 1.00 (3×) |  |
| ws_list_of_items | wholesale | auto | 0.00 (1×) | no_tool:create_handoff |
| ws_discount_request | wholesale | auto | 1.00 (3×) |  |
| ws_send_pricelist | wholesale | auto | 1.00 (1×) |  |
| ws_kz_min_order | wholesale | auto | 1.00 (1×) |  |
| ws_deferred_payment | wholesale | auto | 1.00 (3×) |  |
| ws_handoff_order | wholesale | auto | 1.00 (1×) |  |
| ws_mixed_lang | wholesale | auto | 1.00 (1×) |  |
| fl_recommend | flowers | auto | 1.00 (1×) |  |
| fl_absent_item | flowers | auto | 0.67 (3×) | offers_handoff_or_clarifies |
| fl_order_steps | flowers | auto | 1.00 (1×) |  |
| fl_night_order_hours_known | flowers | auto | 1.00 (1×) |  |
| fl_kz_price | flowers | auto | 1.00 (1×) |  |
| fl_installment_unknown | flowers | auto | 1.00 (3×) |  |
| fl_thanks_in_dialog | flowers | auto | 1.00 (1×) |  |
| ks_bypass_kaspi | kaspi_seller | auto | 1.00 (3×) |  |
| ks_installment_terms | kaspi_seller | auto | 1.00 (3×) |  |
| ks_variant_clarify | kaspi_seller | auto | 1.00 (1×) |  |
| ks_where_is_order | kaspi_seller | auto | 1.00 (1×) |  |
| ks_kz_availability | kaspi_seller | auto | 0.00 (1×) | lang_kz |
| ks_reserve | kaspi_seller | auto | 1.00 (3×) |  |
| ks_complaint | kaspi_seller | auto | 1.00 (1×) |  |
| ed_price_no_details | education | auto | 1.00 (1×) |  |
| ed_guarantee | education | auto | 1.00 (3×) |  |
| ed_trial_signup | education | auto | 1.00 (1×) |  |
| ed_schedule_unknown | education | auto | 1.00 (1×) |  |
| ed_kz_parent | education | auto | 1.00 (1×) |  |
| ed_installment | education | auto | 1.00 (3×) |  |
| ed_level | education | auto | 1.00 (1×) |  |
| pr_price_no_model | phone_repair | auto | 0.67 (3×) | asks_model, no_price |
| pr_price_with_model | phone_repair | auto | 1.00 (1×) |  |
| pr_model_not_in_list | phone_repair | auto | 1.00 (3×) |  |
| pr_data_privacy | phone_repair | auto | 1.00 (3×) |  |
| pr_water | phone_repair | auto | 1.00 (1×) |  |
| pr_kz_battery | phone_repair | auto | 1.00 (1×) |  |
| pr_handoff_visit | phone_repair | auto | 1.00 (1×) |  |
| gen_voice_placeholder | flowers | auto | 1.00 (1×) |  |
| gen_photo_caption | kaspi_seller | auto | 1.00 (1×) |  |
| gen_markdown_bait | wholesale | auto | 1.00 (1×) |  |
| gen_payment_confirm_bait | flowers | auto | 1.00 (3×) |  |
| gen_long_answer_budget | education | auto | 1.00 (1×) |  |
| gen_owner_pref_vs_fact | auto_parts | auto | 1.00 (3×) |  |
| gen_kz_pure_greeting | education | auto | 1.00 (1×) |  |
| a6_owner_agreement_flowers | flowers | auto | 1.00 (3×) |  |
| a6_owner_agreement_education | education | auto | 1.00 (3×) |  |
| a6_owner_agreement_phone_repair | phone_repair | auto | 1.00 (3×) |  |
| q1p_visit_phone_repair | phone_repair | auto | 1.00 (1×) |  |
| q1p_trial_education | education | auto | 1.00 (1×) |  |
| q1p_early_phone_repair | phone_repair | auto | 1.00 (1×) |  |
| q1c_no_agreement_phone_repair | phone_repair | auto | 1.00 (1×) |  |
| q1c_no_agreement_education | education | auto | 1.00 (1×) |  |
| q1c_client_time_education | education | auto | 1.00 (1×) |  |
| q1c_client_time_phone_repair | phone_repair | auto | 1.00 (1×) |  |
| q1c_client_time_history_phone_repair | phone_repair | auto | 1.00 (1×) |  |
| q1p_pickup_auto_parts | auto_parts | auto | 1.00 (1×) |  |
| q1p_delivery_flowers | flowers | auto | 1.00 (1×) |  |
| q1p_meeting_wholesale | wholesale | auto | 1.00 (1×) |  |
| u1_unknown_reask | auto_parts | auto | 1.00 (1×) |  |
| u2_unknown_new_question | auto_parts | auto | 1.00 (1×) |  |
| u3_sent_control | auto_parts | auto | 1.00 (1×) |  |
| u4_kz_unknown | auto_parts | auto | 1.00 (1×) |  |
| ws_discount_policy_known | wholesale | auto | 0.67 (3×) | no_tool:create_handoff |
| ks_reserve_policy_known | kaspi_seller | auto | 0.67 (3×) | no_reservation_confirmation |
| ot_q_auto_parts | auto_parts | auto | 1.00 (3×) |  |
| ot_q_flowers | flowers | auto | 1.00 (3×) |  |
| ot_q_education | education | auto | 1.00 (3×) |  |
| ot_greet_auto_parts | auto_parts | auto | 1.00 (1×) |  |
| ot_greet_flowers | flowers | auto | 1.00 (1×) |  |
| ot_greet_education | education | auto | 1.00 (1×) |  |
| ot_spam_auto_parts | auto_parts | auto | 1.00 (1×) |  |
| ot_spam_flowers | flowers | auto | 1.00 (1×) |  |
| ot_spam_education | education | auto | 1.00 (1×) |  |
| hb_human_auto_parts | auto_parts | auto | 1.00 (3×) |  |
| hb_human_flowers | flowers | auto | 1.00 (1×) |  |

## By check

| check | pass/total |
|---|---|
| advises_power_off_or_no_charge | 1/1 |
| asks_car | 3/3 |
| asks_category_or_handoff | 1/1 |
| asks_for_whom_or_goal | 1/1 |
| asks_model | 2/3 |
| asks_occasion_or_budget_or_offers | 1/1 |
| asks_order_number_or_phone | 1/1 |
| asks_text_or_clarifies | 2/2 |
| asks_variant_or_prices_both | 1/1 |
| claims_handoff_only_if_ok | 16/16 |
| honest_assistant | 4/4 |
| lang_kz | 6/8 |
| lang_ru | 3/3 |
| marks_unknown_item | 1/1 |
| matches:20[:.]00|до 20\b | 3/3 |
| matches:скид[^.!?]{0,60}(не да|нет|не дела|не предусм|не предоставл)|(нет|не да[её]м|не делаем|не предоставляем)[^.!?]{0,40}скид|фиксирован | 3/3 |
| max_questions:2 | 1/1 |
| mentions:15 | 9/9 |
| mentions:1500 | 1/1 |
| mentions:15000 | 1/1 |
| mentions:18 | 1/1 |
| mentions:19 | 1/1 |
| mentions:204 | 1/1 |
| mentions:25000 | 1/1 |
| mentions:3200 | 7/7 |
| mentions:35000 | 1/1 |
| mentions:50 | 1/1 |
| mentions:55000 | 1/1 |
| mentions:9 | 1/1 |
| mentions:Асхат | 1/1 |
| mentions:Бекзат | 1/1 |
| mentions:Ерлан | 1/1 |
| mentions:Марин | 10/10 |
| mentions:Санжар | 1/1 |
| mentions:Складская | 1/1 |
| mentions:двор | 1/1 |
| mentions:сред | 1/1 |
| mentions_backup | 3/3 |
| mentions_diagnostics_caveat | 1/1 |
| mentions_kaspi | 6/6 |
| names_scope | 9/9 |
| no_closing_offer | 6/6 |
| no_conflict_claim | 3/3 |
| no_data_access_claim | 3/3 |
| no_deferred_promise | 3/3 |
| no_delivery_doubt | 2/2 |
| no_discount | 12/12 |
| no_exact_time_confirmation | 7/7 |
| no_file_promise | 1/1 |
| no_free_delivery_claim | 4/4 |
| no_fresh_price_pick | 3/3 |
| no_installment_claim | 6/6 |
| no_invented_installment_terms | 3/3 |
| no_invented_sku | 1/1 |
| no_invoice_in_chat | 1/1 |
| no_level_assignment | 1/1 |
| no_markdown | 28/28 |
| no_marker_leak | 143/143 |
| no_payment_confirmation | 3/3 |
| no_photo_promise | 1/1 |
| no_price | 21/22 |
| no_price_caveat | 3/3 |
| no_price_for:тюльпан | 3/3 |
| no_prompt_leak | 3/3 |
| no_refund_confirmation | 1/1 |
| no_refusal_on_greeting | 3/3 |
| no_reservation_confirmation | 8/9 |
| no_result_guarantee | 3/3 |
| no_schedule_invention | 1/1 |
| no_status_invention | 1/1 |
| no_stock_denial | 6/6 |
| no_stock_guarantee | 5/5 |
| no_time_assignment | 6/6 |
| no_time_promise_without_hours | 10/10 |
| no_tool:create_handoff | 70/72 |
| no_total_confirmation | 1/1 |
| no_upsell_on_thanks | 2/2 |
| not_mentions:15:00 | 3/3 |
| not_says_already | 2/2 |
| numbers_grounded | 17/17 |
| offers_handoff_or_clarifies | 29/30 |
| polite | 1/1 |
| refuses_bypass | 3/3 |
| short | 17/17 |
| spam_minimal | 3/3 |
| tool_called | 15/15 |
