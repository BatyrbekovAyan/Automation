---
phase: 11-first-run-onboarding-flow
plan: 03
subsystem: ui
tags: [onboarding, carousel, scene-builder, editor-tooling, playerprefs, unity]

# Dependency graph
requires:
  - phase: 11-first-run-onboarding-flow (plan 01)
    provides: OnboardingGate predicates + OnboardingKeys.Seen + internal Onboarding-aware NavRestructureBuilder.ReorderScreens
  - phase: 11-first-run-onboarding-flow (plan 02)
    provides: OnboardingPager (ScrollRect snap pager) + OnboardingScreen (dot binding + slide-3 CTA hand-off)
  - phase: existing
    provides: BotsPage.RefreshEmptyState zero-bot chokepoint, Manager.LoadBots, NavRestructureBuilder helper idioms
provides:
  - "Screen_Onboarding in Main.unity: 3-slide welcome carousel (verbatim RU copy deck, hero comps, dot pills, thumb-zone CTAs, no skip affordance) at the invariant ScreenContainer slot"
  - "OnboardingScreenBuilder: idempotent [MenuItem]+headless editor builder that rebuilds the screen, stamps OnboardingScreen/OnboardingPager/BotsPage refs, and reorders screens"
  - "First-run gate live at BotsPage.RefreshEmptyState (carousel instead of AddBotPanel auto-open on true first run)"
  - "Existing-user auto-flag at end of Manager.LoadBots keyed to live BotsParent.transform.childCount"
affects: [onboarding-success-moment, first-steps-card, first-run-device-uat]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Scene-mutating builder run through the OPEN Editor (orchestrator mcp-unity execute_menu_item + save_scene) with the scene committed immediately in its own commit"
    - "Sibling-order verification via the ScreenContainer transform's m_Children fileID array (flat YAML name order is NOT sibling order)"
    - "Scene copy verification via \\uXXXX-escaped search with YAML line-fold normalization (Unity escapes all non-ASCII in scene YAML)"

key-files:
  created:
    - Assets/Editor/OnboardingScreenBuilder.cs
  modified:
    - Assets/Scripts/Main/BotsPage.cs
    - Assets/Scripts/Main/Manager.cs
    - Assets/Scenes/Main.unity
    - Assets/Images/Icons/WhatsApp.svg.png.meta
    - Assets/Images/Icons/Telegram_2019_Logo.svg.png.meta
    - "Assets/Images/Icons/[CITYPNG.COM]HD Green Check True Tick Mark Icon Sign PNG - 3000x3000.png.meta"

key-decisions:
  - "Auto-flag reads the LIVE BotsParent.transform.childCount, never the monotonic id counter — a create-then-delete-all user keeps carousel eligibility (ONB-01)"
  - "Scene committed alone immediately after the builder run (parallel-scene-clobber rule) — environment constraint overrode the plan's single five-file commit"
  - "Icon Multiple→Single import flips audited against the 05-12 sub-sprite gotcha: internalIDToNameTable retains the legacy fileIDs, so all pre-existing refs (BotSwitcherRow prefab, Add-Bot form) keep resolving — no migration needed"

patterns-established:
  - "«Далее» slide CTAs wired as persistent int listeners (UnityEventTools.AddIntPersistentListener → pager.GoToPage) so slide nav survives serialization with zero runtime wiring"

requirements-completed: [ONB-01, ONB-05]

# Metrics
duration: ~27 min
completed: 2026-07-18
---

# Phase 11 Plan 03: Screen_Onboarding Scene Build + First-Run Gate Summary

**The 3-slide first-run welcome carousel is live in Main.unity — built by the new idempotent `OnboardingScreenBuilder` (verbatim RU copy deck, mini-chat/mode-cards/channel-cards heroes, dot pills, thumb-zone CTAs, no skip), gated at `BotsPage.RefreshEmptyState` so only a true first run (no bots, `OnboardingSeen` unset) sees it, with existing users auto-flagged at `Manager.LoadBots` from the live bot count — suite green at 1165/1165 both before and after the scene mutation.**

## Performance

- **Duration:** ~27 min (15:09:03Z → ~15:36Z, including one checkpoint round-trip)
- **Started:** 2026-07-18T15:09:03Z
- **Completed:** 2026-07-18T15:36:00Z
- **Tasks:** 3 (all auto; Task 3's builder execution went through a checkpoint resolved by the orchestrator's mcp-unity)
- **Files modified:** 8 (1 created + .meta, 6 modified)

## Accomplishments

