# Eval v9r2-after-panel-x8 — v1.2 (2026-10-08: V9-r2 ap_price_agrees_analogues — the price list agrees with the catalog, analogue rows in the same chunk; harness h5)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `gpt-4o-mini` · max_tokens 200 · 12 scenarios / 96 runs · 313 s
pass 12 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 0 in (cached 0) + 0 out (reasoning 0) ≈ $0.0 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): panel median 3087 ms, p90 4693 ms, max 18916 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at the ref)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pn_personal_congrats | flowers | panel | 1.00 (8×) |  |
| pn_emotional_reaction | kaspi_seller | panel | 1.00 (8×) |  |
| pn_go_to_cinema | auto_parts | panel | 1.00 (8×) |  |
| pn_thanks_in_business | flowers | panel | 1.00 (8×) |  |
| pn_price_question | flowers | panel | 1.00 (8×) |  |
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
| cards_numbers_grounded | 16/16 |
| cards_ru | 8/8 |
