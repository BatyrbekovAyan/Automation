---
phase: 08-device-uat-milestone-closeout
plan: 35
subsystem: device-uat
tags: [gate-a, device-uat, reactions, telegram, whatsapp, d2-view, d15, milestone-close, checkpoint]

# Dependency graph
requires:
  - phase: 08 (08-34)
    provides: CR-01a displaced-emoji discrimination + CR-02 Reconcile always-adopt seam + WR-01 pin + the deterministic [D15-probe] trigger (the round-7 fixes the owner verified)
  - phase: 08 (08-33)
    provides: round-6 verdicts — D2-view residual EXACT (differ-during-grace), D15 probe never fired
provides:
  - "Gate A → PASS: the milestone v1.1 (Telegram Parity) device-UAT gate is closed"
  - "D2-view RESOLVED after six failing rounds (displaced-emoji discrimination repaints every own-reaction change)"
  - "D15 disposition REVISED — probe surfaced reactionsKey=True → NOT a platform limit → OPEN-DEFERRED tracked follow-up (absence-based WA reconcile, v1.2/post-milestone)"
  - "I.3 #10 (01-VERIFICATION formal sign-off) re-aggregated to PASS (blocker D5 resolved)"
  - "Gate B (08-PROD-REPLICATION.md runbook) + Gate C (08-MILESTONE-CLOSE.md runbook) UNBLOCKED"
  - "CLAUDE.md D15 note revised from 'confirmed platform limit' to 'in-app-removal gap — tracked follow-up' (probe evidence)"
affects: [08-PROD-REPLICATION owner-assisted runbook, 08-MILESTONE-CLOSE, milestone v1.1 close, v1.2 D15 follow-up]

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created:
    - .planning/phases/08-device-uat-milestone-closeout/08-35-SUMMARY.md
  modified:
    - .planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md
    - CLAUDE.md
    - .planning/STATE.md
    - .planning/ROADMAP.md

key-decisions:
  - "Owner explicit Gate-A decision 'Flip Gate A now': all v1.1 Telegram-parity items pass → close Gate A, unblock Gates B/C, D15 becomes a tracked follow-up rather than a blocker"
  - "D15 disposition WITHDRAWN from 'Wappi platform limit' — the deterministic [D15-probe] returned reactionsKey=True, so the WhatsApp target payload carries reaction state and an absence-based reconcile is implementable"
  - "D15 follow-up FIRST step = capture the reactions key's SHAPE (the probe proved presence only, not the array format)"

patterns-established: []

requirements-completed: []

# Metrics
duration: ~14min
completed: 2026-07-21
---

# Phase 8 Plan 35: Round-7 Owner Re-Verify — GATE A PASSED Summary

**Round-7 owner re-verify returned ALL-PASS across the four fix items and a probe-fired verdict on the fifth — closing D2-view after six failing rounds, revising D15 from a platform limit to a tracked follow-up, and flipping Gate A (the milestone v1.1 gate) to PASS by explicit owner decision, which unblocks Gate B (prod replication) and Gate C (milestone close).**

## Performance

- **Duration:** ~14 min (checkpoint continuation — transcription + disposition + docs)
- **Completed:** 2026-07-21
- **Tasks:** 1 checkpoint (owner-run), executed as a continuation after the owner returned verdicts
- **Files modified:** 4 (08-DEVICE-UAT.md, CLAUDE.md, STATE.md, ROADMAP.md) + this SUMMARY

## Owner Verdicts (verbatim, 2026-07-21)

Owner returned five verdicts + one Editor Console screenshot: **"1. seems ok / 2. ok / 3. ok / 4. ok / 5. screenshot added"**.

| # | Item | Verdict | Outcome |
|---|------|---------|---------|
| 1 | D2-view — rapid own-reaction change in the Telegram app repaints EVERY time | **PASS** ("seems ok") | **RESOLVED** after six failing rounds |
| 2 | Stale-echo defense sanity — no flicker back to the old emoji | **PASS** ("ok") | Original round-2 D2 defense still holds |
| 3 | In-app WhatsApp AND Telegram add/change/remove invariants | **PASS** ("ok") | Count 1, one pill on change, removal stays cleared |
| 4 | Final Gate A device sweep — no regression on one build | **PASS** ("ok") | Nothing regressed vs round 6 |
| 5 | D15 disposition — deterministic probe fires | **probe FIRED, `reactionsKey=True`** ("screenshot added") | Disposition REVISED → OPEN-DEFERRED follow-up |

## Probe Evidence (Editor Console screenshot, all @ 15:06:12 unless noted)

- `[D15] wa-reaction rawId=3A938EDDC110E5E95847 stanza=3AAFD6395EE4345C8EA0 bodyEmpty=False seen=False`
- `[D15-probe] arming target-payload probe for stanza=3AAFD6395EE4345C8EA0`
- `[D2-view] reactions changed id=3AAFD6395EE4345C8EA0 n=1` + `post-render active=True len=30 culled=False`
- **`[D15-probe] wa msgId=3AAFD6395EE4345C8EA0 reactionsKey=True reactionKey=False`**
- (15:06:18) `[D2-view] reactions changed id=3AAFD6395EE4345C8EA0 n=1` + `post-render active=True len=24 culled=False` — a second change applying, the new semantics visibly repainting

## Accomplishments