- **Gate (Task 1):** `BotsPage.RefreshEmptyState` — the single zero-bot chokepoint (the Chats empty-state CTA also routes here) — now shows `Screen_Onboarding` when `OnboardingGate.ShouldShowCarousel(hasBots, seen)` passes, else falls back to the existing `StartNewBot()` auto-open. The `onboardingScreen` ref is null-guarded so a not-yet-built scene can never dead-end a new user (T-11-03-01).
- **Auto-flag (Task 1):** end of `Manager.LoadBots` flags `OnboardingKeys.Seen` for existing users via `OnboardingGate.ShouldAutoFlagSeen` keyed to the LIVE `BotsParent.transform.childCount` — never the monotonic `id` loop bound — so a create-then-fully-delete user stays carousel-eligible (T-11-03-02). Orphan-sweep logic untouched.
- **Builder (Task 2):** `OnboardingScreenBuilder` clones the `NavRestructureBuilder` envelope (font GUIDs, design tokens, verbatim helpers, deferred RoundedCorners bake, `DestroyAllByName` idempotency): 3 slides at 1080-wide offsets inside an `OnboardingPager` viewport (pageCount=3 stamped), per-slide heroes matching the mockup intent (mini chat mock with typing indicator; Авто-selected/Вместе mode cards; WhatsApp/Telegram cards with Image+sprite green checks), shared dot-pill row, per-slide thumb-zone CTA («Далее»×2 wired as persistent `GoToPage` listeners; «Создать бота» = `createBotButton`). Stamps `OnboardingScreen.pager/dots/createBotButton` + `BotsPage.onboardingScreen` via SerializedObject, then calls the internal Onboarding-aware `NavRestructureBuilder.ReorderScreens` (never `Build()`).
- **Scene (Task 3):** `Screen_Onboarding` built into `Main.unity` through the open Editor and committed immediately. Verified: ScreenContainer `m_Children` order = […, Screen_New(4), Screen_Onboarding(5), WhatsappAuth(6), TelegramAuth(7)] — auth stays last; both runtime component GUIDs present ×1; `BotsPage.onboardingScreen` stamped (non-zero fileID); full copy deck present (escaped-unicode + fold-normalized search) with zero «Пропустить»; screen starts inactive.
- **Zero regression (ONB-05):** EditMode suite 1165/1165 green on a fresh editor-assembly recompile after the code edits, and again after the scene save (data-only change — assembly stamp correctly unchanged).

## Task Commits

1. **Task 1: gate + existing-user auto-flag** - `d7d7306` (feat)
2. **Task 2: OnboardingScreenBuilder** - `19438d5` (feat)
3. **Task 3: Screen_Onboarding scene build** - `45af774` (feat — scene + icon metas, committed immediately after the builder run per the parallel-scene-clobber rule)

**Plan metadata:** committed separately with STATE/ROADMAP/REQUIREMENTS updates (docs).

## Files Created/Modified

- `Assets/Editor/OnboardingScreenBuilder.cs` - Idempotent [MenuItem "Tools/Onboarding/Build"] + `BuildHeadless` (exact sentinel `[OnboardingScreenBuilder] Headless build + save complete`) builder for the 3-slide carousel.
- `Assets/Scripts/Main/BotsPage.cs` - `onboardingScreen` serialized ref + carousel-or-StartNewBot gate in `RefreshEmptyState`.
- `Assets/Scripts/Main/Manager.cs` - Existing-user auto-flag at end of `LoadBots` (live childCount fact).
- `Assets/Scenes/Main.unity` - `Screen_Onboarding` (inactive) with pager/slides/dots/CTAs, all refs stamped, invariant sibling order.
- 3 icon `.meta` files - Builder import normalization (Sprite/Single, mipmaps off); legacy sub-sprite fileIDs retained via `internalIDToNameTable`.

## Decisions Made

- Auto-flag fact = live `BotsParent.transform.childCount` (the container `LoadBots` instantiates into), never the `id` counter — the plan's central correctness requirement, verified by acceptance greps.
- Scene + icon metas committed as one immediate commit, separate from the builder commit: the environment's scene-commit discipline (commit `Main.unity` alone with any `.meta` it needs, immediately) overrode the plan's literal single five-file commit instruction; both subjects carry `11-03`.
- Icon `Multiple→Single` flips accepted without reference migration after auditing the exact 05-12 gotcha: all three metas retain their legacy internal sprite IDs in `internalIDToNameTable`/sprite-sheet `internalID`, the configuration already proven on device for the Telegram logo since 05-12 (its prefab internal-ID ref and scene 21300000 refs coexist). New onboarding refs serialize as canonical `21300000`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Reconciled the plan-internal documentary-token contradiction (third occurrence in this phase)**
- **Found during:** Task 2
- **Issue:** The builder's XML-doc originally said "NO «Пропустить» affordance" while the same task's acceptance criterion requires no «Пропустить» string anywhere in the builder — mutually exclusive on the literal token (identical to the 11-01 `PlayerPrefs` and 11-02 `SnappyFlickScrollRect`/skip contradictions).
- **Fix:** Reworded to "no bypass affordance, the informative slides advance only via those two CTAs" — documentary intent preserved, token gone. No code/behavior change; there is provably no skip logic.
- **Files modified:** Assets/Editor/OnboardingScreenBuilder.cs
- **Verification:** `grep -c "Пропустить"` = 0 and `grep -ci "skip"` = 0 on the builder; scene grep confirms zero skip strings in the built screen.
- **Committed in:** `19438d5`

