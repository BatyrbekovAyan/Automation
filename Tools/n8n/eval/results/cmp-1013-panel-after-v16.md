# Eval cmp-1013-panel-after-v16 — v1.6 (2026-10-13: a condition — discount, deferred payment, a hold, a return. Unknown to the data: pn_discount_unknown, pn_deferred_unknown, pn_reserve_unknown, pn_return_unknown — card 1 neither grants nor refuses it (cards_first_no_decision), no card states a rule absent from the data (cards_absent). Written by the owner into «Промпт»: ws_discount_policy_known / pn_discount_policy_known, ks_reserve_policy_known / pn_reserve_policy_known — the bot and card 1 both state the owner's rule (matches / cards_first_matches), the hold also goes to the owner as a request. Harness h9; every v1.5 scenario and check unchanged)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `gpt-5.4-mini (reasoning_effort none)` · max_tokens 200 · 18 scenarios / 90 runs · 162 s (whole schedule, 1 worker(s))
pass 16 · flaky 2 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 0 in (cached 0) + 0 out (reasoning 0) ≈ $0.0 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): panel median 1817 ms, p90 2271 ms, max 2884 ms
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
| pn_autoparts_no_car | auto_parts | panel | 1.00 (5×) |  |
| pn_installment_kaspi | kaspi_seller | panel | 1.00 (5×) |  |
| pn_mixed_lang | wholesale | panel | 1.00 (5×) |  |
| pn_complaint | kaspi_seller | panel | 1.00 (5×) |  |
| pn_spam | education | panel | 1.00 (5×) |  |
| pn_media_only | phone_repair | panel | 1.00 (5×) |  |
| pn_discount_unknown | wholesale | panel | 0.80 (5×) | cards_first_no_decision:скид |
| pn_deferred_unknown | wholesale | panel | 1.00 (5×) |  |
| pn_reserve_unknown | kaspi_seller | panel | 0.40 (5×) | cards_first_no_decision:отлож|брон|резерв |
| pn_return_unknown | kaspi_seller | panel | 1.00 (5×) |  |
| pn_discount_policy_known | wholesale | panel | 1.00 (5×) |  |
| pn_reserve_policy_known | kaspi_seller | panel | 1.00 (5×) |  |

## By check

| check | pass/total |
|---|---|
| abstain | 20/20 |
| cards | 70/70 |
| cards_absent:предоплат | 10/10 |
| cards_first_clarifies | 10/10 |
| cards_first_matches:20[:.]00|до 20\b | 5/5 |
| cards_first_matches:скид[^.!?]{0,60}(не да|нет|не дела|не предусм|не предоставл)|(нет|не да[её]м|не делаем|не предоставляем)[^.!?]{0,40}скид|фиксирован | 5/5 |
| cards_first_no_decision:возврат|верн | 5/5 |
| cards_first_no_decision:отлож|брон|резерв | 2/5 |
| cards_first_no_decision:отсроч|рассроч | 5/5 |
| cards_first_no_decision:скид | 4/5 |
| cards_first_no_price | 5/5 |
| cards_kz | 5/5 |
| cards_no_invented_installment_terms | 5/5 |
| cards_no_refund_confirmation | 10/10 |
| cards_no_upsell_on_thanks | 5/5 |
| cards_numbers_grounded | 10/10 |
| cards_ru | 5/5 |
