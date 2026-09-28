# Eval r14p-final-x5-panel-flowers — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `gpt-4o-mini` · max_tokens 200 · 3 scenarios / 15 runs · 28 s
pass 3 · flaky 0 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 0 in (cached 0) + 0 out (reasoning 0) ≈ $0.0 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): panel median 1817 ms, p90 2509 ms, max 2625 ms

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pn_personal_congrats | flowers | panel | 1.00 (5×) |  |
| pn_thanks_in_business | flowers | panel | 1.00 (5×) |  |
| pn_price_question | flowers | panel | 1.00 (5×) |  |

## By check

| check | pass/total |
|---|---|
| abstain | 5/5 |
| cards | 10/10 |
| cards_no_upsell_on_thanks | 5/5 |
| cards_numbers_grounded | 5/5 |
| cards_ru | 5/5 |
