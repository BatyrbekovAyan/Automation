---
phase: 11-first-run-onboarding-flow
plan: 01
subsystem: ui
tags: [onboarding, playerprefs, pure-logic, tdd, editmode-tests, nav-restructure, unity]

# Dependency graph
requires:
  - phase: 06-channel-switcher
    provides: NavRestructureBuilder.ReorderScreens + deterministic ScreenContainer order (auth pages last)
  - phase: existing
    provides: Bot.OpenSettings, BotSettings.OpenGeneralTab/OpenProductTab, Manager.openBotSettings, UploadedFilesStore
provides:
  - "OnboardingGate: pure first-run carousel gate + existing-user auto-flag predicates"
  - "OnboardingPageMath: nearest-page + page-to-normalized-X arithmetic for the 3-page carousel"
  - "SuccessCtaSelector: success-panel primary-CTA target (UploadPriceList | OpenChats)"
  - "FirstStepsChecklist: pure step-state + channel-label + 4/4-completion derivation"
  - "OnboardingKeys: 3 global PlayerPrefs key constants (Seen / ChecklistDone / FirstBotReplySeen)"
  - "Bot.OpenSettingsAtProductTab() + Bot.OpenSettingsAtGeneralTab() deep-link entries"
  - "NavRestructureBuilder.ReorderScreens now internal + Screen_Onboarding-aware"
affects: [onboarding-carousel-gate, onboarding-success-moment, first-steps-card, onboarding-screen-builder]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure static logic class (runtime asm, global namespace) + EditMode NUnit test, no MonoBehaviour harness"
    - "Additive public deep-link entries reusing an existing private open path (no behavior change)"
    - "internal static builder helper reachable cross-class within Assembly-CSharp-Editor"

key-files:
  created:
    - Assets/Scripts/Main/Onboarding/OnboardingKeys.cs
    - Assets/Scripts/Main/Onboarding/OnboardingGate.cs
    - Assets/Scripts/Main/Onboarding/OnboardingPageMath.cs
    - Assets/Scripts/Main/Onboarding/SuccessCtaSelector.cs
    - Assets/Scripts/Main/Onboarding/FirstStepsChecklist.cs
    - Assets/Tests/Editor/Chat/OnboardingGateTests.cs
    - Assets/Tests/Editor/Chat/OnboardingPageMathTests.cs
    - Assets/Tests/Editor/Chat/SuccessCtaSelectorTests.cs
    - Assets/Tests/Editor/Chat/FirstStepsChecklistTests.cs
  modified:
    - Assets/Scripts/Main/Bot.cs
    - Assets/Editor/NavRestructureBuilder.cs

key-decisions:
  - "Kept OnboardingKeys FirstStepsChecklist/etc pure — MonoBehaviours supply facts, pure classes only transform"
  - "Two separate Bot deep-link entries (Product + General tab) rather than one parameterized method — matches the two distinct onboarding call sites (success CTA / checklist rows)"
  - "ReorderScreens made internal (not public) — least-visibility seam for the same-assembly OnboardingScreenBuilder"

patterns-established:
  - "Onboarding decision logic extracted to pure static classes for EditMode testability (ChatRowSwipePolicy / ServerPageMath idiom)"

requirements-completed: [ONB-01, ONB-03, ONB-04, ONB-05]

# Metrics
duration: 19 min
completed: 2026-07-18
---

# Phase 11 Plan 01: Onboarding Pure-Logic Foundations + Code Seams Summary

**Five pure static onboarding classes (gate, page-math, success-CTA, checklist, key constants) with 29 EditMode tests, plus two additive code seams — `Bot` settings-at-tab deep-link entries and an onboarding-aware `NavRestructureBuilder.ReorderScreens` — with the full suite green at 1165/1165.**

## Performance

