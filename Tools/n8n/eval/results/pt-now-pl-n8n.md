# Eval pt-now-pl-n8n — v1.8 (2026-10-29: the price that lives ONLY in the price list — 8 auto scenarios pl_* (products_override / services_override, harness h11; pl_empty_reask_flowers = the e2e-auth retired_bot_reconnects_after_purchase model step); every v1.7 scenario and check unchanged)

prompts: `f0c81168` · side `n8n` · block `pl` of plan `Tools/n8n/eval/plans/pt-now.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) (served: gpt-5.4-mini-2026-03-17; request n8n) · panel model `None` · max_tokens 200 · 8 scenarios / 64 runs · 484 s (whole schedule, 2 worker(s))
pass 6 · flaky 2 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 628864 in (cached 588160) + 4294 out (reasoning 0) ≈ $0.094 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3136 ms, p90 3502 ms, max 4225 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at f0c81168)
memory marks (apply-history-handoff.py at `f0c81168`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pl_51_roses_flowers | flowers | auto | 1.00 (8×) |  |
| pl_51_roses_kz_flowers | flowers | auto | 1.00 (8×) |  |
| pl_iphone13_screen_phone_repair | phone_repair | auto | 1.00 (8×) |  |
| pl_ielts_individual_education | education | auto | 1.00 (8×) |  |
| pl_spark_plugs_auto_parts | auto_parts | auto | 0.62 (8×) | numbers_grounded |
| pl_empty_plain_flowers | flowers | auto | 1.00 (8×) |  |
| pl_empty_memory_flowers | flowers | auto | 1.00 (8×) |  |
| pl_empty_reask_flowers | flowers | auto | 0.88 (8×) | mentions:18:00 |

## By check

| check | pass/total |
|---|---|
| lang_kz | 8/8 |
| lang_ru | 56/56 |
| mentions:12000 | 8/8 |
| mentions:18500 | 24/24 |
| mentions:18:00 | 7/8 |
| mentions:4200 | 8/8 |
| mentions:48000 | 16/16 |
| mentions:52000 | 8/8 |
| mentions_diagnostics_caveat | 8/8 |
| no_closing_offer | 64/64 |
| no_markdown | 64/64 |
| no_marker_leak | 64/64 |
| no_tool:create_handoff | 64/64 |
| numbers_grounded | 61/64 |
