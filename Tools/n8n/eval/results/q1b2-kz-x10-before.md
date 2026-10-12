# Eval q1b2-kz-x10-before — v1.5 (2026-10-12: T11b — no_tool:create_handoff on every auto scenario whose question the business data answers and whose design calls for no handoff (34: a6_/q1p_ as in v1.4 + 25 more, among them ws_kz_min_order and ws_list_of_items); every auto scenario is classified in test_checks.py (NO_HANDOFF / HANDOFF_IN_DESIGN, each with its reason); roles and all other checks as v1.4; harness h8 — interleaved repeats)

prompts: `661431c9` · side `before` · block `kz-x10` of plan `Tools/n8n/eval/plans/q1b2.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 7 scenarios / 70 runs · 777 s (whole schedule, 4 worker(s))
pass 3 · flaky 4 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 464420 in (cached 406400) + 5789 out (reasoning 0) ≈ $0.1 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3143 ms, p90 4318 ms, max 5141 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 661431c9)
memory marks (apply-history-handoff.py at `661431c9`): owner '' · unknown delivery ''
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_sku_exact_kz | auto_parts | auto | 0.80 (10×) | lang_kz |
| ws_kz_min_order | wholesale | auto | 1.00 (10×) |  |
| fl_kz_price | flowers | auto | 1.00 (10×) |  |
| ks_kz_availability | kaspi_seller | auto | 0.50 (10×) | lang_kz |
| ed_kz_parent | education | auto | 0.60 (10×) | lang_kz |
| pr_kz_battery | phone_repair | auto | 0.10 (10×) | lang_kz, mentions:15000 |
| gen_kz_pure_greeting | education | auto | 1.00 (10×) |  |

## By check

| check | pass/total |
|---|---|
| lang_kz | 56/70 |
| mentions:15000 | 3/10 |
| mentions:25000 | 10/10 |
| mentions:50 | 10/10 |
| mentions:55000 | 10/10 |
| no_invented_sku | 10/10 |
| no_markdown | 10/10 |
| no_marker_leak | 70/70 |
| no_stock_guarantee | 10/10 |
| no_tool:create_handoff | 70/70 |
| numbers_grounded | 50/50 |