- **Duration:** 19 min
- **Started:** 2026-07-18T14:28:00Z
- **Completed:** 2026-07-18T14:47:00Z
- **Tasks:** 3 (2 TDD + 1 auto)
- **Files modified:** 11 (9 created, 2 modified)

## Accomplishments
- `OnboardingGate` / `OnboardingPageMath` / `SuccessCtaSelector` / `FirstStepsChecklist` pure classes + `OnboardingKeys` constants, all in the runtime assembly, global namespace, zero MonoBehaviour/PlayerPrefs coupling.
- 29 new EditMode tests (19 gate+math, 10 CTA+checklist) driven RED→GREEN and confirmed via the in-Editor bridge.
- `Bot.OpenSettingsAtProductTab()` + `Bot.OpenSettingsAtGeneralTab()` — additive public deep-link entries reusing the private `OpenSettings` path unchanged.
- `NavRestructureBuilder.ReorderScreens` promoted `private static` → `internal static` and taught the `Screen_Onboarding` slot (after `Screen_New`, before the auth pages which stay last).
- Full EditMode suite green at **1165/1165** (baseline 1136 + 29 new), both runtime and editor assemblies freshly recompiled.

## Task Commits

Each task committed atomically (TDD tasks = test → feat):

1. **Task 1 RED: gate + page-math tests (stubs)** - `be26c9e` (test)
2. **Task 1 GREEN: gate + page-math logic** - `ebb1c67` (feat)
3. **Task 2 RED: success-CTA + checklist tests (stubs)** - `ba2d2b1` (test)
4. **Task 2 GREEN: success-CTA + checklist logic** - `f8dc8bb` (feat)
5. **Task 3: Bot settings-at-tab entries + onboarding-aware ReorderScreens** - `513f89b` (feat)

**Plan metadata:** committed separately with STATE/ROADMAP/REQUIREMENTS updates (docs).

## Files Created/Modified
- `Assets/Scripts/Main/Onboarding/OnboardingKeys.cs` - 3 global PlayerPrefs key constants (Seen / ChecklistDone / FirstBotReplySeen)
- `Assets/Scripts/Main/Onboarding/OnboardingGate.cs` - `ShouldShowCarousel` / `ShouldAutoFlagSeen` predicates
- `Assets/Scripts/Main/Onboarding/OnboardingPageMath.cs` - `NearestPage` / `PageToNormalizedX` (Math.Clamp, guarded for pageCount<=1)
- `Assets/Scripts/Main/Onboarding/SuccessCtaSelector.cs` - `SuccessCta` enum + `Choose(hasFiles)`
- `Assets/Scripts/Main/Onboarding/FirstStepsChecklist.cs` - `ChannelLabel` / `StepStates` / `AllDone`
- `Assets/Tests/Editor/Chat/OnboardingGateTests.cs` - 7 gate cases
- `Assets/Tests/Editor/Chat/OnboardingPageMathTests.cs` - 12 page-math cases
- `Assets/Tests/Editor/Chat/SuccessCtaSelectorTests.cs` - 2 CTA cases
- `Assets/Tests/Editor/Chat/FirstStepsChecklistTests.cs` - 8 checklist cases
- `Assets/Scripts/Main/Bot.cs` - added two public settings-at-tab deep-link entries (OpenSettings stays private)
- `Assets/Editor/NavRestructureBuilder.cs` - ReorderScreens now internal + lists Screen_Onboarding in the correct slot

