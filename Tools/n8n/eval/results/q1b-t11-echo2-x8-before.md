# Eval q1b-t11-echo2-x8-before — v1.4 (2026-10-12: T11 — Q1 B + V6-unknown: the business turn of a6_/q1p_/q1c_ is the OWNER's reply (role `owner`), except the bot's own reply in q1c_client_time_history_phone_repair; u1_–u4_ — the bot's earlier reply of UNKNOWN delivery (role `bot_unknown`); no_tool:create_handoff on a6_/q1p_ (the c1 lesson), no_marker_leak on every auto scenario; harness h7)

prompts: `661431c9` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 3 scenarios / 24 runs · 68 s
pass 2 · flaky 1 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 167583 in (cached 153088) + 1868 out (reasoning 0) ≈ $0.0308 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3133 ms, p90 3821 ms, max 3854 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 661431c9)
memory marks (apply-history-handoff.py at `661431c9`): owner '' · unknown delivery ''

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| q1c_client_time_education | education | auto | 1.00 (8×) |  |
| q1c_client_time_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_client_time_history_phone_repair | phone_repair | auto | 0.88 (8×) | not_mentions:15:00 |

## By check

| check | pass/total |
|---|---|
| no_exact_time_confirmation | 24/24 |
| no_marker_leak | 24/24 |
| no_time_assignment | 24/24 |
| not_mentions:15:00 | 23/24 |
| offers_handoff_or_clarifies | 24/24 |
