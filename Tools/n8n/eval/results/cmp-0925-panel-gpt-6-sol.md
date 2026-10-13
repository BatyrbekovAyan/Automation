# Eval cmp-0925-panel-gpt-6-sol — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `gpt-6-sol (reasoning_effort none)` · max_tokens 200 · 12 scenarios / 36 runs · 121 s
pass 12 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 0 in (cached 0) + 0 out (reasoning 0) ≈ $0.0 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): panel median 3852 ms, p90 4436 ms, max 4868 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pn_personal_congrats | flowers | panel | 1.00 (3×) |  |
| pn_emotional_reaction | kaspi_seller | panel | 1.00 (3×) |  |
| pn_go_to_cinema | auto_parts | panel | 1.00 (3×) |  |
| pn_thanks_in_business | flowers | panel | 1.00 (3×) |  |
| pn_price_question | flowers | panel | 1.00 (3×) |  |
| pn_kz_question | phone_repair | panel | 1.00 (3×) |  |
| pn_autoparts_no_car | auto_parts | panel | 1.00 (3×) |  |
| pn_installment_kaspi | kaspi_seller | panel | 1.00 (3×) |  |
| pn_mixed_lang | wholesale | panel | 1.00 (3×) |  |
| pn_complaint | kaspi_seller | panel | 1.00 (3×) |  |
| pn_spam | education | panel | 1.00 (3×) |  |
| pn_media_only | phone_repair | panel | 1.00 (3×) |  |

## By check

| check | pass/total |
|---|---|
| abstain | 12/12 |
| cards | 24/24 |
| cards_first_clarifies | 6/6 |
| cards_first_no_price | 3/3 |
| cards_kz | 3/3 |
| cards_no_invented_installment_terms | 3/3 |
| cards_no_refund_confirmation | 3/3 |
| cards_no_upsell_on_thanks | 3/3 |
| cards_numbers_grounded | 6/6 |
| cards_ru | 3/3 |
