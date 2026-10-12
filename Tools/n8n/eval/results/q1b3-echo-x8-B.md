# Eval q1b3-echo-x8-B — v1.5 (2026-10-12: T11b — no_tool:create_handoff on every auto scenario whose question the business data answers and whose design calls for no handoff (34: a6_/q1p_ as in v1.4 + 25 more, among them ws_kz_min_order and ws_list_of_items); every auto scenario is classified in test_checks.py (NO_HANDOFF / HANDOFF_IN_DESIGN, each with its reason); roles and all other checks as v1.4; harness h8 — interleaved repeats)

prompts: `6799b512` · side `B` · block `echo-x8` of plan `Tools/n8n/eval/plans/q1b3.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 3 scenarios / 24 runs · 1012 s (whole schedule, 4 worker(s))
pass 3 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 172528 in (cached 157056) + 1878 out (reasoning 0) ≈ $0.0318 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3313 ms, p90 3983 ms, max 4312 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 6799b512)
memory marks (apply-history-handoff.py at `6799b512`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| q1c_client_time_education | education | auto | 1.00 (8×) |  |
| q1c_client_time_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| q1c_client_time_history_phone_repair | phone_repair | auto | 1.00 (8×) |  |

## By check

| check | pass/total |
|---|---|
| no_exact_time_confirmation | 24/24 |
| no_marker_leak | 24/24 |
| no_time_assignment | 24/24 |
| not_mentions:15:00 | 24/24 |
| offers_handoff_or_clarifies | 24/24 |
