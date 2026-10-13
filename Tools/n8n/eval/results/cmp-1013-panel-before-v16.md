# Eval cmp-1013-panel-before-v16 — v1.6 (2026-10-13: four panel scenarios for a condition the data does not state — pn_discount_unknown, pn_deferred_unknown, pn_reserve_unknown, pn_return_unknown: card 1 neither grants nor refuses it (cards_first_no_decision), no card states a rule absent from the data (cards_absent); harness h9; every v1.5 scenario and check unchanged)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `gpt-4o-mini` · max_tokens 200 · 16 scenarios / 80 runs · 181 s (whole schedule, 1 worker(s))
pass 12 · flaky 1 · fail 3 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 0 in (cached 0) + 0 out (reasoning 0) ≈ $0.0 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): panel median 2355 ms, p90 3034 ms, max 5861 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pn_personal_congrats | flowers | panel | 1.00 (5×) |  |
| pn_emotional_reaction | kaspi_seller | panel | 1.00 (5×) |  |
| pn_go_to_cinema | auto_parts | panel | 1.00 (5×) |  |
| pn_thanks_in_business | flowers | panel | 1.00 (5×) |  |
| pn_price_question | flowers | panel | 1.00 (5×) |  |
| pn_kz_question | phone_repair | panel | 1.00 (5×) |  |
| pn_autoparts_no_car | auto_parts | panel | 0.60 (5×) | cards_first_clarifies, cards_first_no_price |
| pn_installment_kaspi | kaspi_seller | panel | 1.00 (5×) |  |
| pn_mixed_lang | wholesale | panel | 1.00 (5×) |  |
| pn_complaint | kaspi_seller | panel | 1.00 (5×) |  |
| pn_spam | education | panel | 1.00 (5×) |  |
| pn_media_only | phone_repair | panel | 1.00 (5×) |  |
| pn_discount_unknown | wholesale | panel | 0.00 (5×) | cards_first_no_decision:скид |
| pn_deferred_unknown | wholesale | panel | 0.00 (5×) | cards_first_no_decision:отсроч|рассроч |
| pn_reserve_unknown | kaspi_seller | panel | 0.00 (5×) | cards_first_no_decision:отлож|брон|резерв |
| pn_return_unknown | kaspi_seller | panel | 1.00 (5×) |  |

## By check

| check | pass/total |
|---|---|
| abstain | 20/20 |
| cards | 60/60 |
| cards_absent:предоплат | 10/10 |
| cards_first_clarifies | 8/10 |
| cards_first_no_decision:возврат|верн | 5/5 |
| cards_first_no_decision:отлож|брон|резерв | 0/5 |
| cards_first_no_decision:отсроч|рассроч | 0/5 |
| cards_first_no_decision:скид | 0/5 |
| cards_first_no_price | 3/5 |
| cards_kz | 5/5 |
| cards_no_invented_installment_terms | 5/5 |
| cards_no_refund_confirmation | 10/10 |
| cards_no_upsell_on_thanks | 5/5 |
| cards_numbers_grounded | 10/10 |
| cards_ru | 5/5 |
