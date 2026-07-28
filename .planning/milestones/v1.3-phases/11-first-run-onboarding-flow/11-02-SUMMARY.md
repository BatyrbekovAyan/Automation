---
phase: 11-first-run-onboarding-flow
plan: 02
subsystem: ui
tags: [onboarding, carousel, scrollrect, dotween, pager, unity, monobehaviour]

# Dependency graph
requires:
  - phase: 11-first-run-onboarding-flow (plan 01)
    provides: OnboardingPageMath (NearestPage / PageToNormalizedX) + OnboardingKeys.Seen constant
  - phase: existing
    provides: BotsPage.Instance.StartNewBot (→ AddBotPanel.Instance.Open), ScrollRect/DOTween
provides:
  - "OnboardingPager: horizontal snap pager (ScrollRect subclass) — end-drag settles on the nearest of 3 pages with a 0.3s OutCubic tween and raises OnPageChanged"
  - "OnboardingScreen: carousel controller — binds pager page changes to the dot pills and hands the slide-3 «Создать бота» CTA off to the existing AddBotPanel wizard after latching OnboardingSeen"
affects: [onboarding-screen-builder, onboarding-carousel-scene, first-run-device-uat]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Horizontal snap-pager as a ScrollRect subclass driving snap through the unit-tested OnboardingPageMath (net-new UI mechanism; NOT the vertical flick-momentum subclass)"
    - "Thin event-driven carousel controller: [SerializeField] private refs are the builder↔component contract; subscribe/unsubscribe OnPageChanged in OnEnable/OnDisable"

key-files:
  created:
    - Assets/Scripts/Main/Onboarding/OnboardingPager.cs
    - Assets/Scripts/Main/Onboarding/OnboardingScreen.cs
  modified: []

key-decisions:
  - "Kept OnboardingPager a ScrollRect subclass (not a plain MonoBehaviour) so the Plan-03 builder can add it as the scroll component of the slide viewport"
  - "Snap logic delegates entirely to Plan-01's OnboardingPageMath — zero paging arithmetic duplicated into the MonoBehaviour"
  - "CTA sets OnboardingSeen before hiding the screen and calls BotsPage.Instance?.StartNewBot() so the user is never trapped on the carousel even if the singleton is momentarily null"

patterns-established:
  - "Net-new horizontal pager isolated from the vertical flick-momentum ScrollRect subclass; snap owns the motion (horizontal-only, Clamped, inertia off)"

requirements-completed: [ONB-01]

# Metrics
duration: 8 min
completed: 2026-07-18
---

# Phase 11 Plan 02: Onboarding Carousel Runtime (Pager + Screen Controller) Summary

**Two runtime MonoBehaviours that drive the 3-slide welcome carousel: `OnboardingPager` (a horizontal snap ScrollRect subclass that settles on the nearest page via the Plan-01 `OnboardingPageMath` with a 0.3s OutCubic tween and raises `OnPageChanged`) and `OnboardingScreen` (dot binding + the slide-3 «Создать бота» hand-off into the existing `AddBotPanel` wizard after latching `OnboardingSeen`), both compiling cleanly with the EditMode suite unchanged at 1165/1165.**

## Performance

- **Duration:** 8 min
- **Started:** 2026-07-18T14:54:06Z
- **Completed:** 2026-07-18T15:02:03Z
- **Tasks:** 2 (both auto)
- **Files modified:** 2 (2 created, 0 modified)

