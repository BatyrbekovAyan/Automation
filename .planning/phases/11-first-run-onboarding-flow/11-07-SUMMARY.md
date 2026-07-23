---
phase: 11-first-run-onboarding-flow
plan: 07
subsystem: ui
tags: [onboarding, human-uat, device-gate, runbook, telegram-parity, unity]

# Dependency graph
requires:
  - phase: 11-first-run-onboarding-flow (plans 01-06)
    provides: the full onboarding surface under test (carousel + gate, trust blocks, success moment, checklist)
  - phase: 11-first-run-onboarding-flow (gap plans 08-10)
    provides: D2 standalone SuccessOverlay (11-08), D1 FirstStepsCardVisibility + D3 RefreshFromFacts hooks (11-09), Round-2 re-verify addendum (11-10)
provides:
  - "11-HUMAN-UAT.md: owner-run device gate runbook covering ONB-01..ONB-05 (fresh-install, existing-user, both-channel, settings re-auth, regression paths) with Round-1 + Round-2 verdicts recorded"
  - "Phase-11 gate verdict: Round 2 PASS, owner-approved 2026-07-23 — ONB-01..ONB-05 proven on device"
affects: [phase-11-close, v1.3-milestone]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Consolidated owner device gate: autonomous runbook authoring + checkpoint, owner runs it; FAILs become a defects table that seeds gap-closure plans; a focused Round-2 addendum re-verifies only the defects"

key-files:
  created:
    - .planning/phases/11-first-run-onboarding-flow/11-HUMAN-UAT.md
  modified:
    - .planning/phases/11-first-run-onboarding-flow/11-HUMAN-UAT.md

key-decisions:
  - "Runbook mirrors the proven 06/08 gate shape (Status line, Setup, numbered owner checklist, Blocks note, defects table, Overall) and is self-contained so it runs independently of the still-open Phase-8 gate sharing the owner's device time"
  - "Fresh-run reset uses the sanctioned «Удалить все данные» wipe (Профиль → Аккаунт) — never hand-deleted PlayerPrefs keys"
  - "Round-1 record kept byte-intact when transcribing the Round-2 approval — only the Round-2 addendum items, Round-2 Overall, top status line, and frontmatter were updated"

requirements-completed: [ONB-01, ONB-02, ONB-03, ONB-04, ONB-05]

# Metrics
duration: "~15 min authoring 2026-07-18 + owner rounds through 2026-07-23 (gate wall-clock 5 days)"
completed: 2026-07-23
---

# Phase 11 Plan 07: Owner Device UAT Gate (11-HUMAN-UAT.md) Summary

**The phase-closing owner gate: an autonomous ONB-01..05 device runbook (fresh-install, existing-user, both-channel, settings re-auth, and regression paths) whose Round-1 run found 3 defects (D1 EmptyState/card overlap, D2 success sheet stacked on the auth UI, D3 stale checklist rows), which the 11-08..11-10 gap round closed — Round 2 was approved by the owner on 2026-07-23 with the suite at 1209/1209, proving ONB-01..ONB-05 and closing Phase 11.**

## Performance

- **Duration:** ~15 min active authoring (2026-07-18) + verdict recording (2026-07-23); gate wall-clock spanned the Round-1 run, the 11-08..11-10 gap round, and the Round-2 approval
- **Started:** 2026-07-18
- **Completed:** 2026-07-23
- **Tasks:** 2 (1 auto — author the runbook; 1 checkpoint:human-verify — owner device pass)
- **Files modified:** 1 (11-HUMAN-UAT.md created, then verdicts recorded)

## Accomplishments

- **Task 1 (2026-07-18, `8da2c3d`):** Authored `11-HUMAN-UAT.md` mirroring the 06/08 gate shape — Setup with the sanctioned «Удалить все данные» first-run reset, a verbatim RU copy deck, five numbered checklist sections (carousel/gate ONB-01, trust blocks ONB-02, success moment ONB-03, checklist ONB-04, zero regression ONB-05, 36 items), the Pitfall-2 auth-code-flow regression check, an owner defects table, and the "closes Phase 11" Blocks note. Checkpoint returned; executor did not perform device verification.
- **Round 1 (owner, 2026-07-18):** Overall **ISSUES** — carousel, trust blocks, and deep-links worked; 3 defects recorded in the runbook's table: **D1** (medium — with zero bots the EmptyState AND «Первые шаги» card rendered overlapping), **D2** (high — the success sheet rendered stacked ON TOP of the still-visible code-entry UI), **D3** (medium — checklist rows stale until navigate-away-and-back). Suite 1165/1165.
- **Gap round (11-08..11-10, 2026-07-23):** 11-08 relocated the success moment to a standalone Canvas-level `SuccessOverlay` (single field set, nested SuccessCta clusters torn down); 11-09 added the pure `FirstStepsCardVisibility.ShouldShow` gate (CanvasGroup hide) + `RefreshFromFacts()` at 5 fact-change hooks; 11-10 appended the focused «Round 2 re-verify (D1–D3)» addendum. Suite rose to **1209/1209** (1205 v1.2-lifted baseline + 4 new D1 tests).
- **Round 2 (owner, 2026-07-23):** **PASS — owner replied «approved»**; D1–D3 verified fixed on device (D2 overlay clean on both channels + settings re-auth; D1 EmptyState alone owns the zero-bot screen; D3 card mirrors facts live), zero-regression re-check green, no new defects. **ONB-01..ONB-05 proven; Phase 11 gate CLOSED.**
- **Task 2 wrap-up (this session):** transcribed the owner's Round-2 approval into the runbook (13 Round-2 items ticked PASS, Round-2 Overall ☑ PASS, suite 1209/1209 recorded, frontmatter `status: passed`, top status line records the full Round-1 → gap → Round-2 arc) with the Round-1 historical record byte-intact.

