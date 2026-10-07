# Eval v9p-after-x8 — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `gpt-4o-mini` · max_tokens 200 · 12 scenarios / 96 runs · 277 s
pass 11 · flaky 1 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 0 in (cached 0) + 0 out (reasoning 0) ≈ $0.0 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): panel median 3098 ms, p90 4639 ms, max 7007 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pn_personal_congrats | flowers | panel | 1.00 (8×) |  |
| pn_emotional_reaction | kaspi_seller | panel | 1.00 (8×) |  |
| pn_go_to_cinema | auto_parts | panel | 1.00 (8×) |  |
| pn_thanks_in_business | flowers | panel | 1.00 (8×) |  |
| pn_price_question | flowers | panel | 0.88 (8×) | cards_numbers_grounded |
| pn_kz_question | phone_repair | panel | 1.00 (8×) |  |
| pn_autoparts_no_car | auto_parts | panel | 1.00 (8×) |  |
| pn_installment_kaspi | kaspi_seller | panel | 1.00 (8×) |  |
| pn_mixed_lang | wholesale | panel | 1.00 (8×) |  |
| pn_complaint | kaspi_seller | panel | 1.00 (8×) |  |
| pn_spam | education | panel | 1.00 (8×) |  |
| pn_media_only | phone_repair | panel | 1.00 (8×) |  |

## By check

| check | pass/total |
|---|---|
| abstain | 32/32 |
| cards | 64/64 |
| cards_first_clarifies | 16/16 |
| cards_first_no_price | 8/8 |
| cards_kz | 8/8 |
| cards_no_invented_installment_terms | 8/8 |
| cards_no_refund_confirmation | 8/8 |
| cards_no_upsell_on_thanks | 8/8 |
| cards_numbers_grounded | 15/16 |
| cards_ru | 8/8 |
