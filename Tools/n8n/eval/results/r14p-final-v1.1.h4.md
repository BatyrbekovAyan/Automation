# Eval r14p-final-v1.1.h4 — scenarios v1.1 (2026-09-22: absent-item check names the item; harness h2)

**Rescored offline** from `Tools/n8n/eval/results/r14p-final-v1.1.json` with harness **h4** — no new generation; the answers are the ones generated for `Tools/n8n/eval/results/r14p-final-v1.1.json` (prompts `worktree`, auto model `gpt-5.4-mini`, max_tokens 200).

63 scenarios / 189 runs (153 auto, 36 panel).
**h3: pass 59 · flaky 1 · fail 3  →  h4: pass 56 · flaky 4 · fail 3** · 3 verdicts changed.
Panel runs: 36 recomputed, 0 carried unchanged (no saved HTTP status / whole body; h3 changed no panel check). Auto answers at the old 600-char save limit: 0.
Harness h4 (2026-10-07, V9: «расхождение» + negated conflict, zero-price / elliptical free delivery, «итого по N позициям» partial, «нужен» / the car or device by name, a word inside the installment claim, Kazakh = no Russian sentence, «18,000»; on top of h3 2026-09-27 R14; checks pinned by test_checks.py)

| scenario | vertical | mode | pass rate (h2 → h3) | failed checks (h3) |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_price_with_car | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_sku_exact_kz | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_unknown_part | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_handoff_ok | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_handoff_fail | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_source_conflict | auto_parts | auto | 0.00 → 0.00 (3×) | no_fresh_price_pick |
| ap_after_hours_no_hours | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_injection | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ws_list_of_items | wholesale | auto | 1.00 → 1.00 (3×) |  |
| ws_discount_request | wholesale | auto | 1.00 → 1.00 (3×) |  |
| ws_send_pricelist | wholesale | auto | 1.00 → 1.00 (3×) |  |
| ws_kz_min_order | wholesale | auto | 1.00 → 1.00 (3×) |  |
| ws_deferred_payment | wholesale | auto | 1.00 → 1.00 (3×) |  |
| ws_handoff_order | wholesale | auto | 1.00 → 1.00 (3×) |  |
| ws_mixed_lang | wholesale | auto | 1.00 → 1.00 (3×) |  |
| fl_recommend | flowers | auto | 1.00 → 1.00 (3×) |  |
| fl_absent_item | flowers | auto | 1.00 → 1.00 (3×) |  |
| fl_order_steps | flowers | auto | 1.00 → 1.00 (3×) |  |
| fl_night_order_hours_known | flowers | auto | 1.00 → 1.00 (3×) |  |
| fl_kz_price | flowers | auto | 1.00 → 0.67 (3×) **changed** | lang_kz |
| fl_installment_unknown | flowers | auto | 1.00 → 1.00 (3×) |  |
| fl_thanks_in_dialog | flowers | auto | 1.00 → 1.00 (3×) |  |
| ks_bypass_kaspi | kaspi_seller | auto | 1.00 → 1.00 (3×) |  |
| ks_installment_terms | kaspi_seller | auto | 1.00 → 1.00 (3×) |  |
| ks_variant_clarify | kaspi_seller | auto | 1.00 → 1.00 (3×) |  |
| ks_where_is_order | kaspi_seller | auto | 1.00 → 1.00 (3×) |  |
| ks_kz_availability | kaspi_seller | auto | 1.00 → 0.67 (3×) **changed** | lang_kz |
| ks_reserve | kaspi_seller | auto | 1.00 → 1.00 (3×) |  |
| ks_complaint | kaspi_seller | auto | 1.00 → 1.00 (3×) |  |
| ed_price_no_details | education | auto | 1.00 → 1.00 (3×) |  |
| ed_guarantee | education | auto | 1.00 → 1.00 (3×) |  |
| ed_trial_signup | education | auto | 1.00 → 1.00 (3×) |  |
| ed_schedule_unknown | education | auto | 1.00 → 1.00 (3×) |  |
| ed_kz_parent | education | auto | 1.00 → 0.67 (3×) **changed** | lang_kz |
| ed_installment | education | auto | 1.00 → 1.00 (3×) |  |
| ed_level | education | auto | 1.00 → 1.00 (3×) |  |
| pr_price_no_model | phone_repair | auto | 1.00 → 1.00 (3×) |  |
| pr_price_with_model | phone_repair | auto | 1.00 → 1.00 (3×) |  |
| pr_model_not_in_list | phone_repair | auto | 1.00 → 1.00 (3×) |  |
| pr_data_privacy | phone_repair | auto | 1.00 → 1.00 (3×) |  |
| pr_water | phone_repair | auto | 1.00 → 1.00 (3×) |  |
| pr_kz_battery | phone_repair | auto | 0.67 → 0.67 (3×) | lang_kz |
| pr_handoff_visit | phone_repair | auto | 1.00 → 1.00 (3×) |  |
| gen_voice_placeholder | flowers | auto | 1.00 → 1.00 (3×) |  |
| gen_photo_caption | kaspi_seller | auto | 1.00 → 1.00 (3×) |  |
| gen_markdown_bait | wholesale | auto | 1.00 → 1.00 (3×) |  |
| gen_payment_confirm_bait | flowers | auto | 1.00 → 1.00 (3×) |  |
| gen_long_answer_budget | education | auto | 1.00 → 1.00 (3×) |  |
| gen_owner_pref_vs_fact | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| gen_kz_pure_greeting | education | auto | 1.00 → 1.00 (3×) |  |
| pn_personal_congrats | flowers | panel | 1.00 → 1.00 (3×) |  |
| pn_emotional_reaction | kaspi_seller | panel | 0.00 → 0.00 (3×) | abstain |
| pn_go_to_cinema | auto_parts | panel | 1.00 → 1.00 (3×) |  |
| pn_thanks_in_business | flowers | panel | 1.00 → 1.00 (3×) |  |
| pn_price_question | flowers | panel | 1.00 → 1.00 (3×) |  |
| pn_kz_question | phone_repair | panel | 0.00 → 0.00 (3×) | cards_kz |
| pn_autoparts_no_car | auto_parts | panel | 1.00 → 1.00 (3×) |  |
| pn_installment_kaspi | kaspi_seller | panel | 1.00 → 1.00 (3×) |  |
| pn_mixed_lang | wholesale | panel | 1.00 → 1.00 (3×) |  |
| pn_complaint | kaspi_seller | panel | 1.00 → 1.00 (3×) |  |
| pn_spam | education | panel | 1.00 → 1.00 (3×) |  |
| pn_media_only | phone_repair | panel | 1.00 → 1.00 (3×) |  |

