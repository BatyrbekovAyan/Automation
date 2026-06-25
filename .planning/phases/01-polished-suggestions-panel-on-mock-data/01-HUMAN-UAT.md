---
status: partial
phase: 01-polished-suggestions-panel-on-mock-data
source: [01-VERIFICATION.md]
started: 2026-06-25T07:42:43Z
updated: 2026-06-25T07:42:43Z
---

## Current Test

[awaiting human testing — user gave informal "seems working" sign-off on the live loop this session; these formalize the roadmap-contract items, notably the device app-restart cycle]

## Tests

### 1. Per-chat semi-auto persistence survives an app restart (SC-1 / SEMI-02)
expected: Flip a chat to semi-auto, fully quit and relaunch the app (device build) → the same chat reopens with the toggle lit and the panel shown; other chats stay manual / no-panel.
result: [pending]

### 2. Panel renders all visual states at a fixed footprint with no layout pop (SC-2 / PANEL-04 / PANEL-06)
expected: Play Mode (1080×2400): toggle on → 4 shimmer skeletons → 4 ranked RU cards; «Рекомендуем» on the TOP card only; the 209-char reply truncates to ~2 lines + ellipsis without widening the card; empty («Нет предложений») and error («Не удалось загрузить» + «Обновить») render at the SAME footprint; rounded corners on sheet/cards/chip/badge.
result: [pending]

### 3. Card-tap hand-off + re-cluster; incoming auto-populate never overwrites a draft (INT-01 / INT-02 / INT-04)
expected: Tap a card → its RU text loads into the composer (editable, overwrites any draft) AND a fresh steered set of 4 appears; nothing auto-sends. Type a draft, then trigger an incoming message → cards refresh but the composer draft is NOT touched.
result: [pending]

### 4. Stale/out-of-order/crossed responses never render under rapid picks + chat switches (SC-5 / DATA-03)
expected: Rapidly tap several cards and/or switch chats mid-request (mock latency ~1s) → no stale or crossed set ever appears; newest request for the current chat wins; superseded/foreign responses silently discarded.
result: [pending]

## Summary

total: 4
passed: 0
issues: 0
pending: 4
skipped: 0
blocked: 0

## Gaps
