# Eval ix-5-n — v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

prompts: `f0c81168` · side `n` · auto model `gpt-5.4-mini` · reasoning_effort (model default) (served: gpt-5.4-mini-2026-03-17; request n8n) · panel model `None` · max_tokens 200 · 1 scenarios / 2 runs · 6 s (whole schedule, 1 worker(s))
pass 1 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 19315 in (cached 18944) + 142 out (reasoning 0) ≈ $0.0023 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3493 ms, p90 3493 ms, max 3493 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at f0c81168)
memory marks (apply-history-handoff.py at `f0c81168`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pl_empty_reask_flowers | flowers | auto | 1.00 (2×) |  |

## By check

| check | pass/total |
|---|---|
| lang_ru | 2/2 |
| mentions:18500 | 2/2 |
| mentions:18:00 | 2/2 |
| no_closing_offer | 2/2 |
| no_markdown | 2/2 |
| no_marker_leak | 2/2 |
| no_tool:create_handoff | 2/2 |
| numbers_grounded | 2/2 |
