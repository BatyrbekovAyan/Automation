---
status: resolved
phase: 01-polished-suggestions-panel-on-mock-data
source: [01-VERIFICATION.md]
started: 2026-06-25T07:42:43Z
updated: 2026-07-28T00:00:00Z
---

## Current Test

[CLOSED 2026-07-28 — see Final Disposition at the end of this file. Two items superseded by later phase UAT, two owner-verified in a consolidated pass, one accepted deviation.]

## Tests

### 1. Per-chat semi-auto persistence survives an app restart (SC-1 / SEMI-02)
expected: Flip a chat to semi-auto, fully quit and relaunch the app (device build) → the same chat reopens with the toggle lit and the panel shown; other chats stay manual / no-panel.
result: **SUPERSEDED** (2026-07-28) — covered by Phase 9's UAT (09-05, 5/5 both channels): per-chat «Вместе» override, the `'*'` bot-default row, and absence→reply fallback were all exercised on device, and `RestoreForActiveChat` adds a re-assert-on-open heal that did not exist at v1.0. Per-chat state persistence is additionally unit-covered (`SemiAutoStoreTests` 5/5).

### 2. Panel renders all visual states at a fixed footprint with no layout pop (SC-2 / PANEL-04 / PANEL-06)
expected: Play Mode (1080×2400): toggle on → 4 shimmer skeletons → 4 ranked RU cards; «Рекомендуем» on the TOP card only; the 209-char reply truncates to ~2 lines + ellipsis without widening the card; empty («Нет предложений») and error («Не удалось загрузить» + «Обновить») render at the SAME footprint; rounded corners on sheet/cards/chip/badge.
result: **PARTIAL — accepted deviation** (owner, 2026-07-28). Error and empty states render correctly and recover (verified in the same pass as scenario 4 of `02-HUMAN-UAT.md`). **Truncation was NOT implemented**: a long reply does not clamp to ~2 lines + ellipsis — `SuggestionCard.Setup` assigns `replyText.text` with no truncation, so the card grows to fit. Owner reviewed and accepted the current behavior as-is ("i think it is good as it is"), so the SC-2/PANEL-04 truncation clause is a **deliberate deviation from the original contract, not a defect**. Note also that the original expectation names a «Рекомендуем» badge on the top card — that badge was later replaced by the mint green tint (`SuggestionCard.cs:10`, a locked design decision), so that clause is obsolete rather than failed.

### 3. Card-tap hand-off + re-cluster; incoming auto-populate never overwrites a draft (INT-01 / INT-02 / INT-04)
expected: Tap a card → its RU text loads into the composer (editable, overwrites any draft) AND a fresh steered set of 4 appears; nothing auto-sends. Type a draft, then trigger an incoming message → cards refresh but the composer draft is NOT touched.
result: **PASS** (owner, 2026-07-28) — card tap loads its text into the composer immediately and re-clusters; nothing auto-sends. This also closed Phase 10's sub-checks A/B (card-pick + manual-refresh immediacy, i.e. neither is delayed by the BATCH-03 debounce). Draft protection on incoming is superseded: Phase 10 rewrote `HandleLive` entirely (burst accumulation + outgoing-reply boundary) and verified it on device and via execution runData.

### 4. Stale/out-of-order/crossed responses never render under rapid picks + chat switches (SC-5 / DATA-03)
expected: Rapidly tap several cards and/or switch chats mid-request (mock latency ~1s) → no stale or crossed set ever appears; newest request for the current chat wins; superseded/foreign responses silently discarded.
result: **PASS** (owner, 2026-07-28) — rapid picks plus a chat switch mid-load never rendered a stale or crossed set; the newest request for the current chat wins. Guard predicate additionally unit-covered (`SuggestionSequenceGuardTests` 5/5).

## Summary

total: 4
passed: 2
issues: 0
pending: 0
skipped: 0
blocked: 0
superseded: 1
accepted_deviation: 1

## Gaps

## Final Disposition (2026-07-28)

**RESOLVED.** Closed during the v1.0 UAT-debt closeout, ~13 months of feature evolution after these
scenarios were written. Two were re-run against current behavior by the owner and PASS (3, 4); one is
SUPERSEDED by stronger later verification (1 — Phase 9 UAT); one is a PARTIAL with an accepted
deviation (2 — no truncation, owner-accepted; the «Рекомендуем» badge clause is obsolete, replaced by
the mint tint).

**Why the scripts were not run verbatim:** they were written against the v1.0 mock-data UI. Since
then the provider swapped to `N8nSuggestionsProvider`, the panel became channel-aware (v1.1), the
server-side suppression gate landed (v1.2 / Phase 9), and `HandleLive` was rewritten for debounce +
burst accumulation (v1.2 / Phase 10). Running them literally would have failed on obsolete wording
rather than on real behavior. Same supersede-with-disposition pattern as commit `1ebdedd`, which
closed the v1.1 verification debt via Phase 8's Gate A.

**Carried forward:** nothing. No known defect was found.
