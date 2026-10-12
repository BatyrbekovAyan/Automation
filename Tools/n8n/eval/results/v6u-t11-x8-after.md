# Eval v6u-t11-x8-after — v1.4 (2026-10-12: T11 — Q1 B + V6-unknown: the business turn of a6_/q1p_/q1c_ is the OWNER's reply (role `owner`), except the bot's own reply in q1c_client_time_history_phone_repair; u1_–u4_ — the bot's earlier reply of UNKNOWN delivery (role `bot_unknown`); no_tool:create_handoff on a6_/q1p_ (the c1 lesson), no_marker_leak on every auto scenario; harness h7)

prompts: `70518dd0` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 4 scenarios / 32 runs · 41 s
pass 4 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 141976 in (cached 135168) + 905 out (reasoning 0) ≈ $0.0193 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1241 ms, p90 1551 ms, max 1950 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 70518dd0)
memory marks (apply-history-handoff.py at `70518dd0`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| u1_unknown_reask | auto_parts | auto | 1.00 (8×) |  |
| u2_unknown_new_question | auto_parts | auto | 1.00 (8×) |  |
| u3_sent_control | auto_parts | auto | 1.00 (8×) |  |
| u4_kz_unknown | auto_parts | auto | 1.00 (8×) |  |

## By check

| check | pass/total |
|---|---|
| lang_kz | 8/8 |
| mentions:1500 | 8/8 |
| mentions:3200 | 24/24 |
| no_delivery_doubt | 16/16 |
| no_free_delivery_claim | 8/8 |
| no_marker_leak | 32/32 |
| not_says_already | 16/16 |
