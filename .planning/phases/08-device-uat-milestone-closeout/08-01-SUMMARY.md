---
phase: 08-device-uat-milestone-closeout
plan: 01
subsystem: testing
tags: [device-uat, runbook, telegram-parity, milestone-gate, human-verify]

# Dependency graph
requires:
  - phase: 05-channel-aware-chatmanager-core
    provides: 05-HUMAN-UAT / 05-VERIFICATION / 05-06-REVIEW WR-02 device-reverify items (auth 2FA, media treatments, vthumb probe, 05-09 field fixes)
  - phase: 06-channel-switcher-ui
    provides: 06-HUMAN-UAT switcher visual/interaction gate + 2 deferred-polish decisions
  - phase: 04-n8n-telegram-template-parity-dev
    provides: 04-HUMAN-UAT TPL-06 auto-reply e2e gate (rides dev-n8n session)
  - phase: 07-vmeste-suggestions-dashboard-on-telegram
    provides: 07-HUMAN-UAT live «Вместе» + dashboard live-data gate
provides:
  - "08-DEVICE-UAT.md — one consolidated owner-run device-UAT runbook aggregating every still-open v1.1 gate (groups A–I) + carried v1.0, each item source-ref'd"
affects: [08-02-prod-replication, 08-03-milestone-close, gap-closure-plans]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Consolidated milestone-gate runbook: one ordered owner pass aggregating scattered per-phase gate docs, each item = expected/how-to/PASS·FAIL·N/A + source-ref for traceable FAILs"

key-files:
  created:
    - .planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md
  modified: []

key-decisions:
  - "Group I (carried v1.0) items get an explicit RE-DEFER verdict box (run OR re-defer with a reason is a valid disposition per the plan; rolls forward via 08-03)"
  - "Workflows/credentials referenced by NAME only (no ids/tokens/phone numbers) to satisfy T-08-01 information-disclosure mitigation"
  - "Groups G+H carry a shared dev-n8n-session note + G's non-scored preconditions (verifier exit-0, Postgres UPDATE-grant, rotate-tunnel) so the two n8n-dependent groups run back-to-back in one window"

patterns-established:
  - "Milestone device-UAT consolidation: pull the exact PASS/FAIL items + source anchors from each phase's gate doc into a single grouped checklist; every FAIL maps to its origin via a Defects table"

requirements-completed: []  # plan frontmatter requirements: [] — closeout phase owns no v1.1 REQ-ID

# Metrics
duration: 4min
completed: 2026-07-15
---

# Phase 8 Plan 01: Consolidated Device-UAT Runbook Summary

**08-DEVICE-UAT.md — one ordered owner-run device pass aggregating every still-open v1.1 gate (auth+2FA, chat/media incl. the 05-07/08/09 treatments, vthumb id-ambiguity probe, switcher, auto-reply e2e, live «Вместе»+dashboard) plus the carried v1.0 scenarios, each item source-ref'd for traceable FAILs.**

## Performance

- **Duration:** ~4 min (authoring; dominated by reading the 8 source gate docs + context)
- **Started:** 2026-07-15T09:17:40Z
- **Completed:** 2026-07-15T09:21:23Z
- **Tasks:** 1 of 2 executed (Task 2 is the owner-run gate — NOT executed; see Checkpoint below)
- **Files modified:** 1 created

## Accomplishments

- Authored `08-DEVICE-UAT.md` (428 lines): a Preamble (status OPEN/owner-run; environment = real device build + authorized dev Telegram profile + a 2FA-enabled account + the shared dev-n8n session for Groups G/H), then grouped gate sections **A–I**, a **Defects found** table, and an **Overall** line.
- Every item uses the exact shape `**expected:** … | **how-to:** … | **verdict:** ☐ PASS ☐ FAIL ☐ N/A | **source:** <doc-ref>`, aggregated verbatim from 08-CONTEXT §08-01 with the specific items pulled from each @-referenced source gate doc:
  - **A** Auth — Telegram phone/code + 2FA cloud-password (both code + QR flows) ← 05-VERIFICATION.md #3 (+ 05-05 SUMMARY)
  - **B** Chat client — list/history/media (image/video/voice/document + `.tgs` sticker CARD, кружок bubble-free circle, GIF badge, static webp), send text/media/quoted-reply, reaction toggle, incoming reply card, mark-read, swipe-delete hidden on TG, plus the "expected non-defects" observations ← 05-VERIFICATION.md #1/#2 + 05-HUMAN-UAT.md #1-3 (+ 05-08)
  - **C** 05-09 field/UI fixes — clean Telegram number (never the JSON blob), chip-label padding, no outside-app de-auth ← 05-HUMAN-UAT.md #4/#5/#6
  - **D** vthumb id-ambiguity probe (two TG dialogs, colliding numeric ids) ← 05-VERIFICATION.md #4 / **05-06-REVIEW WR-02** / STATE blocker
  - **E** video-note `is_round` re-capture (optional/minor) ← 05-08 / 05-HUMAN-UAT.md #2
  - **F** switcher (placement, mid-flight-safe flip, muted→connect, auto-select, per-bot persistence, 4-tab «Чаты» bar) + 2 record-owner-decision lines ← 06-HUMAN-UAT.md
  - **G** auto-reply e2e (text/voice/memory/pre-auth RAG re-stamp/recreate-clone/deactivate) ← 04-HUMAN-UAT.md
  - **H** live «Вместе» + dashboard live-data ← 07-HUMAN-UAT.md
  - **I** carried v1.0 — 4 pending 01 scenarios + 5 pending 02 scenarios + the 01-VERIFICATION device confirmation, each **run OR re-defer** ← 01-HUMAN-UAT.md / 02-HUMAN-UAT.md / 01-VERIFICATION.md
