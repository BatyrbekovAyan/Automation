---
phase: 10-message-batching-debounce
plan: 04
subsystem: testing
tags: [uat, owner-gate, debounce, batching, wappi, whatsapp, telegram, coalesce, semi-auto, cross-phase]

# Dependency graph
requires:
  - phase: 10-message-batching-debounce (10-01)
    provides: "the committed debounce splice (Debounce Wait -> Fetch Recent -> Latest+Combine -> Is Latest?) in both bot templates"
  - phase: 10-message-batching-debounce (10-02)
    provides: "the client IncomingDebounceGate (~2.5s) coalescing rapid «Вместе» incomings; 6 EditMode tests, suite 1197/1197"
  - phase: 10-message-batching-debounce (10-03)
    provides: "both templates proven LIVE on dev via runData (2-fragment -> ONE combined reply, id-equality True on all winners, fresh clones inherit the debounce)"
  - phase: 09-semi-auto-suppression (09-04)
    provides: "the /webhook/SetReplyMode deploy + reply_mode_flags suppression the scenario-5 composition check depends on — STILL OPEN (blocks scenario 4 app-toggle + scenario 5 e2e)"
provides:
  - "10-HUMAN-UAT.md owner runbook with recorded verdicts: scenarios 1-3 PASS (auto-reply combine behaviorally confirmed BOTH channels), scenario 4 BLOCKED by 09-04, scenario 5 DEFERRED to post-Phase-9"
  - "Behavioral confirmation of BATCH-01/BATCH-02: multi-fragment -> ONE combined reply on WhatsApp AND Telegram; single complete message -> one reply; humanizer pauses unchanged"
  - "Cross-phase blocker evidence recorded: /webhook/SetReplyMode 404 (Manager.ReplyModeSync.cs:105) is the expected consequence of 09-04 Task 2 being undeployed, NOT a Phase-10 defect"
affects: [09-04 (its SetReplyMode deploy re-enables scenario 4/5 re-verify), /gsd-secure-phase 10 (auto-reply half behaviorally green), prod bagkz replication (debounce + binaryMode strip fold-in)]

# Tech tracking
tech-stack:
  added: []  # UAT gate — no code; owner-run device e2e
  patterns:
    - "Partial-close a phase-closing UAT gate: record per-scenario PASS/BLOCKED/DEFERRED verdicts, mark cross-phase-blocked and owner-deferred scenarios as tracked debt (not passed), close the plan under an explicit owner continue-decision"
    - "Cross-phase blocker honesty: a scenario blocked by another phase's open gate records the exact error evidence + cites the standing automated coverage, so it re-runs trivially without re-opening this plan"

key-files:
  created:
    - .planning/phases/10-message-batching-debounce/10-HUMAN-UAT.md
  modified: []

key-decisions:
  - "Scenarios 4 (suggestions coalesce) and 5 (semi-auto skips path) NOT marked passed — 4 is BLOCKED by the open 09-04 SetReplyMode deploy (404 evidence), 5 is DEFERRED by explicit owner decision to post-Phase-9; both tracked as UAT debt so they surface in /gsd-progress and /gsd-audit-uat"
  - "Plan closed with status: partial under the owner's explicit 2026-07-22 continue-decision — the auto-reply combine half (BATCH-01/02) is behaviorally green on both channels, which is the phase's user-observable core; BATCH-03 keeps full EditMode coverage (10-02, 1197/1197) pending on-device confirmation behind 09-04"
  - "The SetReplyMode 404 is recorded as EXPECTED (undeployed dev webhook), not a Phase-10 regression — the client write path is correct, the server webhook is 09-04's open deploy"

patterns-established:
  - "UAT-debt carry pattern: cross-phase-blocked + owner-deferred scenarios recorded with evidence + standing coverage, re-verified alongside the unblocking phase (09-04/09-05) rather than re-planned here"

requirements-completed: [BATCH-01, BATCH-02, BATCH-03]  # already marked complete by 10-01/10-02/10-03 code plans; mark-complete returns changed:0 (BATCH ids not formalized in REQUIREMENTS.md, definitions in 10-CONTEXT.md)

# Metrics
duration: owner-gate
completed: 2026-07-22
---

# Phase 10 Plan 04: Message-Batching HUMAN-UAT Gate Summary

