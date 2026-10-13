# Eval cmp-0925-rep-gpt-5.4-mini-nofp — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · dropped frequency_penalty · panel model `None` · max_tokens 200 · 3 scenarios / 30 runs · 50 s
pass 1 · flaky 2 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 156966 in (cached 146560) + 2665 out (reasoning 0) ≈ $0.0308 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1337 ms, p90 2545 ms, max 2629 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| fl_recommend | flowers | auto | 0.90 (10×) | max_questions:2 |
| pr_data_privacy | phone_repair | auto | 0.80 (10×) | no_data_access_claim |
| gen_owner_pref_vs_fact | auto_parts | auto | 1.00 (10×) |  |

## By check

| check | pass/total |
|---|---|
| asks_occasion_or_budget_or_offers | 10/10 |
| max_questions:2 | 9/10 |
| mentions_backup | 10/10 |
| no_data_access_claim | 8/10 |
| no_free_delivery_claim | 10/10 |
| no_stock_guarantee | 10/10 |
| numbers_grounded | 10/10 |
