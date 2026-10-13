# Eval cmp-0925-rep-gpt-6-luna — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · auto model `gpt-6-luna` · reasoning_effort `none` · dropped frequency_penalty · panel model `None` · max_tokens 200 · 3 scenarios / 30 runs · 68 s
pass 3 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 136373 in (cached 135446) + 2332 out (reasoning 0) ≈ $0.0026 at $0.1/$0.01/$0.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 2033 ms, p90 3449 ms, max 3949 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| fl_recommend | flowers | auto | 1.00 (10×) |  |
| pr_data_privacy | phone_repair | auto | 1.00 (10×) |  |
| gen_owner_pref_vs_fact | auto_parts | auto | 1.00 (10×) |  |

## By check

| check | pass/total |
|---|---|
| asks_occasion_or_budget_or_offers | 10/10 |
| max_questions:2 | 10/10 |
| mentions_backup | 10/10 |
| no_data_access_claim | 10/10 |
| no_free_delivery_claim | 10/10 |
| no_stock_guarantee | 10/10 |
| numbers_grounded | 10/10 |