## Accomplishments
- `OnboardingPager.cs` — `ScrollRect` subclass implementing `IEndDragHandler` snap-to-page: `Awake` locks horizontal-only / `MovementType.Clamped` / no inertia; `OnEndDrag` snaps to `OnboardingPageMath.NearestPage(horizontalNormalizedPosition, pageCount)` with a `DOTween.To(...0.3f).SetEase(Ease.OutCubic)`; raises `OnPageChanged` on settle; `GoToPage` for programmatic nav; tween killed on `OnDisable`.
- `OnboardingScreen.cs` — thin controller: subscribes/unsubscribes `pager.OnPageChanged` in `OnEnable`/`OnDisable`, elongates+tints the active dot pill (#1B7CEB), and the slide-3 CTA latches `OnboardingKeys.Seen`, hides `Screen_Onboarding`, and calls `BotsPage.Instance?.StartNewBot()`.
- Bound to Plan-01 seams only (`OnboardingPageMath`, `OnboardingKeys.Seen`) plus existing singletons (`BotsPage`/`AddBotPanel`) — no logic duplicated into the MonoBehaviours; no scene mutation, no `Manager.cs`/`BotsPage.cs` edits (as scoped).
- Full EditMode suite green at **1165/1165** (baseline unchanged — these are MonoBehaviours with no new pure logic), verified via the in-Editor `ClaudeTestBridge` after a confirmed `Assembly-CSharp.dll` recompile that imported both new files.

## Task Commits

Each task was committed atomically:

1. **Task 1: OnboardingPager — horizontal snap pager + OnPageChanged** - `14696af` (feat)
2. **Task 2: OnboardingScreen — dot binding + slide-3 CTA hand-off** - `aa697e1` (feat)

**Plan metadata:** committed separately with STATE/ROADMAP/REQUIREMENTS updates (docs).

## Files Created/Modified
- `Assets/Scripts/Main/Onboarding/OnboardingPager.cs` - Horizontal snap pager (ScrollRect subclass); end-drag → nearest page via OnboardingPageMath, 0.3s OutCubic, `OnPageChanged` event.
- `Assets/Scripts/Main/Onboarding/OnboardingScreen.cs` - Carousel controller: dot binding to the pager + slide-3 «Создать бота» CTA that latches `OnboardingSeen` and opens the existing wizard.

## Decisions Made
- Kept `OnboardingPager` a `ScrollRect` subclass rather than a plain MonoBehaviour so the Plan-03 builder can drop it in as the slide viewport's scroll component (the field names `pageCount`/`OnPageChanged` are the builder↔component contract).
- Snap arithmetic lives entirely in Plan-01's unit-tested `OnboardingPageMath` — the MonoBehaviour only reads/writes `horizontalNormalizedPosition` and tweens; no paging math duplicated.
- The CTA sets `OnboardingSeen` and hides the screen *before* the singleton hand-off, so a null `BotsPage.Instance` still leaves the flag latched (user never re-traps on the carousel — threat T-11-02-01 mitigation).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Reconciled two plan-internal contradictions between the provided verbatim code and its own acceptance criteria (documentary comment tokens)**
- **Found during:** Task 1 (OnboardingPager) and Task 2 (OnboardingScreen)
- **Issue:** The plan's verbatim code blocks contained two documentary XML-doc phrases — "NOT SnappyFlickScrollRect …" in `OnboardingPager` and "No skip affordance …" in `OnboardingScreen` — while the same tasks' acceptance criteria require `grep` for `SnappyFlickScrollRect` (Task 1) and for `skip`/`Пропустить` (Task 2) to **return nothing**. The two directives are mutually exclusive on the literal token (identical pattern to the Plan-01 `PlayerPrefs`-token contradiction, resolved the same way).
- **Fix:** Reworded both comments to drop the literal tokens while fully preserving their documentary intent — "Deliberately NOT the existing vertical flick-momentum ScrollRect subclass, which has no paging (RESEARCH Pitfall 1)" and "No bypass affordance (CONTEXT: informative slides advance only via «Далее»/«Создать бота»)". No code/behavior change; there is provably no code dependency on `SnappyFlickScrollRect` and no skip/bypass logic.
- **Files modified:** Assets/Scripts/Main/Onboarding/OnboardingPager.cs, Assets/Scripts/Main/Onboarding/OnboardingScreen.cs
- **Verification:** `grep -i "SnappyFlickScrollRect"` on the pager and `grep -iE "skip|Пропустить"` on the screen both return nothing; every other acceptance grep passes; suite 1165/1165 green.
- **Committed in:** `14696af` (Task 1) and `aa697e1` (Task 2)

---

**Total deviations:** 1 auto-fixed (1 bug — plan-internal contradiction reconciliation, spanning both files).
**Impact on plan:** No scope change and no behavior change; the resolution honors both the acceptance-grep purity checks and the documentary intent of the comments. All plan behavior delivered as written.

## Issues Encountered
- **Environment: Editor was OPEN (as the corrected execution context stated).** Used the in-Editor `ClaudeTestBridge` (drop empty `Temp/claude/run-tests.trigger`, `open -a Unity` to fire the focus-gated poll, read `test-summary.json`) — `Tools/run-tests-headless.sh` was correctly avoided (it refuses while the project lock is held).
- **Freshness gate for runtime-only edits.** Both new files compile into `Assembly-CSharp.dll` (not the editor assembly), so the bridge's `editorAssemblyWrittenUtc` stamp does not move for these edits (per project memory). Confirmed the recompile instead via `Assembly-CSharp.dll` mtime advancing to 19:59:14Z (newer than both file writes) and both `.meta` siblings being generated — proving Unity imported and compiled the new files (a compile error would have produced a `CompilationFailed` bridge status, not a green 1165).

## Known Stubs
None — both files are complete runtime components bound to real Plan-01 seams and existing singletons. Dot visuals default to a code-driven elongate/tint (`SetDotActive`); the Plan-03 builder may override this with baked wide/narrow sprites via the wired refs, which is expected scene work, not a stub.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- The two carousel MonoBehaviours are ready for Plan 03's `OnboardingScreenBuilder` to instantiate `Screen_Onboarding`, add `OnboardingPager` as the slide viewport's scroll component (stamp `pageCount=3`), build the 3 slides + dot pills, and stamp `OnboardingScreen`'s `[SerializeField]` refs (`pager`, `dots`, `createBotButton`) via `SerializedObject`.
- The slide-3 gate hand-off (`OnboardingSeen` latch → `BotsPage.StartNewBot`) is wired; the reciprocal gate insertion in `BotsPage.RefreshEmptyState`/`Manager.LoadBots` remains later-plan work (scoped out here).
- No scene mutation and no `Manager.cs`/`BotsPage.cs`/`Bot.cs` edits occurred in this plan.

## Self-Check: PASSED
- Both created files present on disk with `.meta` siblings (verified via `ls`).
- Both task commits present in git history (`14696af`, `aa697e1`).
- All task acceptance greps re-run and PASS; full EditMode suite 1165/1165 green via the in-Editor bridge against a freshly recompiled `Assembly-CSharp.dll`.

---
*Phase: 11-first-run-onboarding-flow*
*Completed: 2026-07-18*
