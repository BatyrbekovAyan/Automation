# Eval pt-now-pl-n8n.h11 — scenarios v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

**Rescored offline** from `Tools/n8n/eval/results/pt-now-pl-n8n.json` with harness **h11** — no new generation; the answers are the ones generated for `Tools/n8n/eval/results/pt-now-pl-n8n.json` (prompts `f0c81168`, auto model `gpt-5.4-mini`, max_tokens 200).

8 scenarios / 64 runs (64 auto, 0 panel).
**h11: pass 6 · flaky 2 · fail 0  →  h11: pass 7 · flaky 1 · fail 0** · 3 verdicts changed.
Panel runs: 0 recomputed, 0 carried unchanged (no saved HTTP status / whole body; h3 changed no panel check). Auto answers at the old 600-char save limit: 0.
Harness h11 (2026-10-29: scenario fields products_override / services_override (scenario_knowledge) for the price that lives ONLY in the price list — set v1.8 adds the pl_* scenarios, every v1.7 scenario composes byte for byte; --request n8n / a side «REF@n8n» sends the request the bot's n8n sends (developer message, the template's tools as n8n builds them, strict:false, response_format text), every run records the snapshot that answered; AMOUNT never spans a line break («SK20R11⏎4200 тг» is 4200) — rescored, it moves no saved verdict of v1.3–v1.7; on top of h10 (2026-10-13: three off-topic classes — names_scope (рамка + имя бизнеса), no_refusal_on_greeting, honest_assistant, spam_minimal; panel cards_eq_1_refusal, cards_name_business; set v1.7 adds the scenarios for them, knowledge.<vertical>.businessName rides into About Business as «Название:» like the app writes it; every earlier check unchanged; on top of h9 (2026-10-13: panel checks cards_first_no_decision:<topic> — card 1 neither grants nor refuses a condition the data does not state — cards_absent:<regex> — no card states a rule absent from the data — and cards_first_matches:<regex>; auto check matches:<regex>; set v1.6 adds the scenarios for them; every earlier check unchanged; on top of h8 (2026-10-12, T11b: interleaved schedule — every scenario's repeats spread over the whole run, round-robin over the scenarios, the sides of one repeat back to back with the first side rotating (--side / --plan run the sides of a comparison as ONE schedule); set v1.5 adds no_tool:create_handoff wherever the scenario does not call for a handoff; no check function changed; on top of h7 (2026-10-12, T11 Q1 B + V6-unknown: scenario roles `owner` / `bot_unknown` rendered with the memory marks the writers store at --prompts-ref (apply-history-handoff.py; none before T6); no_marker_leak, no_tool:<name>, not_says_already, no_delivery_doubt; on top of h6 2026-10-09, Q1: not_mentions:X — X stated in no un-refused clause and not merely quoted as the client's words, for the client-time echo; mentions:H:MM reads a time of day; on top of h5 2026-10-08, V9-r2: no_conflict_claim / no_price_caveat for a price list that AGREES with the catalog; on top of h4 2026-10-07 V9: «расхождение» + negated conflict, zero-price / elliptical free delivery, «итого по N позициям» partial, «нужен» / the car or device by name, a word inside the installment claim, Kazakh = no Russian sentence, «18,000»; h3 2026-09-27 R14; checks pinned by test_checks.py)))

| scenario | vertical | mode | pass rate (h2 → h3) | failed checks (h3) |
|---|---|---|---|---|
| pl_51_roses_flowers | flowers | auto | 1.00 → 1.00 (8×) |  |
| pl_51_roses_kz_flowers | flowers | auto | 1.00 → 1.00 (8×) |  |
| pl_iphone13_screen_phone_repair | phone_repair | auto | 1.00 → 1.00 (8×) |  |
| pl_ielts_individual_education | education | auto | 1.00 → 1.00 (8×) |  |
| pl_spark_plugs_auto_parts | auto_parts | auto | 0.62 → 1.00 (8×) **changed** |  |
| pl_empty_plain_flowers | flowers | auto | 1.00 → 1.00 (8×) |  |
| pl_empty_memory_flowers | flowers | auto | 1.00 → 1.00 (8×) |  |
| pl_empty_reask_flowers | flowers | auto | 0.88 → 0.88 (8×) | mentions:18:00 |

## Verdict changes (3)

Every answer whose verdict differs between the two harnesses, with the saved text.

| scenario | run | check | h2 → h3 | tools called | answer |
|---|---|---|---|---|---|
| pl_spark_plugs_auto_parts | 1 | numbers_grounded | FAIL → PASS | supabase_vector_store | Да, есть. / Свечи зажигания Denso SK20R11 / 4200 тг/шт |
| pl_spark_plugs_auto_parts | 6 | numbers_grounded | FAIL → PASS | supabase_vector_store | Да, есть. / Свечи зажигания Denso SK20R11 / 4200 тг/шт |
| pl_spark_plugs_auto_parts | 8 | numbers_grounded | FAIL → PASS | supabase_vector_store | Да, есть. / Свечи зажигания Denso SK20R11 / 4200 тг/шт |

## By check (h2 → h3)

| check | h2 | h3 |
|---|---|---|
| lang_kz | 8/8 | 8/8 |
| lang_ru | 56/56 | 56/56 |
| mentions:12000 | 8/8 | 8/8 |
| mentions:18500 | 24/24 | 24/24 |
| mentions:18:00 | 7/8 | 7/8 |
| mentions:4200 | 8/8 | 8/8 |
| mentions:48000 | 16/16 | 16/16 |
| mentions:52000 | 8/8 | 8/8 |
| mentions_diagnostics_caveat | 8/8 | 8/8 |
| no_closing_offer | 64/64 | 64/64 |
| no_markdown | 64/64 | 64/64 |
| no_marker_leak | 64/64 | 64/64 |
| no_tool:create_handoff | 64/64 | 64/64 |
| numbers_grounded | 61/64 | 64/64 ← |
