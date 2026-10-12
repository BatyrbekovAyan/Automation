# Eval q1b-t11-kz-x10-after — v1.4 (2026-10-12: T11 — Q1 B + V6-unknown: the business turn of a6_/q1p_/q1c_ is the OWNER's reply (role `owner`), except the bot's own reply in q1c_client_time_history_phone_repair; u1_–u4_ — the bot's earlier reply of UNKNOWN delivery (role `bot_unknown`); no_tool:create_handoff on a6_/q1p_ (the c1 lesson), no_marker_leak on every auto scenario; harness h7)

prompts: `70518dd0` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 7 scenarios / 70 runs · 193 s
pass 2 · flaky 5 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 513100 in (cached 437760) + 5862 out (reasoning 0) ≈ $0.1157 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 2851 ms, p90 3527 ms, max 4076 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 70518dd0)
memory marks (apply-history-handoff.py at `70518dd0`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_sku_exact_kz | auto_parts | auto | 0.80 (10×) | lang_kz |
| ws_kz_min_order | wholesale | auto | 0.40 (10×) | lang_kz |
| fl_kz_price | flowers | auto | 1.00 (10×) |  |
| ks_kz_availability | kaspi_seller | auto | 0.80 (10×) | lang_kz |
| ed_kz_parent | education | auto | 0.90 (10×) | lang_kz |
| pr_kz_battery | phone_repair | auto | 0.20 (10×) | lang_kz, mentions:15000 |
| gen_kz_pure_greeting | education | auto | 1.00 (10×) |  |

## By check

| check | pass/total |
|---|---|
| lang_kz | 57/70 |
| mentions:15000 | 4/10 |
| mentions:25000 | 10/10 |
| mentions:50 | 10/10 |
| mentions:55000 | 10/10 |
| no_invented_sku | 10/10 |
| no_markdown | 10/10 |
| no_marker_leak | 70/70 |
| no_stock_guarantee | 10/10 |
| numbers_grounded | 50/50 |
