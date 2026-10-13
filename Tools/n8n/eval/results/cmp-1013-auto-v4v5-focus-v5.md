# Eval cmp-1013-auto-v4v5-focus-v5 — v1.6 (2026-10-13: a condition — discount, deferred payment, a hold, a return. Unknown to the data: pn_discount_unknown, pn_deferred_unknown, pn_reserve_unknown, pn_return_unknown — card 1 neither grants nor refuses it (cards_first_no_decision), no card states a rule absent from the data (cards_absent). Written by the owner into «Промпт»: ws_discount_policy_known / pn_discount_policy_known, ks_reserve_policy_known / pn_reserve_policy_known — the bot and card 1 both state the owner's rule (matches / cards_first_matches), the hold also goes to the owner as a request. Harness h9; every v1.5 scenario and check unchanged)

prompts: `worktree` · side `v5` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 12 scenarios / 96 runs · 421 s (whole schedule, 4 worker(s))
pass 6 · flaky 6 · fail 0 · error runs 3 · finish_reason=length in 0 runs
auto-path tokens 710935 in (cached 651520) + 7401 out (reasoning 0) ≈ $0.1267 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3581 ms, p90 12439 ms, max 41868 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at worktree)
memory marks (apply-history-handoff.py at `worktree`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 (8×) |  |
| ws_list_of_items | wholesale | auto | 0.25 (8×) | no_tool:create_handoff, no_total_confirmation |
| ws_discount_request | wholesale | auto | 1.00 (8×) |  |
| ws_deferred_payment | wholesale | auto | 1.00 (8×) |  |
| fl_absent_item | flowers | auto | 0.88 (8×) | offers_handoff_or_clarifies |
| ks_variant_clarify | kaspi_seller | auto | 1.00 (8×) |  |
| ks_reserve | kaspi_seller | auto | 1.00 (8×) |  |
| ed_price_no_details | education | auto | 0.50 (8×) | asks_for_whom_or_goal |
| pr_price_no_model | phone_repair | auto | 0.75 (8×) | asks_model, no_price |
| u2_unknown_new_question | auto_parts | auto | 0.75 (8×) | no_tool:create_handoff |
| ws_discount_policy_known | wholesale | auto | 1.00 (8×) |  |
| ks_reserve_policy_known | kaspi_seller | auto | 0.50 (8×) | matches:20[:.]00|до 20\b, no_reservation_confirmation |

## By check

| check | pass/total |
|---|---|
| asks_car | 8/8 |
| asks_for_whom_or_goal | 4/8 |
| asks_model | 6/8 |
| asks_variant_or_prices_both | 8/8 |
| claims_handoff_only_if_ok | 8/8 |
| lang_ru | 8/8 |
| marks_unknown_item | 8/8 |
| matches:20[:.]00|до 20\b | 5/8 |
| matches:скид[^.!?]{0,60}(не да|нет|не дела|не предусм|не предоставл)|(нет|не да[её]м|не делаем|не предоставляем)[^.!?]{0,40}скид|фиксирован | 8/8 |
| mentions:1500 | 8/8 |
| no_closing_offer | 8/8 |
| no_deferred_promise | 8/8 |
| no_delivery_doubt | 8/8 |
| no_discount | 16/16 |
| no_free_delivery_claim | 8/8 |
| no_markdown | 24/24 |
| no_marker_leak | 96/96 |
| no_price | 15/16 |
| no_price_for:тюльпан | 8/8 |
| no_reservation_confirmation | 15/16 |
| no_stock_denial | 8/8 |
| no_tool:create_handoff | 48/56 |
| no_total_confirmation | 7/8 |
| numbers_grounded | 16/16 |
| offers_handoff_or_clarifies | 31/32 |
| tool_called | 8/8 |
