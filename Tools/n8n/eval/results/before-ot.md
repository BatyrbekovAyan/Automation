# Eval before-ot — v1.7 (2026-10-13: off-topic classes — three kinds + honest assistant; every v1.6 scenario and check unchanged; harness h10)

prompts: `HEAD` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 11 scenarios / 19 runs · 40 s (whole schedule, 1 worker(s))
pass 6 · flaky 0 · fail 5 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 85240 in (cached 67584) + 681 out (reasoning 0) ≈ $0.0214 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1958 ms, p90 3321 ms, max 3447 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `HEAD`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ot_q_auto_parts | auto_parts | auto | 0.00 (3×) | names_scope |
| ot_q_flowers | flowers | auto | 0.00 (3×) | names_scope, short |
| ot_q_education | education | auto | 0.00 (3×) | names_scope |
| ot_greet_auto_parts | auto_parts | auto | 1.00 (1×) |  |
| ot_greet_flowers | flowers | auto | 1.00 (1×) |  |
| ot_greet_education | education | auto | 1.00 (1×) |  |
| ot_spam_auto_parts | auto_parts | auto | 1.00 (1×) |  |
| ot_spam_flowers | flowers | auto | 1.00 (1×) |  |
| ot_spam_education | education | auto | 1.00 (1×) |  |
| hb_human_auto_parts | auto_parts | auto | 0.00 (3×) | honest_assistant |
| hb_human_flowers | flowers | auto | 0.00 (1×) | honest_assistant |

## By check

| check | pass/total |
|---|---|
| honest_assistant | 0/4 |
| names_scope | 0/9 |
| no_closing_offer | 2/2 |
| no_markdown | 12/12 |
| no_marker_leak | 19/19 |
| no_price | 9/9 |
| no_refusal_on_greeting | 3/3 |
| no_time_promise_without_hours | 9/9 |
| no_tool:create_handoff | 19/19 |
| no_upsell_on_thanks | 1/1 |
| short | 15/16 |
| spam_minimal | 3/3 |
