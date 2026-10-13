# Eval cmp-1013-auto-v4v5-focus-before — v1.6 (2026-10-13: a condition — discount, deferred payment, a hold, a return. Unknown to the data: pn_discount_unknown, pn_deferred_unknown, pn_reserve_unknown, pn_return_unknown — card 1 neither grants nor refuses it (cards_first_no_decision), no card states a rule absent from the data (cards_absent). Written by the owner into «Промпт»: ws_discount_policy_known / pn_discount_policy_known, ks_reserve_policy_known / pn_reserve_policy_known — the bot and card 1 both state the owner's rule (matches / cards_first_matches), the hold also goes to the owner as a request. Harness h9; every v1.5 scenario and check unchanged)

prompts: `HEAD` · side `before` · auto model `gpt-5.4-mini` · reasoning_effort (model default) · panel model `None` · max_tokens 200 · 12 scenarios / 96 runs · 421 s (whole schedule, 4 worker(s))
pass 3 · flaky 7 · fail 2 · error runs 5 · finish_reason=length in 0 runs
auto-path tokens 756872 in (cached 698112) + 8523 out (reasoning 0) ≈ $0.1348 at $0.75/$0.075/$4.5 per 1M in/cached/out (panel usage is not returned by the webhook)
latency (wall clock through staging): auto median 3699 ms, p90 14003 ms, max 59340 ms
simulated price-list search: Price List Search sub-workflow (Reconcile Prices at HEAD)
memory marks (apply-history-handoff.py at `HEAD`): owner 'Владелец: ' · unknown delivery '[доставка не подтверждена] '
order: h8 interleaved — every scenario's repeats spread over the whole schedule, the sides of one repeat back to back (run `seq` in the JSON)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| ap_price_without_car | auto_parts | auto | 1.00 (8×) |  |
| ws_list_of_items | wholesale | auto | 0.00 (8×) | no_tool:create_handoff, no_total_confirmation |
| ws_discount_request | wholesale | auto | 1.00 (8×) |  |
| ws_deferred_payment | wholesale | auto | 1.00 (8×) |  |
| fl_absent_item | flowers | auto | 0.88 (8×) | offers_handoff_or_clarifies |
| ks_variant_clarify | kaspi_seller | auto | 0.88 (8×) | asks_variant_or_prices_both |
| ks_reserve | kaspi_seller | auto | 0.88 (8×) | offers_handoff_or_clarifies |
| ed_price_no_details | education | auto | 0.50 (8×) | asks_for_whom_or_goal |
| pr_price_no_model | phone_repair | auto | 0.50 (8×) | asks_model, no_price |
| u2_unknown_new_question | auto_parts | auto | 0.62 (8×) | no_tool:create_handoff |
| ws_discount_policy_known | wholesale | auto | 0.00 (8×) | matches:скид[^.!?]{0,60}(не да|нет|не дела|не предусм|не предоставл)|(нет|не да[её]м|не делаем|не предоставляем)[^.!?]{0,40}скид|фиксирован, no_tool:create_handoff |
| ks_reserve_policy_known | kaspi_seller | auto | 0.38 (8×) | matches:20[:.]00|до 20\b |

## By check

| check | pass/total |
|---|---|
| asks_car | 8/8 |
| asks_for_whom_or_goal | 4/8 |
| asks_model | 7/8 |
| asks_variant_or_prices_both | 7/8 |
| claims_handoff_only_if_ok | 8/8 |
| lang_ru | 8/8 |
| marks_unknown_item | 8/8 |
| matches:20[:.]00|до 20\b | 3/8 |
| matches:скид[^.!?]{0,60}(не да|нет|не дела|не предусм|не предоставл)|(нет|не да[её]м|не делаем|не предоставляем)[^.!?]{0,40}скид|фиксирован | 7/8 |
| mentions:1500 | 8/8 |
| no_closing_offer | 8/8 |
| no_deferred_promise | 8/8 |
| no_delivery_doubt | 8/8 |
| no_discount | 16/16 |
| no_free_delivery_claim | 8/8 |
| no_markdown | 24/24 |
| no_marker_leak | 96/96 |
| no_price | 13/16 |
| no_price_for:тюльпан | 8/8 |
| no_reservation_confirmation | 16/16 |
| no_stock_denial | 8/8 |
| no_tool:create_handoff | 37/56 |
| no_total_confirmation | 7/8 |
| numbers_grounded | 16/16 |
| offers_handoff_or_clarifies | 30/32 |
| tool_called | 8/8 |
