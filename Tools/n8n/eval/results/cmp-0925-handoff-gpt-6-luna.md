# Eval cmp-0925-handoff-gpt-6-luna — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · auto model `gpt-6-luna` · reasoning_effort `none` · dropped frequency_penalty · panel model `None` · max_tokens 200 · 8 scenarios / 40 runs · 158 s
pass 8 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 328052 in (cached 322811) + 5044 out (reasoning 0) ≈ $0.0063 at $0.1/$0.01/$0.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3571 ms, p90 6533 ms, max 6621 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_handoff_ok | auto_parts | auto | 1.00 (5×) |  |
| ap_handoff_fail | auto_parts | auto | 1.00 (5×) |  |
| ws_handoff_order | wholesale | auto | 1.00 (5×) |  |
| fl_order_steps | flowers | auto | 1.00 (5×) |  |
| fl_night_order_hours_known | flowers | auto | 1.00 (5×) |  |
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
| tool_called | 40/40 |