**2. [Environment override] Task 3 executed via the open Editor instead of the headless runner, and the commit was split**
- **Found during:** Task 3
- **Issue:** The plan's primary path (`Tools/run-editor-builder.sh`) requires a closed Editor; the Editor was open (PID 1327) and this executor session could not load mcp-unity or drive the Editor (ToolSearch disabled, osascript lacked assistive access) → checkpoint raised per the plan's own Editor-open fallback.
- **Resolution:** The orchestrator (with mcp-unity access) ran `execute_menu_item "Tools/Onboarding/Build"` + `save_scene`; both console sentinels confirmed. The scene was then committed alone+immediately (`45af774`) per the environment's scene-commit discipline rather than the plan's single five-file commit — code files had already been committed per-task (`d7d7306`, `19438d5`).
- **Impact:** None on outcome; all Task 3 acceptance criteria pass (Editor-open build path is explicitly sanctioned by the plan).

---

**Total deviations:** 1 auto-fixed contradiction + 1 environment-driven execution-path substitution. No scope change; all plan behavior delivered as written.

## Issues Encountered

- **Checkpoint round-trip for the scene mutation.** This session's toolset (no ToolSearch → no mcp-unity/computer-use; osascript blocked) had no autonomous way to execute a menu item in the open Editor; returned a `human-action` checkpoint which the coordinator resolved via the orchestrator's mcp-unity without owner action.
- **Scene copy greps initially returned 0** for all RU strings — Unity serializes every non-ASCII character in scene YAML as `\uXXXX` escapes AND folds long double-quoted scalars across lines. Verification required escaping the probe strings and normalizing the folds; after that the full copy deck matched exactly (and pre-existing strings like «Сводка» confirmed the encoding hypothesis).
- **Icon meta churn audited, not blindly committed.** The builder's `EnsureIconImportSettings` flipped WhatsApp + check icons Multiple→Single (the exact 05-12 breakage pattern). Audit showed `internalIDToNameTable` retained every legacy sub-sprite fileID, so the pre-existing `BotSwitcherRow.prefab` and Add-Bot form references still resolve — no heal required, metas committed with the scene they support.

## Known Stubs

None — the screen is fully built and wired (pager, dots, CTAs, gate, auto-flag); no placeholder copy, no unwired refs. Visual polish verification on device/Game view is the phase's normal UAT tail, not a stub.

## Threat Model Compliance

All four `mitigate` dispositions applied: T-11-03-01 (null-guarded gate falls back to `StartNewBot`), T-11-03-02 (live childCount, `id` never used as the hasBots fact), T-11-03-03 (ReorderScreens invariant verified via `m_Children` fileID order — auth last), T-11-03-04 (scene committed immediately in the same task as the builder run). No new threat surface (no network/auth/schema changes; `OnboardingSeen` is an app-written 0/1 int — T-11-03-05 accepted).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- ONB-01 is code+scene complete: first launch shows the carousel; slide-3 «Создать бота» latches `OnboardingSeen` and opens the existing wizard; existing users are auto-flagged and never see it.
- Downstream plans can now build on the live screen: the success moment (Plan 04, Manager edits B+C) and trust blocks / first-steps card (Plans 05+) per 11-PATTERNS.md.
- Device/Game-view visual pass (carousel paging feel, hero rendering at 1080×2400) rides the phase's UAT gate — the builder is idempotent, so any visual calibration is a re-run away.

## Self-Check: PASSED

- All key files present on disk (builder + .meta, gate/auto-flag edits, scene with `Screen_Onboarding`).
- All 3 task commits present in git history (`d7d7306`, `19438d5`, `45af774`).
- All task acceptance criteria re-run and PASS; EditMode suite 1165/1165 green after both the code compile and the scene save; sibling-order invariant verified via the transform `m_Children` array.

---
*Phase: 11-first-run-onboarding-flow*
*Completed: 2026-07-18*
