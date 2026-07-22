---
phase: 09-semi-auto-suppression
plan: 05
subsystem: testing
tags: [owner-gate, human-uat, e2e, suppression, reply-mode-flags, whatsapp, telegram, semi-auto, device-build]

# Dependency graph
requires:
  - phase: 09-semi-auto-suppression (09-02)
    provides: "the fail-closed Read Reply Mode → Suppressed? gate spliced into both bot templates — the branch this gate exercises behaviorally"
  - phase: 09-semi-auto-suppression (09-03)
    provides: "the Unity client write path (Manager.ReplyModeSync + the 3 toggle/default/re-assert write sites) shipped in the device build under test"
  - phase: 09-semi-auto-suppression (09-04)
    provides: "the LIVE server side — reply_mode_flags + default-deny RLS, /webhook/SetReplyMode (SCLcpn6DMDG3Z4VN, cred vvRrFiEXzLVqKjOx), curl/precedence matrix, suppressed dead-end runData both channels, fresh-bot gate inheritance"
provides:
  - "Behavioral confirmation on a real device across BOTH channels: «Вместе» → NO auto-reply + chat stays UNREAD + suggestions panel STILL populates; «Авто» → auto-replies restore"
  - "Re-assert-on-open heal proven live: re-opening a suppressed chat keeps it suppressed"
  - "Bot-wide '*' default proven live: a NEVER-opened chat is suppressed when the bot default is «Вместе»"
  - "Telegram parity proven live: identical suppress/restore on a Telegram-authed bot (gate keys on the per-channel profile id)"
  - "Absence → reply proven live: a never-toggled chat on an «Авто» bot replies normally (never-silenced common case)"
  - "The «Бот работает / Бот на паузе» activation switch proven UNTOUCHED — pauses/resumes independently of «Авто/Вместе»"
  - "Phase 9 behaviorally COMPLETE — ready for /gsd-secure-phase 09"
affects: [phase-09-verification, gsd-secure-phase-09, 10-HUMAN-UAT (scenarios 4/5 debt now unblocked), prod bagkz replication (SUP-05 bulk copy)]

# Tech tracking
tech-stack:
  added: []  # owner-run behavioral gate — no code, no libraries
  patterns:
    - "phase-closing HUMAN-UAT gate: structural + curl + runData proof (09-04) precedes a single on-device behavioral confirmation the executor cannot run (secrets/tunnel/dev-n8n/real-profiles all owner-run)"
    - "runbook enforces the real-contacts discipline in-band: pre-flight ACTIVATE-only-for-window + post-run DEACTIVATE checkboxes"

key-files:
  created:
    - .planning/phases/09-semi-auto-suppression/09-HUMAN-UAT.md
    - .planning/phases/09-semi-auto-suppression/09-05-SUMMARY.md
  modified: []

key-decisions:
  - "The optional Phase-10 debt appendix (10-HUMAN-UAT scenarios 4/5) was NOT recorded this session — the owner's resume signal covered the five Phase-9 scenarios only; the appendix is marked not-recorded and its formal closure stays tracked in 10-HUMAN-UAT.md (not edited from here)"
  - "SUP-01..05 are not yet in a REQUIREMENTS.md (v1.2 formalization pending at milestone start); requirements.mark-complete is a no-op (changed:0) by design — the definitions live in 09-CONTEXT.md and the ROADMAP Phase-9 success criteria"

patterns-established:
  - "Owner gate resume: record verbatim signal + PASS verdicts in the HUMAN-UAT file, flip Status/verdict-table/disposition, then author SUMMARY — no executor-run verification of a live-account device pass"

requirements-completed: [SUP-03, SUP-05]

# Metrics
duration: owner-gate (runbook authored 2026-07-22; owner e2e run + verdicts recorded same day)
completed: 2026-07-22
---

# Phase 9 Plan 05: Semi-Auto Suppression HUMAN-UAT Gate Summary

**The phase-closing behavioral gate PASSED: on one device build across BOTH WhatsApp and Telegram, a «Вместе» chat gets NO auto-reply and stays UNREAD while the suggestions panel still populates, «Авто» restores replies, the bot-wide `'*'` default suppresses a never-opened chat, re-opening a suppressed chat self-heals, a never-toggled chat still replies (absence→reply), and the «Бот работает/пауза» activation switch pauses/resumes independently — all 5 scenarios PASS, test clone deactivated after the window, prod dormant.**

## Performance

- **Duration:** owner-gate — runbook authored + owner e2e run + verdicts recorded 2026-07-22
- **Tasks:** 2 (Task 1 `auto` — author the runbook; Task 2 `checkpoint:human-verify` gate="blocking" — owner-run on-device e2e)
- **Files modified:** 1 authored (`09-HUMAN-UAT.md`) + this SUMMARY; no code diff (behavioral gate)

## Accomplishments

