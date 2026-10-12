# Eval q1b3-kz2-x20-before — v1.5 (2026-10-12: T11b — no_tool:create_handoff on every auto scenario whose question the business data answers and whose design calls for no handoff (34: a6_/q1p_ as in v1.4 + 25 more, among them ws_kz_min_order and ws_list_of_items); every auto scenario is classified in test_checks.py (NO_HANDOFF / HANDOFF_IN_DESIGN, each with its reason); roles and all other checks as v1.4; harness h8 — interleaved repeats)

prompts: `661431c9` · side `before` · block `kz2-x20` of plan `Tools/n8n/eval/plans/q1b3.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 2 scenarios / 40 runs · 1012 s (whole schedule, 4 worker(s))
pass 0 · flaky 2 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 300612 in (cached 253824) + 2983 out (reasoning 0) ≈ $0.0676 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 4123 ms, p90 6113 ms, max 6735 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 661431c9)
memory marks (apply-history-handoff.py at `661431c9`): owner '' · unknown delivery ''
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ks_kz_availability | kaspi_seller | auto | 0.50 (20×) | lang_kz |
| pr_kz_battery | phone_repair | auto | 0.15 (20×) | lang_kz, mentions:15000 |

## By check

| check | pass/total |
|---|---|
| lang_kz | 18/40 |
| mentions:15000 | 13/20 |
| no_marker_leak | 40/40 |
| no_stock_guarantee | 20/20 |
| no_tool:create_handoff | 40/40 |
| numbers_grounded | 20/20 |
