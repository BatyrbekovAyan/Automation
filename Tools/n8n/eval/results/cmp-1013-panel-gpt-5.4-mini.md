# Eval cmp-1013-panel-gpt-5.4-mini — v1.5 (2026-10-12: T11b — no_tool:create_handoff on every auto scenario whose question the business data answers and whose design calls for no handoff (34: a6_/q1p_ as in v1.4 + 25 more, among them ws_kz_min_order and ws_list_of_items); every auto scenario is classified in test_checks.py (NO_HANDOFF / HANDOFF_IN_DESIGN, each with its reason); roles and all other checks as v1.4; harness h8 — interleaved repeats)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `gpt-5.4-mini (reasoning_effort none)` · max_tokens 200 · 12 scenarios / 60 runs · 113 s (whole schedule, 1 worker(s))
pass 11 · flaky 1 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 0 in (cached 0) + 0 out (reasoning 0) ≈ $0.0 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): panel median 1967 ms, p90 2344 ms, max 4107 ms
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
| pn_spam | education | panel | 0.40 (5×) | abstain |
| pn_media_only | phone_repair | panel | 1.00 (5×) |  |

## By check

| check | pass/total |
|---|---|
| abstain | 17/20 |
| cards | 40/40 |
| cards_first_clarifies | 10/10 |
| cards_first_no_price | 5/5 |
| cards_kz | 5/5 |
| cards_no_invented_installment_terms | 5/5 |
| cards_no_refund_confirmation | 5/5 |
| cards_no_upsell_on_thanks | 5/5 |
| cards_numbers_grounded | 10/10 |
| cards_ru | 5/5 |