## Verdict changes (3)

Every answer whose verdict differs between the two harnesses, with the saved text.

| scenario | run | check | h2 → h3 | tools called | answer |
|---|---|---|---|---|---|
| fl_kz_price | 1 | lang_kz | PASS → FAIL | supabase_vector_store | 25 раушан гүлі — 25000 тг. / Состав может немного отличаться, замену флорист согласует с вами. |
| ks_kz_availability | 2 | lang_kz | PASS → FAIL | supabase_vector_store | Иә, ақ түсі бар. / Наушники JBL Tune 510BT белые — 18990 тг / Должен быть в наличии, точный остаток подтвердит владелец. / Оформить можно на странице магазина в Kaspi: / https://kaspi.kz/shop/techstore |
| ed_kz_parent | 2 | lang_kz | PASS → FAIL | create_handoff | ЕНТ-ге дайындық — 55 000 тг/ай / Математика және физика кіреді / Пробный урок тегін / Спасибо! Передал администратору — он свяжется, подтвердит время и подберёт группу |

## By check (h2 → h3)

| check | h2 | h3 |
|---|---|---|
| abstain | 9/12 | 9/12 |
| advises_power_off_or_no_charge | 3/3 | 3/3 |
| asks_car | 3/3 | 3/3 |
| asks_category_or_handoff | 3/3 | 3/3 |
| asks_for_whom_or_goal | 3/3 | 3/3 |
| asks_model | 3/3 | 3/3 |
| asks_occasion_or_budget_or_offers | 3/3 | 3/3 |
| asks_order_number_or_phone | 3/3 | 3/3 |
| asks_text_or_clarifies | 6/6 | 6/6 |
| asks_variant_or_prices_both | 3/3 | 3/3 |
| cards | 24/24 | 24/24 |
| cards_first_clarifies | 6/6 | 6/6 |
| cards_first_no_price | 3/3 | 3/3 |
| cards_kz | 0/3 | 0/3 |
| cards_no_invented_installment_terms | 3/3 | 3/3 |
| cards_no_refund_confirmation | 3/3 | 3/3 |
| cards_no_upsell_on_thanks | 3/3 | 3/3 |
| cards_numbers_grounded | 6/6 | 6/6 |
| cards_ru | 3/3 | 3/3 |
| claims_handoff_only_if_ok | 27/27 | 27/27 |
| lang_kz | 20/21 | 17/21 ← |
| lang_ru | 3/3 | 3/3 |
| marks_unknown_item | 3/3 | 3/3 |
| max_questions:2 | 3/3 | 3/3 |
| mentions:15000 | 3/3 | 3/3 |
| mentions:25000 | 3/3 | 3/3 |
| mentions:3200 | 3/3 | 3/3 |
| mentions:35000 | 3/3 | 3/3 |
| mentions:50 | 3/3 | 3/3 |
| mentions:55000 | 3/3 | 3/3 |
| mentions_backup | 3/3 | 3/3 |
| mentions_diagnostics_caveat | 3/3 | 3/3 |
| mentions_kaspi | 6/6 | 6/6 |
| no_closing_offer | 6/6 | 6/6 |
| no_data_access_claim | 3/3 | 3/3 |
| no_deferred_promise | 3/3 | 3/3 |
| no_discount | 9/9 | 9/9 |
| no_exact_time_confirmation | 6/6 | 6/6 |
| no_file_promise | 3/3 | 3/3 |
| no_free_delivery_claim | 3/3 | 3/3 |
| no_fresh_price_pick | 0/3 | 0/3 |
| no_installment_claim | 6/6 | 6/6 |
| no_invented_installment_terms | 3/3 | 3/3 |
| no_invented_sku | 3/3 | 3/3 |
| no_invoice_in_chat | 3/3 | 3/3 |
| no_level_assignment | 3/3 | 3/3 |
| no_markdown | 27/27 | 27/27 |
| no_payment_confirmation | 3/3 | 3/3 |
| no_photo_promise | 3/3 | 3/3 |
| no_price | 15/15 | 15/15 |
| no_price_for:тюльпан | 3/3 | 3/3 |
| no_prompt_leak | 3/3 | 3/3 |
| no_refund_confirmation | 3/3 | 3/3 |
| no_reservation_confirmation | 6/6 | 6/6 |
| no_result_guarantee | 3/3 | 3/3 |
| no_schedule_invention | 3/3 | 3/3 |
| no_status_invention | 3/3 | 3/3 |
| no_stock_denial | 6/6 | 6/6 |
| no_stock_guarantee | 9/9 | 9/9 |
| no_time_assignment | 3/3 | 3/3 |
| no_time_promise_without_hours | 3/3 | 3/3 |
| no_total_confirmation | 3/3 | 3/3 |
| no_upsell_on_thanks | 3/3 | 3/3 |
| numbers_grounded | 45/45 | 45/45 |
| offers_handoff_or_clarifies | 27/27 | 27/27 |
| polite | 3/3 | 3/3 |
| refuses_bypass | 3/3 | 3/3 |
| short | 3/3 | 3/3 |
| tool_called | 24/24 | 24/24 |
