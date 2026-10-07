# Eval v9p-before-x8.h4 — scenarios v1.1 (2026-09-22: absent-item check names the item; harness h2)

**Rescored offline** from `Tools/n8n/eval/results/v9p-before-x8.json` with harness **h4** — no new generation; the answers are the ones generated for `Tools/n8n/eval/results/v9p-before-x8.json` (prompts `worktree`, auto model `gpt-5.4-mini`, max_tokens 200).

12 scenarios / 96 runs (0 auto, 96 panel).
**h3: pass 9 · flaky 1 · fail 2  →  h4: pass 9 · flaky 1 · fail 2** · 1 verdicts changed.
Panel runs: 96 recomputed, 0 carried unchanged (no saved HTTP status / whole body; h3 changed no panel check). Auto answers at the old 600-char save limit: 0.
Harness h4 (2026-10-07, V9: «расхождение» + negated conflict, zero-price / elliptical free delivery, «итого по N позициям» partial, «нужен» / the car or device by name, a word inside the installment claim, Kazakh = no Russian sentence, «18,000»; on top of h3 2026-09-27 R14; checks pinned by test_checks.py)

| scenario | vertical | mode | pass rate (h2 → h3) | failed checks (h3) |
|---|---|---|---|---|
| pn_personal_congrats | flowers | panel | 1.00 → 1.00 (8×) |  |
| pn_emotional_reaction | kaspi_seller | panel | 0.00 → 0.00 (8×) | abstain |
| pn_go_to_cinema | auto_parts | panel | 1.00 → 1.00 (8×) |  |
| pn_thanks_in_business | flowers | panel | 1.00 → 1.00 (8×) |  |
| pn_price_question | flowers | panel | 1.00 → 1.00 (8×) |  |
| pn_kz_question | phone_repair | panel | 0.62 → 0.50 (8×) **changed** | cards_kz |
| pn_autoparts_no_car | auto_parts | panel | 0.00 → 0.00 (8×) | cards_first_clarifies, cards_first_no_price |
| pn_installment_kaspi | kaspi_seller | panel | 1.00 → 1.00 (8×) |  |
| pn_mixed_lang | wholesale | panel | 1.00 → 1.00 (8×) |  |
| pn_complaint | kaspi_seller | panel | 1.00 → 1.00 (8×) |  |
| pn_spam | education | panel | 1.00 → 1.00 (8×) |  |
| pn_media_only | phone_repair | panel | 1.00 → 1.00 (8×) |  |

## Verdict changes (1)

Every answer whose verdict differs between the two harnesses, with the saved text.

| scenario | run | check | h2 → h3 | tools called | answer |
|---|---|---|---|---|---|
| pn_kz_question | 2 | cards_kz | PASS → FAIL | — | {"v": 1, "requestSeq": 1, "suggestions": [{"text": "iPhone 11 экранын ауыстыру 35000 тг тұрады. Ремонтқа 1 сағат кетеді. Диагностика тегін, келіп тексертуге болады!", "label": "Кратко", "move": "Ответ"}, {"text": "Замена экрана для iPhone 11 стоит 35000 тг. Мы проводим бесплатную диагностику, и сам ремонт займет около 1 часа. Ждем вас!", "label": "Подробно", "move": "Ответ"}, {"text": "Экран iPhone 11 ауыстыру 35000 тг. Ремонтқа 1 сағат кетеді, диагностика тегін. Қай уақытта келесіз?", "label": "К заказу", "move": "К заказу"}, {"text": "Ремонтқа 35000 тг. Нақты бағасын диагностикадан кейін айтамыз. Келіп тексертуге болады!", "label": "Теплее", "move": "Отложить"}]} |

## By check (h2 → h3)

| check | h2 | h3 |
|---|---|---|
| abstain | 24/32 | 24/32 |
| cards | 64/64 | 64/64 |
| cards_first_clarifies | 8/16 | 8/16 |
| cards_first_no_price | 0/8 | 0/8 |
| cards_kz | 5/8 | 4/8 ← |
| cards_no_invented_installment_terms | 8/8 | 8/8 |
| cards_no_refund_confirmation | 8/8 | 8/8 |
| cards_no_upsell_on_thanks | 8/8 | 8/8 |
| cards_numbers_grounded | 16/16 | 16/16 |
| cards_ru | 8/8 | 8/8 |
