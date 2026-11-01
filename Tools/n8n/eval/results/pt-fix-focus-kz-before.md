# Eval pt-fix-focus-kz-before — v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

prompts: `f0c81168` · side `before` · block `kz` of plan `Tools/n8n/eval/plans/pt-fix-focus.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) (served: gpt-5.4-mini-2026-03-17; request n8n) · panel model `None` · max_tokens 200 · 8 scenarios / 48 runs · 263 s (whole schedule, 2 worker(s))
pass 2 · flaky 5 · fail 1 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 415830 in (cached 401280) + 4067 out (reasoning 0) ≈ $0.0593 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 4095 ms, p90 6302 ms, max 11608 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at f0c81168)
memory marks (apply-history-handoff.py at `f0c81168`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_sku_exact_kz | auto_parts | auto | 0.50 (6×) | lang_kz |
| ws_kz_min_order | wholesale | auto | 1.00 (6×) |  |
| fl_kz_price | flowers | auto | 0.67 (6×) | lang_kz |
| ks_kz_availability | kaspi_seller | auto | 0.67 (6×) | lang_kz |
| ed_kz_parent | education | auto | 0.83 (6×) | lang_kz, no_tool:create_handoff |
| pr_kz_battery | phone_repair | auto | 0.00 (6×) | lang_kz, mentions:15000 |
| gen_kz_pure_greeting | education | auto | 1.00 (6×) |  |
| pl_51_roses_kz_flowers | flowers | auto | 0.83 (6×) | lang_kz |

## By check

| check | pass/total |
|---|---|
| lang_kz | 38/48 |
| mentions:15000 | 1/6 |
| mentions:25000 | 6/6 |
| mentions:48000 | 6/6 |
| mentions:50 | 6/6 |
| mentions:55000 | 6/6 |
| no_closing_offer | 6/6 |
| no_invented_sku | 6/6 |
| no_markdown | 12/12 |
| no_marker_leak | 48/48 |
| no_stock_guarantee | 6/6 |
| no_tool:create_handoff | 47/48 |
| numbers_grounded | 36/36 |