- Groups G/H carry a shared dev-n8n-session note; G lists its non-scored preconditions (verifier exit-0, Postgres UPDATE-grant, `rotate-tunnel.py`). Prod bagkz explicitly untouched.
- Defects table maps each FAIL → source-anchor → gap-closure plan (`/gsd-plan-phase 08 --gaps`); pre-planning fixes is out of scope.
- **All 158 checkboxes ship blank** (0 ticked); no tokens/keys/JWTs/apikeys/phone-numbers in the doc (scan clean).

## Task Commits

1. **Task 1: Write the consolidated 08-DEVICE-UAT.md runbook** — `2500e6d` (docs)
2. **Task 2: Owner runs the consolidated device pass** — NOT executed (blocking owner-run gate; see Checkpoint)

**Plan metadata:** committed with this SUMMARY (STATE.md / ROADMAP.md left to the orchestrator per checkpoint-phase handling).

## Files Created/Modified

- `.planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md` — the consolidated owner-run device-UAT runbook (groups A–I + Defects + Overall)

## Decisions Made

- **Group I RE-DEFER box:** the plan mandates group I items be "run OR explicitly re-defer with a reason," so those items use `☐ PASS ☐ FAIL ☐ RE-DEFER (reason: ___)` instead of the default `N/A` box; the other groups keep the exact shape. A note explains this in-group.
- **Credentials/workflows by name only:** referenced Telegram_Bot / CreateTelegramWorkflow / Suggest_Replies and the Wappi token / n8n API key / Postgres cred by NAME, with no ids/tokens/phone numbers (T-08-01 mitigation).
- **Included switcher pill placement/styling (F1)** from 06-HUMAN-UAT §1 as a faithful part of the switcher gate (in-source, not new scope).
- **Verification chain:** the plan's Task-1 automated grep chain returns OK.

## Deviations from Plan

None — plan executed exactly as written. This is a doc-only plan; no app code touched (`git status --porcelain Assets/` shows only pre-existing untracked assets from before this session, none authored here). No trivially-obvious pre-device fix surfaced during authoring (the "Pre-device notes" line in the runbook reads "none").

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required to AUTHOR the runbook. RUNNING it (Task 2) is the owner gate and requires a device build + dev n8n session (documented in the runbook's Environment section).

## Checkpoint — Task 2 is a PENDING owner gate

This plan's autonomous scope (Task 1) is **complete and committed**, but the plan **remains gated on the owner**. Per the plan frontmatter (`autonomous: false`) and the Task 2 `checkpoint:human-verify` (gate="blocking"):

- **Status:** awaiting owner gate — the phase stays `human_needed` until the owner records results in `08-DEVICE-UAT.md`.
- **What the owner must run:** produce a real device build (Android primary), work `08-DEVICE-UAT.md` top-to-bottom, and record PASS/FAIL/N/A (RE-DEFER for group I) per item. For Groups G+H, first start dev n8n (`localhost:5678`) + cloudflared tunnel and run `Tools/n8n/rotate-tunnel.py` (do NOT touch prod bagkz). File any FAIL in the Defects table and set the Overall line.
- **Resume signal:** owner types "approved" once results are recorded (Overall = PASS, or FAILs filed as gaps via `/gsd-plan-phase 08 --gaps`), or describes the FAILs to spin gap-closure plans.

## Next Phase Readiness

- The consolidated runbook is ready for the owner's single device pass; it is the go/no-go for "is v1.1 shippable."
- Downstream: 08-02 (prod bagkz replication) and 08-03 (milestone close) proceed after the owner signs off here (and after 08-02's own owner deploy). Any FAIL becomes its own gap-closure plan rather than blocking mechanically.

## Self-Check: PASSED

- `08-DEVICE-UAT.md` exists ✓ · `08-01-SUMMARY.md` exists ✓
- Task-1 commit `2500e6d` present in git log ✓
- Plan Task-1 automated grep verify chain returns OK ✓ (2FA, 05-VERIFICATION, 05-06-REVIEW WR-02, vthumb, .tgs, GIF, all HUMAN-UAT refs, Defects, PASS, FAIL all present)
- All 158 checkboxes blank / 0 ticked ✓ · no token/JWT/apikey/phone-shaped strings ✓ · no app code touched ✓

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-15 (autonomous scope; Task 2 owner gate PENDING)*