## Task Commits

1. **Task 1: author 11-HUMAN-UAT.md runbook** - `8da2c3d` (docs)
2. **Task 2: record Round-2 owner approval + gate close** - this plan's final docs commit (runbook verdicts + SUMMARY + STATE/ROADMAP)

Gap-round work (Round-1 defects → fixes) was committed under its own plans: 11-08 (`fc7a55e`/`7808aa0`/`a4fba79`), 11-09 (`18abd22`/`b0d0d7f`/`559b89d`/`c2b996c`), 11-10 (`70512cd`).

## Files Created/Modified

- `.planning/phases/11-first-run-onboarding-flow/11-HUMAN-UAT.md` - Owner gate runbook: Round-1 checklist + defects (D1–D3, ISSUES) and Round-2 re-verify addendum (all PASS, approved 2026-07-23); `status: passed`.

## Decisions Made

- Mirrored the 06/08 gate format (proven with this owner across two milestones) rather than inventing a new one; kept the runbook self-contained so it never depends on the also-open Phase-8 device gate.
- Recorded the Round-2 ticks as an explicit **transcription of the owner's «approved» verdict** (same convention as 08-DEVICE-UAT's recorded results) — authoring shipped every checkbox blank.
- Did NOT mark the phase complete in ROADMAP's phase-header line or run SDK state mutations — the orchestrator owns the phase-completion step; STATE/ROADMAP were updated surgically (11-07 checkbox ticked by hand, per the 11-08..11-10 precedent).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Removed stray Write-artifact tags from the runbook tail**
- **Found during:** Task 2 verdict recording
- **Issue:** The 11-10 append left literal `</content>` / `</invoke>` lines at the end of 11-HUMAN-UAT.md (a Write-tool artifact — the closing tags were accidentally included in the file content).
- **Fix:** Deleted the two stray lines while updating the footer; no content loss (the footer text itself was intact).
- **Files modified:** .planning/phases/11-first-run-onboarding-flow/11-HUMAN-UAT.md
- **Verification:** `grep -c '</content>\|</invoke>'` returns 0; Round-1/Round-2 payload counts all verified (13 PASS ticks, 3 defect rows, both Overall lines).

---

**Total deviations:** 1 auto-fixed (stray artifact cleanup). No scope change.

## Issues Encountered

- **REQUIREMENTS.md has no ONB rows to tick** — per the design spec, ONB-01..ONB-05 are "formalized in REQUIREMENTS.md at v1.3 milestone start", which has not happened yet (the file is v1.1/v1.2-scoped). The requirements are recorded as proven here and in the runbook; the checkbox tick happens when v1.3's REQUIREMENTS entries exist.
- **Suite-count drift across the gate's lifetime** — the runbook's Round-1 expectation (1165) predates the v1.2 Phase 9/10 tests landing mid-phase; 11-10 corrected the Round-2 target to 1209 (1205 baseline + 4 new 11-09 tests). Both counts are recorded in their respective rounds.

## Known Stubs

None — this plan's artifact is the gate document itself, now fully dispositioned (Round 1 ISSUES → Round 2 PASS).

## Threat Model Compliance

Both `mitigate` dispositions carried by the gate held: T-11-07-01 (the runbook explicitly re-ran both auth code flows incl. Telegram 2FA after the trust card + success re-sequence — Round-1 §2/§5 and the Round-2 zero-regression re-check, all green by Round 2), T-11-07-02 (verbatim trust copy verified rendering and reading honestly at device size — Round-1 §2 with no copy defect filed).

## User Setup Required

None.

## Next Phase Readiness

- Phase 11 is fully dispositioned: all 7 original plans + the 3 gap plans complete, and the owner gate is PASSED. ONB-01..ONB-05 proven on device.
- Next: the orchestrator's phase-completion/verification step (and `/gsd-secure-phase 11` per the phase-close pipeline). The ONB requirement rows land in REQUIREMENTS.md at v1.3 milestone formalization.
- Open neighbors unaffected by this close: Phase 8's 08-21 owner gate and Phase 9's 09-05 behavioral UAT remain open on their own tracks.

## Self-Check: PASSED

- `11-HUMAN-UAT.md` present with `status: passed`, 13/13 Round-2 PASS ticks, Round-2 Overall ☑ PASS, Round-1 record intact (3 defect rows, ☐ PASS ☑ ISSUES), zero stray tags.
- Task-1 commit `8da2c3d` present in git history; gap-round commits (`fc7a55e`, `70512cd`, et al.) present under their own plans.

---
*Phase: 11-first-run-onboarding-flow*
*Completed: 2026-07-23*
