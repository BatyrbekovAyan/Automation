# Eval cmp-0925-handoff-gpt-5.4 — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · auto model `gpt-5.4` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 8 scenarios / 40 runs · 308 s
pass 7 · flaky 0 · fail 1 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 285131 in (cached 244992) + 5445 out (reasoning 0) ≈ $0.2433 at $2.5/$0.25/$15.0 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 8032 ms, p90 11765 ms, max 12474 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_handoff_ok | auto_parts | auto | 1.00 (5×) |  |
| ap_handoff_fail | auto_parts | auto | 1.00 (5×) |  |
| ws_handoff_order | wholesale | auto | 1.00 (5×) |  |
| fl_order_steps | flowers | auto | 1.00 (5×) |  |
| fl_night_order_hours_known | flowers | auto | 0.00 (5×) | tool_called |
| ks_complaint | kaspi_seller | auto | 1.00 (5×) |  |
| ed_trial_signup | education | auto | 1.00 (5×) |  |
| pr_handoff_visit | phone_repair | auto | 1.00 (5×) |  |

## By check

| check | pass/total |
|---|---|
| claims_handoff_only_if_ok | 40/40 |
| no_exact_time_confirmation | 10/10 |
| no_invoice_in_chat | 5/5 |
| no_photo_promise | 5/5 |
| no_refund_confirmation | 5/5 |
| no_time_assignment | 5/5 |
| numbers_grounded | 10/10 |
| polite | 5/5 |
| tool_called | 35/40 |
