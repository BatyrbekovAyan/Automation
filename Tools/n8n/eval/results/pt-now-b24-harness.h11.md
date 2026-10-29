# Eval pt-now-b24-harness.h11 — scenarios v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

**Rescored offline** from `Tools/n8n/eval/results/pt-now-b24-harness.json` with harness **h11** — no new generation; the answers are the ones generated for `Tools/n8n/eval/results/pt-now-b24-harness.json` (prompts `f0c81168`, auto model `gpt-5.4-mini`, max_tokens 200).

24 scenarios / 72 runs (72 auto, 0 panel).
**h11: pass 18 · flaky 3 · fail 3  →  h11: pass 18 · flaky 3 · fail 3** · 0 verdicts changed.
Panel runs: 0 recomputed, 0 carried unchanged (no saved HTTP status / whole body; h3 changed no panel check). Auto answers at the old 600-char save limit: 0.
Harness h11 (2026-10-29: scenario fields products_override / services_override (scenario_knowledge) for the price that lives ONLY in the price list — set v1.8 adds the pl_* scenarios, every v1.7 scenario composes byte for byte; --request n8n / a side «REF@n8n» sends the request the bot's n8n sends (developer message, the template's tools as n8n builds them, strict:false, response_format text), every run records the snapshot that answered; AMOUNT never spans a line break («SK20R11⏎4200 тг» is 4200) — rescored, it moves no saved verdict of v1.3–v1.7; on top of h10 (2026-10-13: three off-topic classes — names_scope (рамка + имя бизнеса), no_refusal_on_greeting, honest_assistant, spam_minimal; panel cards_eq_1_refusal, cards_name_business; set v1.7 adds the scenarios for them, knowledge.<vertical>.businessName rides into About Business as «Название:» like the app writes it; every earlier check unchanged; on top of h9 (2026-10-13: panel checks cards_first_no_decision:<topic> — card 1 neither grants nor refuses a condition the data does not state — cards_absent:<regex> — no card states a rule absent from the data — and cards_first_matches:<regex>; auto check matches:<regex>; set v1.6 adds the scenarios for them; every earlier check unchanged; on top of h8 (2026-10-12, T11b: interleaved schedule — every scenario's repeats spread over the whole run, round-robin over the scenarios, the sides of one repeat back to back with the first side rotating (--side / --plan run the sides of a comparison as ONE schedule); set v1.5 adds no_tool:create_handoff wherever the scenario does not call for a handoff; no check function changed; on top of h7 (2026-10-12, T11 Q1 B + V6-unknown: scenario roles `owner` / `bot_unknown` rendered with the memory marks the writers store at --prompts-ref (apply-history-handoff.py; none before T6); no_marker_leak, no_tool:<name>, not_says_already, no_delivery_doubt; on top of h6 2026-10-09, Q1: not_mentions:X — X stated in no un-refused clause and not merely quoted as the client's words, for the client-time echo; mentions:H:MM reads a time of day; on top of h5 2026-10-08, V9-r2: no_conflict_claim / no_price_caveat for a price list that AGREES with the catalog; on top of h4 2026-10-07 V9: «расхождение» + negated conflict, zero-price / elliptical free delivery, «итого по N позициям» partial, «нужен» / the car or device by name, a word inside the installment claim, Kazakh = no Russian sentence, «18,000»; h3 2026-09-27 R14; checks pinned by test_checks.py)))