**The phase-closing owner e2e recorded three PASS verdicts that behaviorally confirm the auto-reply combine half (multi-fragment -> ONE combined reply on both WhatsApp and Telegram; single message -> one reply; humanizer pauses unchanged); the suggestions-coalesce scenario is BLOCKED by the still-open Phase-9 09-04 SetReplyMode deploy (a recorded 404, not a Phase-10 defect) and the composition check is DEFERRED to post-Phase-9 by explicit owner decision — plan closed with those two scenarios tracked as UAT debt.**

## Performance

- **Duration:** owner-gate (author runbook -> owner device e2e -> record verdicts)
- **Tasks:** 2 (Task 1 `auto` — author the runbook; Task 2 `checkpoint:human-verify` — owner-run device e2e)
- **Files modified:** 1 (`10-HUMAN-UAT.md` — created in Task 1, verdict-filled after Task 2)

## Accomplishments
- **Task 1 (authored + committed `b3b2e38`):** `10-HUMAN-UAT.md` owner runbook — 5 scenarios each with EXPECT + PASS/FAIL, a pre-flight block (fresh device build carrying the 10-02 debounce, `rotate-tunnel.py`-before-build to avoid the -1003 baked-URL trap, activate the test clone for the window), a post-run block (deactivate clone; prod dormant), the ~8s accepted-latency note, the immediate manual-refresh/card-pick sub-checks, the «Бот работает/пауза»-untouched sub-check, and the honest scenario-5 minimal SQL-suppression path (given `/webhook/SetReplyMode` is not live on dev).
- **Task 2 (owner-run device e2e):** verdicts recorded — **scenarios 1-3 PASS** (WhatsApp combine, WhatsApp single, Telegram combine); **scenario 4 BLOCKED** by 09-04; **scenario 5 DEFERRED** by owner.
- **Behavioral proof (BATCH-01/02):** a multi-fragment customer message produces exactly ONE combined reply on BOTH channels, a single complete message still replies once, and the humanizer read/type pauses feel unchanged at the accepted ~8s window.
- **Real-contacts discipline confirmed:** owner confirmed the test reply-workflow clone was DEACTIVATED after the window, the scenario-5 `reply_mode_flags` row was deleted, and prod bagkz stayed dormant/untouched.

## Recorded UAT verdicts

| # | Scenario | Channel(s) | Result | Evidence |
|---|----------|-----------|--------|----------|
| 1 | Multi-fragment combine | WhatsApp | ☑ PASS | one combined reply, both fragments answered |
| 2 | Single message | WhatsApp | ☑ PASS | one reply after window; pauses natural; 8s accepted |
| 3 | Multi-fragment combine | Telegram | ☑ PASS | one combined reply on Telegram |
| 4 | Suggestions coalesce | WhatsApp + Telegram | ⛔ BLOCKED (09-04) | SetReplyMode 404; BATCH-03 EditMode-covered 1197/1197 |
| 5 | Semi-auto skips path | WhatsApp (SQL row) | ⏸ DEFERRED | owner will verify post-Phase-9 |

**Count:** passed 3 · blocked 1 · deferred 1.

## Task Commits

1. **Task 1: Author the 10-HUMAN-UAT.md owner e2e runbook** — `b3b2e38` (docs)
2. **Task 2: Owner runs the batching e2e on one build** — owner-run device checkpoint; verdicts recorded into `10-HUMAN-UAT.md` (this docs commit).

**Plan metadata:** _(this docs commit — SUMMARY + 10-HUMAN-UAT verdicts + STATE + ROADMAP)_

## Files Created/Modified
- `.planning/phases/10-message-batching-debounce/10-HUMAN-UAT.md` — NEW (Task 1), then verdict-filled after Task 2: per-scenario results, the scenario-4 blocker-evidence block (SetReplyMode 404 + standing EditMode coverage), the scenario-5 deferral note, the verdict table, checked post-run block, and a resolved PARTIAL final-disposition block.

