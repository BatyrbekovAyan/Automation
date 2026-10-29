# Eval pt-fix-focus-wslist-after — v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

prompts: `worktree` · side `after` · block `wslist` of plan `Tools/n8n/eval/plans/pt-fix-focus.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) (served: gpt-5.4-mini-2026-03-17; request n8n) · panel model `None` · max_tokens 200 · 1 scenarios / 6 runs · 263 s (whole schedule, 2 worker(s))
pass 0 · flaky 1 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 60421 in (cached 58496) + 1007 out (reasoning 0) ≈ $0.0104 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 5140 ms, p90 7384 ms, max 7384 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ws_list_of_items | wholesale | auto | 0.83 (6×) | no_total_confirmation |

## By check

| check | pass/total |
|---|---|
| marks_unknown_item | 6/6 |
| no_markdown | 6/6 |
| no_marker_leak | 6/6 |
| no_tool:create_handoff | 6/6 |
| no_total_confirmation | 5/6 |
| numbers_grounded | 6/6 |
