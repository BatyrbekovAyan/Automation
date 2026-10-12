# Eval q1b2-ws-x10-B — v1.5 (2026-10-12: T11b — no_tool:create_handoff on every auto scenario whose question the business data answers and whose design calls for no handoff (34: a6_/q1p_ as in v1.4 + 25 more, among them ws_kz_min_order and ws_list_of_items); every auto scenario is classified in test_checks.py (NO_HANDOFF / HANDOFF_IN_DESIGN, each with its reason); roles and all other checks as v1.4; harness h8 — interleaved repeats)

prompts: `fba44558` · side `B` · block `ws-x10` of plan `Tools/n8n/eval/plans/q1b2.json` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 7 scenarios / 70 runs · 777 s (whole schedule, 4 worker(s))
pass 5 · flaky 2 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 514214 in (cached 452352) + 7239 out (reasoning 0) ≈ $0.1129 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3145 ms, p90 5900 ms, max 6696 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at fba44558)
memory marks (apply-history-handoff.py at `fba44558`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ws_list_of_items | wholesale | auto | 0.30 (10×) | no_tool:create_handoff |
| ws_discount_request | wholesale | auto | 1.00 (10×) |  |
| ws_send_pricelist | wholesale | auto | 1.00 (10×) |  |
| ws_kz_min_order | wholesale | auto | 0.50 (10×) | lang_kz, no_tool:create_handoff |
| ws_deferred_payment | wholesale | auto | 1.00 (10×) |  |
| ws_handoff_order | wholesale | auto | 1.00 (10×) |  |
| ws_mixed_lang | wholesale | auto | 1.00 (10×) |  |

## By check

| check | pass/total |
|---|---|
| asks_category_or_handoff | 10/10 |
| claims_handoff_only_if_ok | 10/10 |
| lang_kz | 6/10 |
| marks_unknown_item | 10/10 |
| mentions:50 | 10/10 |
| no_deferred_promise | 10/10 |
| no_discount | 10/10 |
| no_file_promise | 10/10 |
| no_invoice_in_chat | 10/10 |
| no_markdown | 20/20 |
| no_marker_leak | 70/70 |
| no_tool:create_handoff | 18/30 |
| no_total_confirmation | 10/10 |
| numbers_grounded | 30/30 |
| offers_handoff_or_clarifies | 20/20 |
| tool_called | 10/10 |
