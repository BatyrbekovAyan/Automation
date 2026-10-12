# Eval q1b2-v6u-x8-after — v1.5 (2026-10-12: T11b — no_tool:create_handoff on every auto scenario whose question the business data answers and whose design calls for no handoff (34: a6_/q1p_ as in v1.4 + 25 more, among them ws_kz_min_order and ws_list_of_items); every auto scenario is classified in test_checks.py (NO_HANDOFF / HANDOFF_IN_DESIGN, each with its reason); roles and all other checks as v1.4; harness h8 — interleaved repeats)

prompts: `worktree` · side `after` · block `v6u-x8` of plan `Tools/n8n/eval/plans/q1b2.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 4 scenarios / 32 runs · 777 s (whole schedule, 4 worker(s))
pass 3 · flaky 1 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 161949 in (cached 152064) + 1073 out (reasoning 0) ≈ $0.0236 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1417 ms, p90 3011 ms, max 4176 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| u1_unknown_reask | auto_parts | auto | 1.00 (8×) |  |
| u2_unknown_new_question | auto_parts | auto | 0.50 (8×) | no_tool:create_handoff |
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
| no_tool:create_handoff | 28/32 |
| not_says_already | 16/16 |
