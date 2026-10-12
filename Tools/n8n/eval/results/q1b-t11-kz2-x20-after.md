# Eval q1b-t11-kz2-x20-after — v1.4 (2026-10-12: T11 — Q1 B + V6-unknown: the business turn of a6_/q1p_/q1c_ is the OWNER's reply (role `owner`), except the bot's own reply in q1c_client_time_history_phone_repair; u1_–u4_ — the bot's earlier reply of UNKNOWN delivery (role `bot_unknown`); no_tool:create_handoff on a6_/q1p_ (the c1 lesson), no_marker_leak on every auto scenario; harness h7)

prompts: `70518dd0` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 2 scenarios / 40 runs · 105 s
pass 0 · flaky 2 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 287693 in (cached 238720) + 2953 out (reasoning 0) ≈ $0.0679 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 2877 ms, p90 3264 ms, max 3657 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 70518dd0)
memory marks (apply-history-handoff.py at `70518dd0`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ks_kz_availability | kaspi_seller | auto | 0.80 (20×) | lang_kz |
| pr_kz_battery | phone_repair | auto | 0.10 (20×) | lang_kz, mentions:15000 |

## By check

| check | pass/total |
|---|---|
| lang_kz | 26/40 |
| mentions:15000 | 9/20 |
| no_marker_leak | 40/40 |
| no_stock_guarantee | 20/20 |
| numbers_grounded | 20/20 |
