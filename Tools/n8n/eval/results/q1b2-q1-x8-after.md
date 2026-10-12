# Eval q1b2-q1-x8-after — v1.5 (2026-10-12: T11b — no_tool:create_handoff on every auto scenario whose question the business data answers and whose design calls for no handoff (34: a6_/q1p_ as in v1.4 + 25 more, among them ws_kz_min_order and ws_list_of_items); every auto scenario is classified in test_checks.py (NO_HANDOFF / HANDOFF_IN_DESIGN, each with its reason); roles and all other checks as v1.4; harness h8 — interleaved repeats)

prompts: `worktree` · side `after` · block `q1-x8` of plan `Tools/n8n/eval/plans/q1b2.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 14 scenarios / 112 runs · 777 s (whole schedule, 4 worker(s))
pass 11 · flaky 3 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 557304 in (cached 504320) + 4129 out (reasoning 0) ≈ $0.0961 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1456 ms, p90 3141 ms, max 4621 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| a6_owner_agreement_flowers | flowers | auto | 1.00 (8×) |  |
| a6_owner_agreement_education | education | auto | 1.00 (8×) |  |
| a6_owner_agreement_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1p_visit_phone_repair | phone_repair | auto | 0.88 (8×) | mentions:Ерлан |
| q1p_trial_education | education | auto | 0.38 (8×) | mentions:204, mentions:сред |
| q1p_early_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_no_agreement_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_no_agreement_education | education | auto | 1.00 (8×) |  |
| q1c_client_time_education | education | auto | 1.00 (8×) |  |
| q1c_client_time_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_client_time_history_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1p_pickup_auto_parts | auto_parts | auto | 0.75 (8×) | mentions:18, mentions:Асхат, no_tool:create_handoff |
| q1p_delivery_flowers | flowers | auto | 1.00 (8×) |  |
| q1p_meeting_wholesale | wholesale | auto | 1.00 (8×) |  |

## By check

| check | pass/total |
|---|---|
| mentions:15 | 24/24 |
| mentions:18 | 6/8 |
| mentions:19 | 8/8 |
| mentions:204 | 3/8 |
| mentions:9 | 8/8 |
| mentions:Асхат | 7/8 |
| mentions:Бекзат | 8/8 |
| mentions:Ерлан | 7/8 |
| mentions:Марин | 32/32 |
| mentions:Санжар | 8/8 |
| mentions:Складская | 8/8 |
| mentions:двор | 8/8 |
| mentions:сред | 3/8 |
| no_exact_time_confirmation | 40/40 |
| no_marker_leak | 112/112 |
| no_time_assignment | 40/40 |
| no_tool:create_handoff | 71/72 |
| not_mentions:15:00 | 24/24 |
| offers_handoff_or_clarifies | 40/40 |
