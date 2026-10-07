# Eval v9p-after-x8.h4 — scenarios v1.1 (2026-09-22: absent-item check names the item; harness h2)

**Rescored offline** from `Tools/n8n/eval/results/v9p-after-x8.json` with harness **h4** — no new generation; the answers are the ones generated for `Tools/n8n/eval/results/v9p-after-x8.json` (prompts `worktree`, auto model `gpt-5.4-mini`, max_tokens 200).

12 scenarios / 96 runs (0 auto, 96 panel).
**h3: pass 11 · flaky 1 · fail 0  →  h4: pass 12 · flaky 0 · fail 0** · 1 verdicts changed.
Panel runs: 96 recomputed, 0 carried unchanged (no saved HTTP status / whole body; h3 changed no panel check). Auto answers at the old 600-char save limit: 0.
Harness h4 (2026-10-07, V9: «расхождение» + negated conflict, zero-price / elliptical free delivery, «итого по N позициям» partial, «нужен» / the car or device by name, a word inside the installment claim, Kazakh = no Russian sentence, «18,000»; on top of h3 2026-09-27 R14; checks pinned by test_checks.py)

| scenario | vertical | mode | pass rate (h2 → h3) | failed checks (h3) |
|---|---|---|---|---|
| pn_personal_congrats | flowers | panel | 1.00 → 1.00 (8×) |  |
| pn_emotional_reaction | kaspi_seller | panel | 1.00 → 1.00 (8×) |  |
| pn_go_to_cinema | auto_parts | panel | 1.00 → 1.00 (8×) |  |
| pn_thanks_in_business | flowers | panel | 1.00 → 1.00 (8×) |  |
| pn_price_question | flowers | panel | 0.88 → 1.00 (8×) **changed** |  |
| pn_kz_question | phone_repair | panel | 1.00 → 1.00 (8×) |  |
| pn_autoparts_no_car | auto_parts | panel | 1.00 → 1.00 (8×) |  |
| pn_installment_kaspi | kaspi_seller | panel | 1.00 → 1.00 (8×) |  |
| pn_mixed_lang | wholesale | panel | 1.00 → 1.00 (8×) |  |
| pn_complaint | kaspi_seller | panel | 1.00 → 1.00 (8×) |  |
| pn_spam | education | panel | 1.00 → 1.00 (8×) |  |
| pn_media_only | phone_repair | panel | 1.00 → 1.00 (8×) |  |

## Verdict changes (1)

Every answer whose verdict differs between the two harnesses, with the saved text.

| scenario | run | check | h2 → h3 | tools called | answer |
|---|---|---|---|---|---|
| pn_price_question | 4 | cards_numbers_grounded | FAIL → PASS | — | {"v": 1, "requestSeq": 1, "suggestions": [{"text": "Букет пионов стоит 18,000 тг. Если хотите, могу помочь с заказом!", "label": "Цена", "move": "Ответ"}, {"text": "Какой размер букета вас интересует? Могу предложить различные варианты!", "label": "Размер букета", "move": "Уточнить"}, {"text": "У нас есть букеты из роз и пионов. Если хотите, могу предложить их вместе!", "label": "Другие варианты", "move": "Вариант"}, {"text": "Если хотите заказать букет, напишите, когда и куда доставить!", "label": "Доставка", "move": "К заказу"}]} |

## By check (h2 → h3)

| check | h2 | h3 |
|---|---|---|
| abstain | 32/32 | 32/32 |
| cards | 64/64 | 64/64 |
| cards_first_clarifies | 16/16 | 16/16 |
| cards_first_no_price | 8/8 | 8/8 |
| cards_kz | 8/8 | 8/8 |
| cards_no_invented_installment_terms | 8/8 | 8/8 |
| cards_no_refund_confirmation | 8/8 | 8/8 |
| cards_no_upsell_on_thanks | 8/8 | 8/8 |
| cards_numbers_grounded | 15/16 | 16/16 ← |
| cards_ru | 8/8 | 8/8 |
