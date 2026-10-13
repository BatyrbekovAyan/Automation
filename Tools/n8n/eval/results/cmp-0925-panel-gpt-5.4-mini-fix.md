# Eval cmp-0925-panel-gpt-5.4-mini-fix — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `gpt-5.4-mini (reasoning_effort none)` · max_tokens 200 · 1 scenarios / 3 runs · 5 s
pass 1 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 0 in (cached 0) + 0 out (reasoning 0) ≈ $0.0 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): panel median 1485 ms, p90 1924 ms, max 1924 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pn_personal_congrats | flowers | panel | 1.00 (3×) |  |

## By check

| check | pass/total |
|---|---|
| abstain | 3/3 |
