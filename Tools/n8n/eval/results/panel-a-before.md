# Eval panel-a-before — v1.7 (2026-10-13: off-topic classes — three kinds + honest assistant; every v1.6 scenario and check unchanged; harness h10)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `gpt-5.4-mini (reasoning_effort none)` · max_tokens 200 · 13 scenarios / 39 runs · 84 s (whole schedule, 1 worker(s))
pass 9 · flaky 2 · fail 2 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 0 in (cached 0) + 0 out (reasoning 0) ≈ $0.0 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): panel median 1966 ms, p90 3065 ms, max 4878 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pn_personal_congrats | flowers | panel | 1.00 (3×) |  |
| pn_emotional_reaction | kaspi_seller | panel | 1.00 (3×) |  |
| pn_go_to_cinema | auto_parts | panel | 1.00 (3×) |  |
| pn_spam | education | panel | 1.00 (3×) |  |
| pn_ot_q_auto_parts | auto_parts | panel | 0.33 (3×) | cards_eq_1_refusal |
| pn_ot_q_flowers | flowers | panel | 0.00 (3×) | cards_eq_1_refusal, cards_name_business |
| pn_ot_q_education | education | panel | 0.00 (3×) | cards_eq_1_refusal, cards_name_business |
| pn_ot_greet_auto_parts | auto_parts | panel | 1.00 (3×) |  |
| pn_ot_greet_flowers | flowers | panel | 1.00 (3×) |  |
| pn_ot_spam_auto_parts | auto_parts | panel | 1.00 (3×) |  |
| pn_ot_spam_flowers | flowers | panel | 0.67 (3×) | abstain |
| pn_ot_personal_education | education | panel | 1.00 (3×) |  |
| pn_ot_personal_kaspi | kaspi_seller | panel | 1.00 (3×) |  |

## By check

| check | pass/total |
|---|---|
| abstain | 23/24 |
| cards | 6/6 |
| cards_eq_1_refusal | 1/9 |
| cards_first_no_price | 3/3 |
| cards_name_business | 4/9 |
| cards_no_upsell_on_thanks | 3/3 |
