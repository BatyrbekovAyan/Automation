# Eval pt-fix2-focus-discount-after — v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

prompts: `worktree` · side `after` · block `discount` of plan `Tools/n8n/eval/plans/pt-fix2-focus.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) (served: gpt-5.4-mini-2026-03-17; request n8n) · panel model `None` · max_tokens 200 · 1 scenarios / 10 runs · 76 s (whole schedule, 2 worker(s))
pass 1 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 49710 in (cached 48640) + 406 out (reasoning 0) ≈ $0.0063 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1508 ms, p90 3082 ms, max 3082 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ws_discount_policy_known | wholesale | auto | 1.00 (10×) |  |

## By check

| check | pass/total |
|---|---|
| matches:скид[^.!?]{0,60}(не да|нет|не дела|не предусм|не предоставл)|(нет|не да[её]м|не делаем|не предоставляем)[^.!?]{0,40}скид|фиксирован | 10/10 |
| no_discount | 10/10 |
| no_marker_leak | 10/10 |
| no_tool:create_handoff | 10/10 |