- **Task 1 — CLOSED.** Authored `.planning/phases/09-semi-auto-suppression/09-HUMAN-UAT.md`: all 5 scenarios each with an explicit EXPECT + PASS/FAIL field, the re-assert-on-open heal sub-check (scenario 1), the «Бот работает/пауза»-untouched sub-check (scenario 5), a pre-flight block (rotate tunnel BEFORE build → fresh 09-03 build → SetReplyMode live → gate live → Suggest Replies ON → clone ACTIVE-only-for-window → real WA+TG profiles) and a post-run block (DEACTIVATE clone, flip bot default back to «Авто», prod dormant), a verdict table, a disposition line, and an OPTIONAL Phase-10 UAT debt appendix (10-HUMAN-UAT scenarios 4/5). Automated verify passed (`UAT_DOC_OK`).
- **Task 2 — CLOSED, ALL 5 PASS.** The owner installed a fresh build (09-03 client), brought dev n8n + tunnel up, activated the test reply-workflow clone for the window, and ran all 5 scenarios on BOTH channels:
  - **Scenario 1 (WhatsApp suppress + heal) — PASS.** «Вместе» → no auto-reply, chat stayed unread, suggestions panel still populated; re-opening the chat kept it suppressed (heal held).
  - **Scenario 2 (WhatsApp restore) — PASS.** «Авто» → auto-replies restored.
  - **Scenario 3 (bot-wide `'*'` default) — PASS.** Bot default set to «Вместе» → a never-opened chat's incoming message was not auto-replied (the `'*'` default row suppressed it).
  - **Scenario 4 (Telegram suppress/restore) — PASS.** Telegram parity held — «Вместе» → no reply + stayed unread + suggestions still populated; «Авто» → replies restored (gate keyed on the Telegram profile id).
  - **Scenario 5 (absence→reply + activation switch untouched) — PASS.** Never-toggled chat replied normally (absence → reply); the «Бот работает / Бот на паузе» activation switch paused and resumed the bot independently of «Авто/Вместе».
  - **Post-run:** test reply-workflow clone(s) confirmed **deactivated** after the window (owner: "UAT pass — clone deactivated"); prod bagkz untouched/dormant.

## Task Commits

Task 2 is an owner-run live-account on-device gate with no repo diff of its own. The repo changes this plan produced:

1. **Task 1: Author the 09-HUMAN-UAT.md owner e2e runbook** — `a7c5de2` (docs)

**Plan metadata:** _(this docs commit — runbook verdicts + SUMMARY + STATE + ROADMAP)_

## Files Created/Modified

- `.planning/phases/09-semi-auto-suppression/09-HUMAN-UAT.md` — NEW; the 5-scenario owner runbook, now with all verdicts recorded PASS, Status → PASSED, verdict table filled, disposition → ALL PASS, post-run checkboxes ticked, and the optional Phase-10 appendix marked "not recorded this session".
- `.planning/phases/09-semi-auto-suppression/09-05-SUMMARY.md` — NEW; this summary.

## Decisions Made

- **Optional Phase-10 debt appendix left un-verdicted.** The owner's resume signal was scoped to the five Phase-9 scenarios ("UAT pass — clone deactivated"); no verdicts were given for the optional appendix, so it is marked "not recorded this session" (no fabrication) and its formal closure stays in `10-HUMAN-UAT.md` — which was NOT edited from here.
- **SUP-01..05 requirement checkoff is a no-op.** The v1.2 REQUIREMENTS.md formalization is pending at milestone start; `requirements.mark-complete` returning `changed:0` is expected. Definitions live in `09-CONTEXT.md` and the ROADMAP Phase-9 success criteria; the SUMMARY frontmatter records `requirements-completed: [SUP-03, SUP-05]` from the plan for traceability.

## Deviations from Plan

None — plan executed exactly as written. Task 1 authored the runbook to spec (plus the project-known enrichments folded in: rotate-tunnel-before-build, fresh-09-03-build, SetReplyMode-live pre-flight, and the optional Phase-10 debt appendix). Task 2 stopped at the blocking owner gate and resumed on the owner's verbatim "UAT pass — clone deactivated" signal.

## Issues Encountered

None. The 09-04 live bring-up had already removed the only prior blocker (the `/webhook/SetReplyMode` 404 that blocked 10-04 scenario 4) — the webhook was registered for this gate, so the in-app «Вместе» flip reached the server on every scenario.

## User Setup Required

None — no new external service configuration. All live steps were owner-run (`secrets.json` / dev n8n / tunnel deny-ruled for Claude): rotate the tunnel, build, bring dev n8n + tunnel up, activate the test clone for the window, run the 5 scenarios on both channels, then deactivate the clone. Prod bagkz stayed dormant.

## Next Phase Readiness

- **Phase 9 is behaviorally COMPLETE.** Success criteria 1–2 (ROADMAP Phase 9; SUP-03) are user-confirmed on both channels; SUP-05's real-contacts discipline (clone ACTIVE-only-for-window → DEACTIVATED) was honored. Ready for **`/gsd-secure-phase 09`** after phase verification.
- **Phase-10 UAT debt (10-HUMAN-UAT scenarios 4/5)** remains open in `10-HUMAN-UAT.md` — now unblocked (SetReplyMode live) and re-verifiable in a future session; not recorded here.
- **Prod bagkz replication (SUP-05)** stays dormant — folds the suppression gate + the Postgres cred consolidation (bind to the id that exists on the prod instance) into the future one-shot bulk copy.

## Self-Check: PASSED

- Created files exist: `09-HUMAN-UAT.md`, `09-05-SUMMARY.md` — FOUND
- Task 1 commit exists: `a7c5de2` — FOUND
- Runbook verdicts recorded: Status → PASSED; all 5 scenarios ☑ PASS; verdict table filled; disposition → ALL PASS; post-run checkboxes ticked; clone-deactivated confirmed; optional appendix marked not-recorded (no fabrication)

---
*Phase: 09-semi-auto-suppression*
*Completed: 2026-07-22*