## Decisions Made
- Kept every new logic class pure (no PlayerPrefs / MonoBehaviour) so the entire onboarding decision surface is EditMode-testable without a scene harness — the phase's dominant pattern per 11-PATTERNS.md.
- Two distinct `Bot` deep-link methods (Product tab, General tab) rather than one tab-parameterized method, matching the two separate downstream call sites (success CTA «Загрузить прайс-лист» / checklist rows).
- `ReorderScreens` made `internal` (least visibility that still lets the same-assembly `OnboardingScreenBuilder` call it) rather than `public`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Reconciled a conflict between the plan's action text and its own acceptance criterion**
- **Found during:** Task 2 (checklist GREEN)
- **Issue:** The plan action instructed adding an XML-doc note on `ChannelLabel` mentioning `PlayerPrefs.GetInt(...)`, but the same task's acceptance criterion greps for **no** `PlayerPrefs` token anywhere in the file (a purity check). The two directives are mutually exclusive on the literal token.
- **Fix:** Kept the caller guidance (which persisted per-bot keys to pass, both defaulting to 1) but phrased it without the literal `PlayerPrefs` token, so the class is provably free of any PlayerPrefs coupling while still documenting the contract. The purity intent is fully honored; the class reads no persisted state.
- **Files modified:** Assets/Scripts/Main/Onboarding/FirstStepsChecklist.cs
- **Verification:** `grep -l PlayerPrefs` on both Task-2 files returns nothing; tests remained 10/10 green (comment-only change).
- **Committed in:** `f8dc8bb` (Task 2 GREEN commit, amended before finalizing)

---

**Total deviations:** 1 auto-fixed (1 bug — plan-internal contradiction reconciliation).
**Impact on plan:** No scope change; the resolution preserves both the documented caller contract and the purity guarantee. All plan behavior delivered as written.

## Issues Encountered
- **Environment mismatch — Editor was OPEN, not closed.** The execution context stated the Unity Editor was closed (use `Tools/run-tests-headless.sh`), but a live Editor (PID 1327) held the project lock, so the headless runner correctly refused. Used the sanctioned Editor-open path instead: the in-Editor `ClaudeTestBridge` (drop `Temp/claude/run-tests.trigger`, read `test-summary.json`). The bridge's poll is focus-gated, so each run was preceded by `open -a Unity` to bring the already-running instance to the foreground (activates the existing process — never launches a second instance). No Bee/backend compile crash occurred; every run recompiled cleanly and produced results. All RED/GREEN gates and the final full suite ran through the bridge.
- **Baseline count clarification:** the context noted a ~1118 baseline, but the live suite baseline is **1136** (08-20's expected count, now confirmed post-Editor-recovery). 1136 + 29 new = 1165, all green — consistent, no regression.

## Known Stubs
None — the RED-phase throwing stubs were fully replaced by real logic in each GREEN commit; the final full suite (1165/1165) exercises the real implementations.

## TDD Gate Compliance
Both TDD tasks followed RED → GREEN with distinct gate commits:
- Task 1: `test(11-01)` be26c9e (19/19 fail, NotImplementedException) → `feat(11-01)` ebb1c67 (19/19 pass).
- Task 2: `test(11-01)` ba2d2b1 (10/10 fail) → `feat(11-01)` f8dc8bb (10/10 pass).
No RED phase passed unexpectedly; no gate commit missing.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Pure foundations + code seams are in place for the downstream onboarding plans: the carousel gate (`OnboardingGate`), pager math (`OnboardingPageMath`), success deep-link (`SuccessCtaSelector` + `Bot.OpenSettingsAtProductTab`), and the «Первые шаги» card (`FirstStepsChecklist` + `Bot.OpenSettingsAtGeneralTab`).
- `NavRestructureBuilder.ReorderScreens` is reachable and Onboarding-aware, ready for `OnboardingScreenBuilder` to build `Screen_Onboarding` into the ScreenContainer and reorder without re-running `Build()`.
- No scene mutation and no `Manager.cs` edit occurred in this plan (as scoped); those land in later plans.

## Self-Check: PASSED
- All 9 created files present on disk (verified with `[ -f ]`).
- All 5 task commits present in git history (be26c9e, ebb1c67, ba2d2b1, f8dc8bb, 513f89b).
- All task acceptance criteria re-run and PASS; full EditMode suite 1165/1165 green.

---
*Phase: 11-first-run-onboarding-flow*
*Completed: 2026-07-18*
