# Eval pt-header-reask-n8n — v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

prompts: `f0c81168` · side `n8n` · block `reask` of plan `Tools/n8n/eval/plans/pt-header.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) (served: gpt-5.4-mini-2026-03-17; request n8n) · panel model `None` · max_tokens 200 · 1 scenarios / 16 runs · 323 s (whole schedule, 2 worker(s))
pass 0 · flaky 1 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 159559 in (cached 156288) + 1268 out (reasoning 0) ≈ $0.0199 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3270 ms, p90 4413 ms, max 5354 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at f0c81168)
memory marks (apply-history-handoff.py at `f0c81168`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pl_empty_reask_flowers | flowers | auto | 0.88 (16×) | mentions:18:00, no_tool:create_handoff |

## By check

| check | pass/total |
|---|---|
| lang_ru | 16/16 |
| mentions:18500 | 16/16 |
| mentions:18:00 | 15/16 |
| no_closing_offer | 16/16 |
| no_markdown | 16/16 |
| no_marker_leak | 16/16 |
| no_tool:create_handoff | 15/16 |
| numbers_grounded | 16/16 |