## Decisions Made
- **Scenarios 4 and 5 are NOT passed** — 4 is blocked by an OTHER phase's open gate (09-04 SetReplyMode), 5 is deferred by the owner; recording either as PASS would be dishonest. Both are tracked as UAT debt (see Deferred Items) so `/gsd-progress` and `/gsd-audit-uat` keep surfacing them until re-verified.
- **Close the plan at status: partial under the owner's explicit continue-decision (2026-07-22)** — the phase's user-observable core (auto combine, BATCH-01/02) is behaviorally green on both channels; BATCH-03's coalesce logic is EditMode-proven (10-02, 1197/1197) and only its on-device behavioral confirmation is outstanding, which re-runs trivially once 09-04 deploys SetReplyMode.
- **The SetReplyMode 404 is EXPECTED, not a defect** — the client write path (09-03) is correct; the webhook is 09-04's undeployed deploy. Recorded as a cross-phase blocker with the exact error, not a Phase-10 bug.

## Deviations from Plan

None — plan executed as written. Task 1 authored the runbook per spec; Task 2's checkpoint returned as designed and the owner supplied verdicts. Scenarios 4/5 not completing is a documented cross-phase dependency (the plan itself flagged that the full «Вместе» app-toggle e2e belongs to 09-04/09-05), not a deviation in execution.

## Issues Encountered
- **Cross-phase blocker (scenario 4):** switching a chat to Semi-auto in-app 404'd on the server sync — `[SetReplyMode] [404] http://localhost:5678/webhook/SetReplyMode: The requested webhook "POST SetReplyMode" is not registered` (from `Manager/<SyncReplyModeRoutine>` at `Assets/Scripts/Main/Manager.ReplyModeSync.cs:105`). This is the expected consequence of `/webhook/SetReplyMode` being undeployed on dev (Phase-9 09-04 Task 2, still open). The behavioral coalesce observation was therefore not completed; BATCH-03 retains its automated coverage (10-02's 6 EditMode tests incl. the burst-then-chat-switch regression, suite 1197/1197). Side note (no action): the `localhost` URL means this check ran on the Mac (Editor or iOS Simulator), which is legitimate for a client-side scenario.
- **Owner deferral (scenario 5):** the composition/ordering check (semi-auto chat skips the whole reply path before the debounce) was deferred to post-Phase-9 by explicit owner decision; owner asked to continue/close the phase now without additional setup.

## Threat Mitigations (from the plan's threat register)
- **T-10-04-01** (test clone left ACTIVE) — mitigated: owner confirmed the reply-workflow clone was DEACTIVATED after the window (real contacts).
- **T-10-04-02** (gate-vs-debounce ordering / semi-auto chat waiting or replying) — NOT observed this gate: scenario 5 deferred to post-Phase-9; the ordering guarantee (suppression before debounce) rides the 09-04/09-05 re-verify. Tracked as debt.
- **T-10-04-03** (prod bagkz accidental target) — upheld: all e2e dev-only; owner confirmed prod bagkz untouched/dormant; the scenario-5 `reply_mode_flags` row was deleted.

## Known Stubs
None.

## User Setup Required
All live steps were owner-run: fresh device build (10-02 debounce), `rotate-tunnel.py` before build, dev n8n + tunnel, activate/deactivate the test clone, run the 5 scenarios, delete the scenario-5 SQL row. `secrets.json` / dev n8n / tunnel remain owner-run (deny-ruled for Claude).

## Next Phase Readiness
- **Auto-reply combine half (BATCH-01/02) behaviorally COMPLETE** on both channels — ready for `/gsd-secure-phase 10`, with scenarios 4-5 carried as tracked UAT debt.
- **Scenario 4 (suggestions coalesce) re-verify** re-runs trivially once **09-04** deploys `/webhook/SetReplyMode`; BATCH-03 stays EditMode-green in the meantime.
- **Scenario 5 (composition)** re-verifies alongside the 09-04/09-05 «Вместе» app-toggle e2e (owner deferral).
- **Prod bagkz replication** must fold in the debounce splice + the `binaryMode` orchestrator strip (10-03) — stays dormant until run.

## Self-Check: PASSED

- Created file exists: `10-HUMAN-UAT.md` — FOUND (verdicts recorded)
- Task 1 commit exists: `b3b2e38` — FOUND
- Verdicts recorded (1-3 PASS, 4 BLOCKED-by-09-04 with 404 evidence, 5 DEFERRED); counts passed:3 / blocked:1 / deferred:1; owner continue-decision (2026-07-22) recorded as the close authorization.

---
*Phase: 10-message-batching-debounce*
*Completed: 2026-07-22*
