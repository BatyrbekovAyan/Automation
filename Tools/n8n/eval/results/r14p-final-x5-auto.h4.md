# Eval r14p-final-x5-auto.h4 — scenarios v1.1 (2026-09-22: absent-item check names the item; harness h2)

**Rescored offline** from `Tools/n8n/eval/results/r14p-final-x5-auto.json` with harness **h4** — no new generation; the answers are the ones generated for `Tools/n8n/eval/results/r14p-final-x5-auto.json` (prompts `worktree`, auto model `gpt-5.4-mini`, max_tokens 200).

14 scenarios / 70 runs (70 auto, 0 panel).
**h3: pass 7 · flaky 6 · fail 1  →  h4: pass 6 · flaky 7 · fail 1** · 2 verdicts changed.
Panel runs: 0 recomputed, 0 carried unchanged (no saved HTTP status / whole body; h3 changed no panel check). Auto answers at the old 600-char save limit: 0.
Harness h4 (2026-10-07, V9: «расхождение» + negated conflict, zero-price / elliptical free delivery, «итого по N позициям» partial, «нужен» / the car or device by name, a word inside the installment claim, Kazakh = no Russian sentence, «18,000»; on top of h3 2026-09-27 R14; checks pinned by test_checks.py)

| scenario | vertical | mode | pass rate (h2 → h3) | failed checks (h3) |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 → 1.00 (5×) |  |
| ap_price_with_car | auto_parts | auto | 1.00 → 1.00 (5×) |  |
| ap_sku_exact_kz | auto_parts | auto | 1.00 → 1.00 (5×) |  |
| ap_source_conflict | auto_parts | auto | 0.00 → 0.00 (5×) | no_fresh_price_pick |
| ws_list_of_items | wholesale | auto | 0.80 → 0.80 (5×) | no_total_confirmation |
| fl_absent_item | flowers | auto | 0.80 → 0.80 (5×) | offers_handoff_or_clarifies |
| ks_installment_terms | kaspi_seller | auto | 1.00 → 1.00 (5×) |  |
| ks_where_is_order | kaspi_seller | auto | 0.80 → 0.80 (5×) | asks_order_number_or_phone |
| ks_kz_availability | kaspi_seller | auto | 1.00 → 0.60 (5×) **changed** | lang_kz |
| ed_kz_parent | education | auto | 1.00 → 1.00 (5×) |  |
| pr_price_no_model | phone_repair | auto | 0.80 → 0.80 (5×) | asks_model, no_price |
| pr_data_privacy | phone_repair | auto | 0.80 → 0.80 (5×) | mentions_backup |
| pr_kz_battery | phone_repair | auto | 0.60 → 0.60 (5×) | lang_kz |
| gen_owner_pref_vs_fact | auto_parts | auto | 1.00 → 1.00 (5×) |  |

## Verdict changes (2)

Every answer whose verdict differs between the two harnesses, with the saved text.

| scenario | run | check | h2 → h3 | tools called | answer |
|---|---|---|---|---|---|
| ks_kz_availability | 2 | lang_kz | PASS → FAIL | supabase_vector_store | Иә, ақ түсі бар. / Наушники JBL Tune 510BT белые — 18990 тг / Должен быть в наличии, точный остаток подтвердит владелец. / Оформить можно на странице магазина Kaspi: https://kaspi.kz/shop/techstore |
| ks_kz_availability | 5 | lang_kz | PASS → FAIL | supabase_vector_store | Иә, ақ түсі бар. / Наушники JBL Tune 510BT белые — 18990 тг / Оформить можно на странице магазина Kaspi: https://kaspi.kz/shop/techstore |

## By check (h2 → h3)

| check | h2 | h3 |
|---|---|---|
| asks_car | 5/5 | 5/5 |
| asks_model | 4/5 | 4/5 |
| asks_order_number_or_phone | 4/5 | 4/5 |
| lang_kz | 18/20 | 16/20 ← |
| lang_ru | 5/5 | 5/5 |
| marks_unknown_item | 5/5 | 5/5 |
| mentions:15000 | 5/5 | 5/5 |
| mentions:3200 | 5/5 | 5/5 |
| mentions:55000 | 5/5 | 5/5 |
| mentions_backup | 4/5 | 4/5 |
| mentions_kaspi | 5/5 | 5/5 |
| no_closing_offer | 5/5 | 5/5 |
| no_data_access_claim | 5/5 | 5/5 |
| no_free_delivery_claim | 5/5 | 5/5 |
| no_fresh_price_pick | 0/5 | 0/5 |
| no_invented_installment_terms | 5/5 | 5/5 |
| no_invented_sku | 5/5 | 5/5 |
| no_markdown | 20/20 | 20/20 |
| no_price | 9/10 | 9/10 |
| no_price_for:тюльпан | 5/5 | 5/5 |
| no_status_invention | 5/5 | 5/5 |
| no_stock_denial | 5/5 | 5/5 |
| no_stock_guarantee | 10/10 | 10/10 |
| no_total_confirmation | 4/5 | 4/5 |
| numbers_grounded | 25/25 | 25/25 |
| offers_handoff_or_clarifies | 4/5 | 4/5 |
