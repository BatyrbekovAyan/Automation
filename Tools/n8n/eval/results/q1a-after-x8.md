# Eval q1a-after-x8 — v1.3 (2026-10-09: Q1 — the owner's agreement in the chat history: a6_owner_agreement_* ×3 verticals, q1p_* paraphrases (other words, facts, verticals), q1c_* negative controls (no agreement / the CLIENT names the time) with not_mentions:15:00 against echoing the client's own time as agreed; harness h6)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 14 scenarios / 112 runs · 212 s
pass 10 · flaky 1 · fail 3 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 547600 in (cached 486272) + 4719 out (reasoning 0) ≈ $0.1037 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1670 ms, p90 3281 ms, max 4754 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| a6_owner_agreement_flowers | flowers | auto | 1.00 (8×) |  |
| a6_owner_agreement_education | education | auto | 1.00 (8×) |  |
| a6_owner_agreement_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1p_visit_phone_repair | phone_repair | auto | 0.00 (8×) | mentions:Ерлан, mentions:двор |
| q1p_trial_education | education | auto | 0.12 (8×) | mentions:204, mentions:сред |
| q1p_early_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_no_agreement_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_no_agreement_education | education | auto | 1.00 (8×) |  |
| q1c_client_time_education | education | auto | 1.00 (8×) |  |
| q1c_client_time_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_client_time_history_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1p_pickup_auto_parts | auto_parts | auto | 0.00 (8×) | mentions:18, mentions:Асхат |
| q1p_delivery_flowers | flowers | auto | 0.00 (8×) | mentions:Санжар |
| q1p_meeting_wholesale | wholesale | auto | 1.00 (8×) |  |

## By check

| check | pass/total |
|---|---|
| mentions:15 | 24/24 |
| mentions:18 | 0/8 |
| mentions:19 | 8/8 |
| mentions:204 | 1/8 |
| mentions:9 | 8/8 |
| mentions:Асхат | 5/8 |
| mentions:Бекзат | 8/8 |
| mentions:Ерлан | 0/8 |
| mentions:Марин | 32/32 |
| mentions:Санжар | 0/8 |
| mentions:Складская | 8/8 |
| mentions:двор | 0/8 |
| mentions:сред | 1/8 |
| no_exact_time_confirmation | 40/40 |
| no_time_assignment | 40/40 |
| not_mentions:15:00 | 24/24 |
| offers_handoff_or_clarifies | 40/40 |
