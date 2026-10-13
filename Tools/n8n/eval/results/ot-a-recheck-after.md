# Eval ot-a-recheck-after — v1.7 (2026-10-13: off-topic classes — three kinds + honest assistant; every v1.6 scenario and check unchanged; harness h10)

prompts: `worktree` · side `after` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 4 scenarios / 32 runs · 234 s (whole schedule, 1 worker(s))
pass 1 · flaky 3 · fail 0 · error runs 0 · finish_reason=length in 0 runs
auto-path tokens 276366 in (cached 265216) + 2456 out (reasoning 0) ≈ $0.0393 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3714 ms, p90 4985 ms, max 9220 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_sku_exact_kz | auto_parts | auto | 0.75 (8×) | lang_kz |
| pr_price_no_model | phone_repair | auto | 0.50 (8×) | asks_model, no_price |
| ws_discount_policy_known | wholesale | auto | 1.00 (8×) |  |
| ks_reserve_policy_known | kaspi_seller | auto | 0.75 (8×) | no_reservation_confirmation |

## By check

| check | pass/total |
|---|---|
| asks_model | 6/8 |
| claims_handoff_only_if_ok | 8/8 |
| lang_kz | 6/8 |
| matches:20[:.]00|до 20\b | 8/8 |
| matches:скид[^.!?]{0,60}(не да|нет|не дела|не предусм|не предоставл)|(нет|не да[её]м|не делаем|не предоставляем)[^.!?]{0,40}скид|фиксирован | 8/8 |
| no_discount | 8/8 |
| no_invented_sku | 8/8 |
| no_marker_leak | 32/32 |
| no_price | 4/8 |
| no_reservation_confirmation | 6/8 |
| no_tool:create_handoff | 24/24 |
| numbers_grounded | 8/8 |
| tool_called | 8/8 |