- **D2-view RESOLVED (milestone v1.1 #1 defect, open across SIX device rounds).** The owner confirmed the 08-34 fix: displaced-emoji discrimination (`MessageReaction.displacedEmoji` on the persisted optimistic entry; `Merge` suppresses a differing server-me ONLY when it equals the displaced value, adopts any THIRD value) + the pure `Reconcile(cached, server, now, out renderChanged)` seam (`RefreshCachedMessageReactions` always adopts, so the freshness lands through all three live-poll call sites). Two successive `[D2-view] reactions changed` events with healthy post-renders corroborate; no `[D2-merge]` line ate a genuine change.
- **D15 disposition REVISED — NOT a platform limit.** The deterministic `[D15-probe]` (08-34, one-shot on the first WhatsApp `type:"reaction"` raw, resolving the target via the existing authed `messages/id/get` drain) fired without owner choreography and returned `reactionsKey=True reactionKey=False`. Per the checkpoint's own rule ("a surfaced key spins an absence-reconcile round"), the round-5/6 platform-limit disposition is withdrawn: the WhatsApp target-message payload carries reaction state, so an absence-based reconcile (mirror of the Telegram approach) is implementable. What still stands: an in-WhatsApp-app removal emits no raw in the polled stream. Per the owner Gate-A decision this is an **OPEN-DEFERRED tracked follow-up** (v1.2/post-milestone), not a Gate A blocker.
- **GATE A → PASS (owner decision "Flip Gate A now").** All v1.1 Telegram-parity items pass. I.3 #10 (01-VERIFICATION formal human sign-off) re-aggregated to PASS — its sole blocker D5 (live-incoming render) was resolved at 08-04, so per its own aggregation rule (I.1 items 1–4 all PASS ⇒ PASS by aggregation) the sign-off flips from `human_needed` to passed. **Gate B (`08-PROD-REPLICATION.md` runbook) and Gate C (`08-MILESTONE-CLOSE.md` runbook) are UNBLOCKED.** Prod bagkz stays dormant until the 08-PROD-REPLICATION runbook is run (owner-assisted).
- **CLAUDE.md D15 note revised** from "Confirmed platform limit (D15)" to "In-app-removal gap (D15) — tracked follow-up, NOT yet implemented", carrying the corrected fact pattern (removal emits no raw in the polled stream, but the `messages/id/get` target payload carries a `reactions` key — probe-confirmed 2026-07-21 — so absence-based reconcile is possible; first step = capture the key's shape).

## Task Commits

Checkpoint continuation — planning docs only, no app code. Single docs commit (see final metadata commit).

## Files Created/Modified

- `.planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md` — §Round 7 verdicts filled verbatim (items 1–5 + owner quotes + probe evidence); Round-7 Overall = PASS; Round-7 Gate A disposition = PASS (owner "Flip Gate A now" quoted); status header chain updated (round-7 RUN → PASS, Gate A PASSED); I.3 #10 re-aggregated to PASS; §Defects D2-view row → RESOLVED, D15 row → OPEN-DEFERRED follow-up; §Overall result top line → PASS + round-7 bullet.
- `CLAUDE.md` — `message/reaction` bullet D15 note revised (platform-limit claim removed; corrected fact pattern + follow-up).
- `.planning/STATE.md` — stopped_at, completed_plans 62→63, Gate A = PASS, Current Position, Accumulated Context (D15 follow-up), Deferred Items (D15 follow-up), Session Continuity.
- `.planning/ROADMAP.md` — 08-35 checked off; Phase 8 line + Flags + progress-table row updated to Gate A PASS.

## Deviations from Plan

None material. This is a checkpoint continuation: the owner ran the gate and returned verdicts; the executor transcribed them, set the dispositions, re-aggregated I.3 #10, revised CLAUDE.md, and updated STATE/ROADMAP per the plan's `<on-completion>` all-PASS branch. The one interpretive call — **D15's probe returning `reactionsKey=True`** — followed the plan's own rule ("a surfaced key spins an absence-reconcile round") tempered by the owner's explicit override ("Flip Gate A now → D15 is a tracked follow-up, not a blocker"). Recorded as the owner Gate-A decision, not a silent deviation.

## Known Stubs

None. Planning-doc updates only; no code, no UI, no data path.

## Threat Flags

None. No new network endpoint, secret, auth path, or scene mutation. The `[D15-probe]` reuses the app's already-authed `messages/id/get` seam in Play Mode (executor never handled a token); `secrets.json` deny honored.

## Issues Encountered

The Edit tool initially rejected a multi-line match on the item-1 verdict block (special-character mismatch on em/en dashes); resolved by matching the unique single verdict line instead. No other issues.

## Next Phase Readiness

- **Gate A is PASSED.** Next: **Gate B — `08-PROD-REPLICATION.md`** (owner-assisted prod bagkz replication runbook: Suggest Replies + all Telegram fixes into the dormant prod n8n), then **Gate C — `08-MILESTONE-CLOSE.md`** and `/gsd-complete-milestone` for v1.1.
- **Tracked follow-up (D15, v1.2/post-milestone):** WhatsApp absence-based reaction-removal reconcile via the `messages/id/get` target-payload `reactions` key. FIRST step = capture the key's SHAPE (the probe proved presence only). Recorded in STATE Accumulated Context + Deferred Items.
- **Phase-close cleanup (08-REVIEW IN-02/IN-03):** the `[D2-merge]` / `[D15-probe]` / `[D15]` / `[D2-view]` Editor diagnostics are tagged for removal at phase close.

## Self-Check: PASSED

- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-35-SUMMARY.md`
- 08-DEVICE-UAT.md: Round-7 Overall = PASS + Gate A disposition = PASS + all five item verdicts recorded + D2-view/D15 Defects rows updated + I.3 #10 re-aggregated (verified in file).
- CLAUDE.md: D15 note revised (no "Confirmed platform limit" string remains on the message/reaction bullet).
- No app code changed; no deletions; only the four named planning/doc files + this SUMMARY touched.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-21*