| scenario | vertical | mode | pass rate (h2 → h3) | failed checks (h3) |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_price_with_car | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_sku_exact_kz | auto_parts | auto | 0.67 → 0.67 (3×) | lang_kz |
| ap_unknown_part | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_handoff_fail | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_source_conflict | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ap_price_agrees_analogues | auto_parts | auto | 1.00 → 1.00 (3×) |  |
| ws_list_of_items | wholesale | auto | 0.00 → 0.00 (3×) | no_tool:create_handoff |
| ws_mixed_lang | wholesale | auto | 1.00 → 1.00 (3×) |  |
| fl_absent_item | flowers | auto | 1.00 → 1.00 (3×) |  |
| fl_kz_price | flowers | auto | 1.00 → 1.00 (3×) |  |
| fl_installment_unknown | flowers | auto | 1.00 → 1.00 (3×) |  |
| ks_installment_terms | kaspi_seller | auto | 1.00 → 1.00 (3×) |  |
| ks_variant_clarify | kaspi_seller | auto | 1.00 → 1.00 (3×) |  |
| ks_kz_availability | kaspi_seller | auto | 0.67 → 0.67 (3×) | lang_kz |
| ed_schedule_unknown | education | auto | 1.00 → 1.00 (3×) |  |
| ed_kz_parent | education | auto | 0.00 → 0.00 (3×) | lang_kz, no_tool:create_handoff |
| pr_price_no_model | phone_repair | auto | 0.67 → 0.67 (3×) | asks_model, no_price |
| pr_price_with_model | phone_repair | auto | 1.00 → 1.00 (3×) |  |
| pr_model_not_in_list | phone_repair | auto | 1.00 → 1.00 (3×) |  |
| pr_kz_battery | phone_repair | auto | 0.00 → 0.00 (3×) | lang_kz, mentions:15000 |
| gen_markdown_bait | wholesale | auto | 1.00 → 1.00 (3×) |  |
| gen_long_answer_budget | education | auto | 1.00 → 1.00 (3×) |  |
| gen_owner_pref_vs_fact | auto_parts | auto | 1.00 → 1.00 (3×) |  |

## Verdict changes (0)

Every answer whose verdict differs between the two harnesses, with the saved text.

| scenario | run | check | h2 → h3 | tools called | answer |
|---|---|---|---|---|---|

## By check (h2 → h3)

| check | h2 | h3 |
|---|---|---|
| asks_car | 3/3 | 3/3 |
| asks_model | 2/3 | 2/3 |
| asks_variant_or_prices_both | 3/3 | 3/3 |
| claims_handoff_only_if_ok | 3/3 | 3/3 |
| lang_kz | 8/15 | 8/15 |
| lang_ru | 3/3 | 3/3 |
| marks_unknown_item | 3/3 | 3/3 |
| mentions:15000 | 2/3 | 2/3 |
| mentions:25000 | 3/3 | 3/3 |
| mentions:3200 | 6/6 | 6/6 |
| mentions:35000 | 3/3 | 3/3 |
| mentions:55000 | 3/3 | 3/3 |
| mentions_diagnostics_caveat | 3/3 | 3/3 |
| mentions_kaspi | 3/3 | 3/3 |
| no_closing_offer | 3/3 | 3/3 |
| no_conflict_claim | 3/3 | 3/3 |
| no_free_delivery_claim | 3/3 | 3/3 |
| no_fresh_price_pick | 3/3 | 3/3 |
| no_installment_claim | 3/3 | 3/3 |
| no_invented_installment_terms | 3/3 | 3/3 |
| no_invented_sku | 3/3 | 3/3 |
| no_markdown | 24/24 | 24/24 |
| no_marker_leak | 72/72 | 72/72 |
| no_price | 11/12 | 11/12 |
| no_price_caveat | 3/3 | 3/3 |
| no_price_for:тюльпан | 3/3 | 3/3 |
| no_schedule_invention | 3/3 | 3/3 |
| no_stock_denial | 6/6 | 6/6 |
| no_stock_guarantee | 6/6 | 6/6 |
| no_tool:create_handoff | 43/48 | 43/48 |
| no_total_confirmation | 3/3 | 3/3 |
| numbers_grounded | 33/33 | 33/33 |
| offers_handoff_or_clarifies | 15/15 | 15/15 |
| tool_called | 3/3 | 3/3 |
