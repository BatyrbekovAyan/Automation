---
status: partial
phase: 02-n8n-live-wiring
source: [02-04-PLAN.md device script]
started: 2026-07-10T18:03:48Z
updated: 2026-07-10T18:03:48Z
---

## Current Test

[awaiting detailed human testing — the owner ran the live build and confirmed suggestions render ("seems to be giving suggestions… overall seems working"), then explicitly deferred the point-by-point pass: "i will detailed test later. for now just continue please". Scenario 1 is a partial (smoke) pass; scenarios 2–5 and the detail checks of scenario 1 remain to be exercised on device.]

## Tests

### 1. Toggle → live cards (milestone SC-1 / N8N-01 / N8N-02)
expected: Open a WhatsApp chat on an authed bot, flip «Вместе» ON → skeleton loading state, then 4 cards within ~3–4 s — each a DIFFERENT move (labels from «Ответ»/«Уточнить»/«Вариант»/«К заказу»/«Отложить»/«Отказ»), ranked best-first, "Recommended" badge on card 1 only, no numeric %, text reads like a real owner (RU/KZ) and grounded in the bot's catalog where relevant.
result: partial pass — owner smoke: live suggestions render; detail checks (distinct moves, badge, grounding) pending

### 2. Incoming refresh + draft protection (INT-04 / DATA-01)
expected: With the panel open, the customer sends a new message → the cards refresh to fit it. Then type a draft in the composer and trigger another incoming → the in-progress draft is NOT overwritten.
result: [pending]

### 3. Pick → composer + steer (milestone SC-2 / N8N-03 / INT-01)
expected: Tap a card → its text loads into the composer to edit (it does NOT auto-send) AND a FRESH set of 4 appears re-clustered toward the pick; editing and sending via the normal Send button hands off correctly.
result: [pending]

### 4. Airplane mode → error → recover (milestone SC-4 / N8N-04)
expected: Turn on airplane mode, trigger a refresh → the panel's error state renders (no raw JSON, no crash). Turn airplane mode off and manually refresh → cards return.
result: [pending]

### 5. Rapid picks / chat switch — no stale or crossed cards (milestone SC-3 / DATA-03)
expected: Pick several cards quickly, then switch chats mid-load → no stale or crossed set ever renders in the wrong chat; the newest request for the current chat wins.
result: [pending]

## Summary

total: 5
passed: 0
issues: 0
pending: 5
skipped: 0
blocked: 0

## Gaps
