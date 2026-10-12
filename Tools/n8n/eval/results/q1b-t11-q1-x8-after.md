# Eval q1b-t11-q1-x8-after — v1.4 (2026-10-12: T11 — Q1 B + V6-unknown: the business turn of a6_/q1p_/q1c_ is the OWNER's reply (role `owner`), except the bot's own reply in q1c_client_time_history_phone_repair; u1_–u4_ — the bot's earlier reply of UNKNOWN delivery (role `bot_unknown`); no_tool:create_handoff on a6_/q1p_ (the c1 lesson), no_marker_leak on every auto scenario; harness h7)

prompts: `70518dd0` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 14 scenarios / 112 runs · 204 s
pass 9 · flaky 5 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 555262 in (cached 484608) + 4180 out (reasoning 0) ≈ $0.1081 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1440 ms, p90 3510 ms, max 5147 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 70518dd0)
memory marks (apply-history-handoff.py at `70518dd0`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| a6_owner_agreement_flowers | flowers | auto | 1.00 (8×) |  |
| a6_owner_agreement_education | education | auto | 1.00 (8×) |  |
| a6_owner_agreement_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1p_visit_phone_repair | phone_repair | auto | 0.50 (8×) | mentions:Ерлан, mentions:двор |
| q1p_trial_education | education | auto | 0.88 (8×) | mentions:204, mentions:сред |
| q1p_early_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_no_agreement_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_no_agreement_education | education | auto | 1.00 (8×) |  |
| q1c_client_time_education | education | auto | 0.88 (8×) | not_mentions:15:00 |
| q1c_client_time_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_client_time_history_phone_repair | phone_repair | auto | 0.75 (8×) | not_mentions:15:00 |
| q1p_pickup_auto_parts | auto_parts | auto | 0.88 (8×) | mentions:18, mentions:Асхат, no_tool:create_handoff |
| q1p_delivery_flowers | flowers | auto | 1.00 (8×) |  |
| q1p_meeting_wholesale | wholesale | auto | 1.00 (8×) |  |

## By check

| check | pass/total |
|---|---|
| mentions:15 | 24/24 |
| mentions:18 | 7/8 |
| mentions:19 | 8/8 |
| mentions:204 | 7/8 |
| mentions:9 | 8/8 |
| mentions:Асхат | 7/8 |
| mentions:Бекзат | 8/8 |
| mentions:Ерлан | 4/8 |
| mentions:Марин | 32/32 |
| mentions:Санжар | 8/8 |
| mentions:Складская | 8/8 |
| mentions:двор | 4/8 |
| mentions:сред | 7/8 |
| no_exact_time_confirmation | 40/40 |
| no_marker_leak | 112/112 |
| no_time_assignment | 40/40 |
| no_tool:create_handoff | 71/72 |
| not_mentions:15:00 | 21/24 |
| offers_handoff_or_clarifies | 40/40 |
