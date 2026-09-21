# Eval after-v1-mt320-ws — v1 (2026-09-17, audit stage 6)

prompts: `worktree` · model `gpt-5.4-mini` · max_tokens 320 · 1 scenarios / 1 runs · 5 s
pass 0 · flaky 0 · fail 1 · finish_reason=length in 0 runs · tokens 7390+234 ≈ $0.0012 (at $0.15/$0.6 per 1M — assumption)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ws_list_of_items | wholesale | auto | 0.00 (1×) | numbers_grounded |

## By check

| check | pass/total |
|---|---|
| marks_unknown_item | 1/1 |
| no_markdown | 1/1 |
| no_total_confirmation | 1/1 |
| numbers_grounded | 0/1 |
