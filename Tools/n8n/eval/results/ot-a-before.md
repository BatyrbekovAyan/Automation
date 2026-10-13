# Eval ot-a-before — v1.7 (2026-10-13: off-topic classes — three kinds + honest assistant; every v1.6 scenario and check unchanged; harness h10)

prompts: `HEAD` · side `before` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 11 scenarios / 33 runs · 87 s (whole schedule, 1 worker(s))
pass 4 · flaky 2 · fail 5 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 147732 in (cached 135168) + 986 out (reasoning 0) ≈ $0.024 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 1252 ms, p90 1891 ms, max 2551 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at HEAD)
memory marks (apply-history-handoff.py at `HEAD`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ot_q_auto_parts | auto_parts | auto | 0.00 (3×) | names_scope |
| ot_q_flowers | flowers | auto | 0.00 (3×) | names_scope, short |
| ot_q_education | education | auto | 0.00 (3×) | names_scope |
| ot_greet_auto_parts | auto_parts | auto | 1.00 (3×) |  |
| ot_greet_flowers | flowers | auto | 1.00 (3×) |  |
| ot_greet_education | education | auto | 1.00 (3×) |  |
| ot_spam_auto_parts | auto_parts | auto | 0.67 (3×) | spam_minimal |
| ot_spam_flowers | flowers | auto | 0.67 (3×) | spam_minimal |
| ot_spam_education | education | auto | 1.00 (3×) |  |
| hb_human_auto_parts | auto_parts | auto | 0.00 (3×) | honest_assistant |
| hb_human_flowers | flowers | auto | 0.00 (3×) | honest_assistant |

## By check

| check | pass/total |
|---|---|
| honest_assistant | 0/6 |
| names_scope | 0/9 |
| no_closing_offer | 6/6 |
| no_markdown | 12/12 |
| no_marker_leak | 33/33 |
| no_price | 15/15 |
| no_refusal_on_greeting | 9/9 |
| no_time_promise_without_hours | 9/9 |
| no_tool:create_handoff | 33/33 |
| no_upsell_on_thanks | 3/3 |
| short | 22/24 |
| spam_minimal | 7/9 |
