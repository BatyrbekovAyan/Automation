# Eval q1-before-x8 — Q1 design probe (2026-10-09): a6 ×3 verticals + paraphrases + negative controls; knowledge = v1.2

prompts: `d08c345d` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 14 scenarios / 112 runs · 214 s
pass 6 · flaky 3 · fail 5 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 567660 in (cached 514048) + 5565 out (reasoning 0) ≈ $0.1038 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1627 ms, p90 3156 ms, max 3897 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at d08c345d)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| a6_owner_agreement_flowers | flowers | auto | 1.00 (8×) |  |
| a6_owner_agreement_education | education | auto | 0.00 (8×) | mentions:15, mentions:Марин |
| a6_owner_agreement_phone_repair | phone_repair | auto | 0.12 (8×) | mentions:15, mentions:Марин |
| q1p_visit_phone_repair | phone_repair | auto | 0.00 (8×) | mentions:Ерлан, mentions:двор |
| q1p_trial_education | education | auto | 0.00 (8×) | mentions:204, mentions:сред |
| q1p_early_phone_repair | phone_repair | auto | 0.12 (8×) | mentions:9, mentions:Марин |
| q1c_no_agreement_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_no_agreement_education | education | auto | 1.00 (8×) |  |
| q1c_client_time_education | education | auto | 1.00 (8×) |  |
| q1c_client_time_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_client_time_history_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1p_pickup_auto_parts | auto_parts | auto | 0.00 (8×) | mentions:18, mentions:Асхат |
| q1p_delivery_flowers | flowers | auto | 0.00 (8×) | mentions:Санжар |
| q1p_meeting_wholesale | wholesale | auto | 0.88 (8×) | mentions:Бекзат, mentions:Складская |

## By check

| check | pass/total |
|---|---|
| mentions:15 | 9/24 |
| mentions:18 | 0/8 |
| mentions:19 | 8/8 |
| mentions:204 | 0/8 |
| mentions:9 | 1/8 |
| mentions:Асхат | 1/8 |
| mentions:Бекзат | 7/8 |
| mentions:Ерлан | 0/8 |
| mentions:Марин | 10/32 |
| mentions:Санжар | 0/8 |
| mentions:Складская | 7/8 |
| mentions:двор | 0/8 |
| mentions:сред | 0/8 |
| no_exact_time_confirmation | 40/40 |
| no_time_assignment | 40/40 |
| offers_handoff_or_clarifies | 40/40 |
