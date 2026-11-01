# Eval pt-fix2-focus-v6u-after — v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

prompts: `worktree` · side `after` · block `v6u` of plan `Tools/n8n/eval/plans/pt-fix2-focus.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) (served: gpt-5.4-mini-2026-03-17; request n8n) · panel model `None` · max_tokens 200 · 4 scenarios / 40 runs · 76 s (whole schedule, 2 worker(s))
pass 4 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 206430 in (cached 204672) + 1105 out (reasoning 0) ≈ $0.0216 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1440 ms, p90 1812 ms, max 5453 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| u1_unknown_reask | auto_parts | auto | 1.00 (10×) |  |
| u2_unknown_new_question | auto_parts | auto | 1.00 (10×) |  |
| u3_sent_control | auto_parts | auto | 1.00 (10×) |  |
| u4_kz_unknown | auto_parts | auto | 1.00 (10×) |  |

## By check

| check | pass/total |
|---|---|
| lang_kz | 10/10 |
| mentions:1500 | 10/10 |
| mentions:3200 | 30/30 |
| no_delivery_doubt | 20/20 |
| no_free_delivery_claim | 10/10 |
| no_marker_leak | 40/40 |
| no_tool:create_handoff | 40/40 |
| not_says_already | 20/20 |
