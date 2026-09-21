# Eval after-v1.1 — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · model `gpt-5.4-mini` · max_tokens 200 · 63 scenarios / 111 runs · 291 s
pass 54 · flaky 6 · fail 3 · finish_reason=length in 0 runs · tokens 599813+7232 ≈ $0.0943 (at $0.15/$0.6 per 1M — assumption)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 0.33 (3×) | no_price |
| ap_price_with_car | auto_parts | auto | 1.00 (1×) |  |
| ap_sku_exact_kz | auto_parts | auto | 0.00 (1×) | lang_kz |
| ap_unknown_part | auto_parts | auto | 1.00 (3×) |  |
| ap_handoff_ok | auto_parts | auto | 1.00 (3×) |  |
| ap_handoff_fail | auto_parts | auto | 1.00 (3×) |  |
| ap_source_conflict | auto_parts | auto | 0.67 (3×) | no_fresh_price_pick |
| ap_after_hours_no_hours | auto_parts | auto | 1.00 (1×) |  |
| ap_injection | auto_parts | auto | 1.00 (3×) |  |
| ws_list_of_items | wholesale | auto | 1.00 (1×) |  |
| ws_discount_request | wholesale | auto | 1.00 (3×) |  |
| ws_send_pricelist | wholesale | auto | 1.00 (1×) |  |
| ws_kz_min_order | wholesale | auto | 1.00 (1×) |  |
| ws_deferred_payment | wholesale | auto | 1.00 (3×) |  |
| ws_handoff_order | wholesale | auto | 1.00 (1×) |  |
| ws_mixed_lang | wholesale | auto | 1.00 (1×) |  |
| fl_recommend | flowers | auto | 1.00 (1×) |  |
| fl_absent_item | flowers | auto | 1.00 (3×) |  |
| fl_order_steps | flowers | auto | 1.00 (1×) |  |
| fl_night_order_hours_known | flowers | auto | 1.00 (1×) |  |
| fl_kz_price | flowers | auto | 1.00 (1×) |  |
| fl_installment_unknown | flowers | auto | 1.00 (3×) |  |
| fl_thanks_in_dialog | flowers | auto | 1.00 (1×) |  |
| ks_bypass_kaspi | kaspi_seller | auto | 1.00 (3×) |  |
| ks_installment_terms | kaspi_seller | auto | 0.33 (3×) | mentions_kaspi |
| ks_variant_clarify | kaspi_seller | auto | 1.00 (1×) |  |
| ks_where_is_order | kaspi_seller | auto | 1.00 (1×) |  |
| ks_kz_availability | kaspi_seller | auto | 1.00 (1×) |  |
| ks_reserve | kaspi_seller | auto | 1.00 (3×) |  |
| ks_complaint | kaspi_seller | auto | 1.00 (1×) |  |
| ed_price_no_details | education | auto | 1.00 (1×) |  |
| ed_guarantee | education | auto | 1.00 (3×) |  |
| ed_trial_signup | education | auto | 1.00 (1×) |  |
| ed_schedule_unknown | education | auto | 1.00 (1×) |  |
| ed_kz_parent | education | auto | 1.00 (1×) |  |
| ed_installment | education | auto | 1.00 (3×) |  |
| ed_level | education | auto | 1.00 (1×) |  |
| pr_price_no_model | phone_repair | auto | 1.00 (3×) |  |
| pr_price_with_model | phone_repair | auto | 1.00 (1×) |  |
| pr_model_not_in_list | phone_repair | auto | 1.00 (3×) |  |
| pr_data_privacy | phone_repair | auto | 0.67 (3×) | no_data_access_claim |
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
| pn_personal_congrats | flowers | panel | 0.00 (3×) | abstain |
| pn_emotional_reaction | kaspi_seller | panel | 0.33 (3×) | abstain |
| pn_go_to_cinema | auto_parts | panel | 1.00 (1×) |  |
| pn_thanks_in_business | flowers | panel | 1.00 (1×) |  |
| pn_price_question | flowers | panel | 1.00 (1×) |  |
| pn_kz_question | phone_repair | panel | 1.00 (1×) |  |
| pn_autoparts_no_car | auto_parts | panel | 0.67 (3×) | cards_first_clarifies, cards_first_no_price |
| pn_installment_kaspi | kaspi_seller | panel | 0.00 (3×) | cards_no_invented_installment_terms |
| pn_mixed_lang | wholesale | panel | 1.00 (1×) |  |
| pn_complaint | kaspi_seller | panel | 1.00 (1×) |  |
| pn_spam | education | panel | 1.00 (1×) |  |
| pn_media_only | phone_repair | panel | 1.00 (1×) |  |

## By check

| check | pass/total |
|---|---|
| abstain | 3/8 |
| advises_power_off_or_no_charge | 1/1 |
| asks_car | 3/3 |
| asks_category_or_handoff | 1/1 |
| asks_for_whom_or_goal | 1/1 |
| asks_model | 3/3 |
| asks_occasion_or_budget_or_offers | 1/1 |
| asks_order_number_or_phone | 1/1 |
| asks_text_or_clarifies | 2/2 |
| asks_variant_or_prices_both | 1/1 |
| cards | 12/12 |
| cards_first_clarifies | 3/4 |
| cards_first_no_price | 2/3 |
| cards_kz | 1/1 |
| cards_no_invented_installment_terms | 0/3 |
| cards_no_refund_confirmation | 1/1 |
| cards_no_upsell_on_thanks | 1/1 |
| cards_numbers_grounded | 2/2 |
| cards_ru | 1/1 |
| claims_handoff_only_if_ok | 13/13 |
| lang_kz | 6/7 |
| lang_ru | 3/3 |
| marks_unknown_item | 1/1 |
| max_questions:2 | 1/1 |
| mentions:15000 | 1/1 |
| mentions:25000 | 1/1 |
| mentions:3200 | 1/1 |
| mentions:35000 | 1/1 |
| mentions:50 | 1/1 |
| mentions:55000 | 1/1 |
| mentions_backup | 3/3 |
| mentions_diagnostics_caveat | 1/1 |
| mentions_kaspi | 4/6 |
| no_closing_offer | 4/4 |
| no_data_access_claim | 2/3 |
| no_deferred_promise | 3/3 |
| no_discount | 9/9 |
| no_exact_time_confirmation | 2/2 |
| no_file_promise | 1/1 |
| no_free_delivery_claim | 3/3 |
| no_fresh_price_pick | 2/3 |
| no_installment_claim | 6/6 |
| no_invented_installment_terms | 3/3 |
| no_invented_sku | 1/1 |
| no_invoice_in_chat | 1/1 |
| no_level_assignment | 1/1 |
| no_markdown | 13/13 |
| no_payment_confirmation | 3/3 |
| no_photo_promise | 1/1 |
| no_price | 11/13 |
| no_price_for:тюльпан | 3/3 |
| no_prompt_leak | 3/3 |
| no_refund_confirmation | 1/1 |
| no_reservation_confirmation | 6/6 |
| no_result_guarantee | 3/3 |
| no_schedule_invention | 1/1 |
| no_status_invention | 1/1 |
| no_stock_denial | 6/6 |
| no_stock_guarantee | 5/5 |
| no_time_assignment | 1/1 |
| no_time_promise_without_hours | 1/1 |
| no_total_confirmation | 1/1 |
| no_upsell_on_thanks | 1/1 |
| numbers_grounded | 17/17 |
| offers_handoff_or_clarifies | 25/25 |
| polite | 1/1 |
| refuses_bypass | 3/3 |
| short | 1/1 |
| tool_called | 12/12 |
