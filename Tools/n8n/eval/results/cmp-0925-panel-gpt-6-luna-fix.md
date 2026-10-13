# Eval cmp-0925-panel-gpt-6-luna-fix — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `gpt-6-luna (reasoning_effort none)` · max_tokens 200 · 4 scenarios / 12 runs · 19 s
pass 4 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 0 in (cached 0) + 0 out (reasoning 0) ≈ $0.0 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): panel median 1480 ms, p90 2394 ms, max 2678 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pn_personal_congrats | flowers | panel | 1.00 (3×) |  |
| pn_emotional_reaction | kaspi_seller | panel | 1.00 (3×) |  |
| pn_go_to_cinema | auto_parts | panel | 1.00 (3×) |  |
| pn_thanks_in_business | flowers | panel | 1.00 (3×) |  |

## By check

| check | pass/total |
|---|---|
| abstain | 9/9 |
| cards | 3/3 |
| cards_no_upsell_on_thanks | 3/3 |
