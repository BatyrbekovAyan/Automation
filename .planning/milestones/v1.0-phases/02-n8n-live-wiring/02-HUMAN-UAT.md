---
status: resolved
phase: 02-n8n-live-wiring
source: [02-04-PLAN.md device script]
started: 2026-07-10T18:03:48Z
updated: 2026-07-28T00:00:00Z
---

## Current Test

[CLOSED 2026-07-28 — see Final Disposition at the end of this file. The deferred point-by-point pass was folded into a consolidated closeout run against CURRENT behavior.]

## Tests

### 1. Toggle → live cards (milestone SC-1 / N8N-01 / N8N-02)
expected: Open a WhatsApp chat on an authed bot, flip «Вместе» ON → skeleton loading state, then 4 cards within ~3–4 s — each a DIFFERENT move (labels from «Ответ»/«Уточнить»/«Вариант»/«К заказу»/«Отложить»/«Отказ»), ranked best-first, "Recommended" badge on card 1 only, no numeric %, text reads like a real owner (RU/KZ) and grounded in the bot's catalog where relevant.
result: **PASS** (2026-07-28) — beyond the original smoke pass, this path has been exercised continuously on real devices and in live n8n executions throughout Phases 9 and 10 (distinct ranked moves, RU copy, catalog grounding all observed in execution data). NOTE: the "Recommended badge on card 1 only" clause is obsolete — the badge was replaced by the mint green tint on the top card (`SuggestionCard.cs:10`, locked design decision).

### 2. Incoming refresh + draft protection (INT-04 / DATA-01)
expected: With the panel open, the customer sends a new message → the cards refresh to fit it. Then type a draft in the composer and trigger another incoming → the in-progress draft is NOT overwritten.
result: **SUPERSEDED** (2026-07-28) — Phase 10 rewrote this exact path (`HandleLive` burst accumulation + the outgoing-reply run boundary) and verified it on device plus via execution runData (execs 1315/1316, 1323–1330). The v1.0 wording predates the debounce entirely.

### 3. Pick → composer + steer (milestone SC-2 / N8N-03 / INT-01)
expected: Tap a card → its text loads into the composer to edit (it does NOT auto-send) AND a FRESH set of 4 appears re-clustered toward the pick; editing and sending via the normal Send button hands off correctly.
result: **PASS** (owner, 2026-07-28) — tapping a card loads its text into the composer for editing, does NOT auto-send, and produces a fresh re-clustered set. Confirmed immediate (not delayed by the BATCH-03 debounce), which also closes Phase 10's sub-check B.

### 4. Airplane mode → error → recover (milestone SC-4 / N8N-04)
expected: Turn on airplane mode, trigger a refresh → the panel's error state renders (no raw JSON, no crash). Turn airplane mode off and manually refresh → cards return.
result: **PASS** (owner, 2026-07-28) — airplane mode triggers the panel's error state (no raw JSON, no crash); returning online and refreshing brings the cards back. This scenario had never been exercised in any phase before this run.

### 5. Rapid picks / chat switch — no stale or crossed cards (milestone SC-3 / DATA-03)
expected: Pick several cards quickly, then switch chats mid-load → no stale or crossed set ever renders in the wrong chat; the newest request for the current chat wins.
result: **PASS** (owner, 2026-07-28) — rapid picks plus a chat switch mid-load never rendered a stale or crossed set in the wrong chat.

## Summary

total: 5
passed: 4
issues: 0
pending: 0
skipped: 0
blocked: 0
superseded: 1

## Gaps

## Final Disposition (2026-07-28)

**RESOLVED.** The point-by-point pass the owner deferred on 2026-07-10 was completed on 2026-07-28 as
part of the v1.0 UAT-debt closeout, run against CURRENT behavior rather than the v1.0 script. Four
scenarios PASS (1, 3, 4, 5); one is SUPERSEDED (2 — the incoming-refresh path was rewritten and
re-verified in Phase 10).

**Most valuable finding:** scenario 4 (airplane mode → error → recover) had never been exercised by
any phase, in any milestone — the suggestions error path was the single genuinely untested behavior
in the whole feature. It works.

**Carried forward:** nothing. No known defect was found.
