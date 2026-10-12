# Eval q1b-t11-ws-x10-before — v1.4 (2026-10-12: T11 — Q1 B + V6-unknown: the business turn of a6_/q1p_/q1c_ is the OWNER's reply (role `owner`), except the bot's own reply in q1c_client_time_history_phone_repair; u1_–u4_ — the bot's earlier reply of UNKNOWN delivery (role `bot_unknown`); no_tool:create_handoff on a6_/q1p_ (the c1 lesson), no_marker_leak on every auto scenario; harness h7)

prompts: `661431c9` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 7 scenarios / 70 runs · 216 s
pass 7 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 473742 in (cached 428416) + 7027 out (reasoning 0) ≈ $0.0977 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3027 ms, p90 5035 ms, max 6684 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at 661431c9)
memory marks (apply-history-handoff.py at `661431c9`): owner '' · unknown delivery ''

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ws_list_of_items | wholesale | auto | 1.00 (10×) |  |
| ws_discount_request | wholesale | auto | 1.00 (10×) |  |
| ws_send_pricelist | wholesale | auto | 1.00 (10×) |  |
| ws_kz_min_order | wholesale | auto | 1.00 (10×) |  |
| ws_deferred_payment | wholesale | auto | 1.00 (10×) |  |
| ws_handoff_order | wholesale | auto | 1.00 (10×) |  |
| ws_mixed_lang | wholesale | auto | 1.00 (10×) |  |

## By check

| check | pass/total |
|---|---|
| asks_category_or_handoff | 10/10 |
| claims_handoff_only_if_ok | 10/10 |
| lang_kz | 10/10 |
| marks_unknown_item | 10/10 |
| mentions:50 | 10/10 |
| no_deferred_promise | 10/10 |
| no_discount | 10/10 |
| no_file_promise | 10/10 |
| no_invoice_in_chat | 10/10 |
| no_markdown | 20/20 |
| no_marker_leak | 70/70 |
| no_total_confirmation | 10/10 |
| numbers_grounded | 30/30 |
| offers_handoff_or_clarifies | 20/20 |
| tool_called | 10/10 |
