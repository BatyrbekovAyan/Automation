---
phase: 11-first-run-onboarding-flow
plan: 09
subsystem: ui
tags: [onboarding, first-steps-checklist, canvasgroup, visibility-gate, live-refresh, d1-d3-gap-closure, unity]

# Dependency graph
requires:
  - phase: 11-first-run-onboarding-flow (plan 06)
    provides: FirstStepsCard MonoBehaviour (private Refresh deriving live facts, ReserveListPadding/RestoreListPadding, 4/4 permanent-hide latch) + pure FirstStepsChecklist.StepStates/AllDone/ChannelLabel
  - phase: 11-first-run-onboarding-flow (plan 08)
    provides: ShowInteractiveSuccessMoment(Bot) signature + arg-free CloseSuccessAndOverlay + the two fire sites (CreateBotFromForm, ShowAuthSuccess else branch) — this plan's hooks anchor on the post-11-08 method bodies
provides:
  - "Pure FirstStepsCardVisibility.ShouldShow(hasBots, checklistDone) => hasBots && !checklistDone — the D1 zero-bot rule + permanent 4/4 hide, EditMode-testable (4 tests)"
  - "FirstStepsCard is externally refreshable: static Instance + cached CanvasGroup (Awake), public RefreshFromFacts(), visibility gated on ShouldShow via a CanvasGroup toggle (root never self-deactivates)"
  - "Five fire-and-forget FirstStepsCard.Instance?.RefreshFromFacts() hooks — BotsPage.RefreshEmptyState (D1 authority), Manager.CreateBotFromForm/ShowAuthSuccess-else/CloseAddBotForm, BotSettings.Auth upload — making the card a live mirror of onboarding progress"
affects: [first-run-device-uat, onboarding-gap-round-2, 11-10-re-verify]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Externally-refreshable card: static Instance + public RefreshFromFacts() entry called at every fact-changing moment, so a card that lives on a still-active screen (the AddBotPanel is an OVERLAY, OnEnable never re-fires after bot creation) is never stale between OnEnable ticks"
    - "Hide via CanvasGroup (alpha 0 + blocksRaycasts/interactable false), NEVER self-SetActive(false) on the root — a self-deactivated root can never be re-shown by an external hook; the root stays active so Instance/RefreshFromFacts always resolve"
    - "Pure boolean visibility predicate (FirstStepsCardVisibility) that both the zero-bot D1 hide and the permanent 4/4 hide collapse into a single ShouldShow gate — mirrors OnboardingGate / FirstStepsChecklist"

key-files:
  created:
    - Assets/Scripts/Main/Onboarding/FirstStepsCardVisibility.cs
    - Assets/Tests/Editor/Chat/FirstStepsCardVisibilityTests.cs
    - .planning/phases/11-first-run-onboarding-flow/11-09-SUMMARY.md
  modified:
    - Assets/Scripts/Main/Onboarding/FirstStepsCard.cs
    - Assets/Scripts/Main/BotsPage.cs
    - Assets/Scripts/Main/Manager.cs
    - Assets/Scripts/Main/BotSettings.Auth.cs

key-decisions:
  - "Both D1 (zero bots) and the permanent 4/4 completion collapse into ONE ShouldShow gate at the top of Refresh — the two former hide reasons (the top ChecklistDone block + the bottom AllDone latch) now hide through the same CanvasGroup path, so there is a single source of truth for visibility"
  - "The card root stays ACTIVE and is hidden via CanvasGroup — a self-SetActive(false) root could never be re-shown by an external RefreshFromFacts hook, which is exactly what D1/D3 require (the card must reappear once ≥1 bot exists / a fact changes)"
  - "Awake (not Start) sets Instance + caches the CanvasGroup — the card root is already active in the scene, so Awake runs on scene load and Instance is resolvable before the first fact-change hook fires"

patterns-established:
  - "Live-mirror UI card: pure visibility predicate + static Instance + public RefreshFromFacts() + CanvasGroup hide, refreshed from every fact-changing call site rather than relying on OnEnable"

requirements-completed: [ONB-04, ONB-05]

# Metrics
duration: ~14 min
completed: 2026-07-23
---

# Phase 11 Plan 09: D1+D3 «Первые шаги» Live-Fact Checklist Summary

**The «Первые шаги» checklist card became a live mirror of onboarding progress: a new pure `FirstStepsCardVisibility.ShouldShow(hasBots, checklistDone)` gate hides the card whenever no bots exist (D1 — the EmptyState owns the zero-bot screen, no overlap) and forever after 4/4; `FirstStepsCard` now exposes a static `Instance` + public `RefreshFromFacts()` and hides via a CanvasGroup (root stays active, never self-deactivates); and five fire-and-forget hooks (BotsPage zero-bot chokepoint, bot-created, late channel-auth, wizard back-out, price-list upload) refresh it on every fact change (D3 — no more all-unchecked-until-navigate-away), with the WhatsApp/Telegram auth flows byte-identical and the EditMode suite green at 1209/1209.**

## Performance

- **Duration:** ~14 min
- **Started:** 2026-07-23T11:18:09Z
- **Completed:** 2026-07-23T11:32:05Z
- **Tasks:** 3 (Task 1 was TDD → RED + GREEN commits)
- **Files modified:** 6 .cs (2 created, 4 modified) + 2 paired .meta

## Accomplishments

- **Task 1 — pure D1 rule + tests (TDD):** New `FirstStepsCardVisibility` static class in the runtime assembly, global namespace, booleans-only: `ShouldShow(hasBots, checklistDone) => hasBots && !checklistDone`. Zero bots ⇒ the EmptyState is the step-1 guidance (card hidden, D1); the permanent 4/4 latch ⇒ hidden forever. Four `FirstStepsCardVisibilityTests` (NUnit, no asmdef) cover all four `hasBots × checklistDone` cases with message-bearing asserts. Real RED (3/4, the mid-onboarding case failed against a `=> false` stub) → GREEN (4/4) verified via the in-Editor ClaudeTestBridge.
- **Task 2 — externally-refreshable card:** `FirstStepsCard` gained `public static FirstStepsCard Instance;` + a cached `CanvasGroup _cg` set in `Awake` (adds one if the scene lacks it — no scene change needed), a `public void RefreshFromFacts() => Refresh();` entry, and a single visibility gate at the top of `Refresh()` on `FirstStepsCardVisibility.ShouldShow(botExists, checklistDone)`. Hiding moved from `gameObject.SetActive(false)` (twice) to a new `SetContentVisible(bool)` helper (alpha + blocksRaycasts + interactable), so the root stays active and Instance/RefreshFromFacts always resolve. The 4/4 latch, first-reply latch, row cascade, deep-links, and reversible list-padding are all intact.
- **Task 3 — five fact-change hooks:** Added `FirstStepsCard.Instance?.RefreshFromFacts();` (fire-and-forget, null-guarded) at the five verified sites — `BotsPage.RefreshEmptyState` (the single zero-bot chokepoint = D1 authority + return-to-Bots refresh), `Manager.CreateBotFromForm` (refresh under the success overlay so the card is already at the right «N из 4»), `Manager.ShowAuthSuccess` else branch (late channel auth flips the connect row), `Manager.CloseAddBotForm` (belt-and-suspenders D1 back-out hide), and `BotSettings.Auth.cs` after the `UploadedFilesStore.Add` + `CompletePendingFileRow` (price-list upload flips row 3). The first-reply latch already refreshes via `LatchIfReplySeen → Refresh()`.
- **Zero regression + auth byte-identical:** EditMode suite 1209/1209 green (1205 baseline + 4 new) after every task on fresh `Assembly-CSharp.dll` recompiles. `GetChild(3/4/5)` stays at 21 and `auth/code|auth/2fa` at 7 in Manager.cs — the hooks are additive single lines placed OUTSIDE any auth/GetChild code. No scene mutation (no Main.unity touched).

## Task Commits

Each task was committed atomically:

1. **Task 1 (RED): failing FirstStepsCardVisibility tests** - `18abd22` (test)
2. **Task 1 (GREEN): FirstStepsCardVisibility.ShouldShow pure D1 rule** - `b0d0d7f` (feat)
3. **Task 2: FirstStepsCard Instance + CanvasGroup hide + ShouldShow gate + RefreshFromFacts** - `559b89d` (feat)
4. **Task 3: five fact-change RefreshFromFacts hooks** - `c2b996c` (feat)

**Plan metadata:** committed separately with STATE/ROADMAP updates (docs).

## Files Created/Modified

- `Assets/Scripts/Main/Onboarding/FirstStepsCardVisibility.cs` - Pure `ShouldShow(hasBots, checklistDone)` gate (global namespace, runtime assembly, booleans-only).
- `Assets/Tests/Editor/Chat/FirstStepsCardVisibilityTests.cs` - 4 NUnit tests for the four visibility cases.
- `Assets/Scripts/Main/Onboarding/FirstStepsCard.cs` - Static `Instance` + cached CanvasGroup (Awake); public `RefreshFromFacts()`; `Refresh()` gated on `ShouldShow`; `SetContentVisible` helper replaces both root `SetActive(false)` calls (root never self-deactivates).
- `Assets/Scripts/Main/BotsPage.cs` - `RefreshEmptyState` drives `RefreshFromFacts()` at the zero-bot chokepoint (D1 authority + return-to-Bots refresh).
- `Assets/Scripts/Main/Manager.cs` - Three hooks: `CreateBotFromForm` (bot created), `ShowAuthSuccess` else branch (late channel auth), `CloseAddBotForm` (wizard back-out). Auth code paths untouched.
- `Assets/Scripts/Main/BotSettings.Auth.cs` - Hook after the price-list `UploadedFilesStore.Add` / `CompletePendingFileRow`.

## Decisions Made

- **One ShouldShow gate for both hide reasons:** the former top-of-Refresh `ChecklistDone` permanent-hide block and the bottom `AllDone` latch now both hide through the same CanvasGroup path; the top gate is the single source of truth for visibility (covers zero-bot D1 and permanent 4/4).
- **CanvasGroup hide, active root:** the root stays active so an external `RefreshFromFacts` can always re-show the card once ≥1 bot exists / a fact changes — a self-`SetActive(false)` root could never be re-shown, which would defeat D1/D3.
- **Instance set in Awake:** the card root is active in the scene, so Awake runs on scene load and Instance resolves before the first fact-change hook fires (fire-and-forget hooks are `?.`-guarded regardless).

## Deviations from Plan

None - plan executed exactly as written. All acceptance greps pass (ShouldShow==1, [Test]==4, Instance==1, RefreshFromFacts==1, ShouldShow-gate==1, SetContentVisible==4, gameObject.SetActive(false)==0; hook counts BotsPage 1 / Manager 3 / BotSettings.Auth 1; auth GetChild==21 and auth/code|auth/2fa==7 unchanged), and there was no scene change (files_modified carries no Main.unity).

## Issues Encountered

- **Chrome repeatedly stole frontmost focus from the open Editor, throttling its update loop** (the ClaudeTestBridge only ticks fast while Unity is focused). Resolved by re-issuing `open -a Unity` before each poll cycle; every run still consumed its armed trigger and produced a fresh result. Freshness was verified per-edit: runtime-only edits (all of Task 2/3 + the GREEN body) gated on the `Assembly-CSharp.dll` mtime advancing (`editorAssemblyWrittenUtc` correctly stays put for runtime-only changes since the editor assembly is unchanged); the new test file (Task 1 RED) advanced `editorAssemblyWrittenUtc` to 2026-07-23T11:22:54Z. New-file `.meta` pairs were confirmed present after the bridge's `AssetDatabase.Refresh` and the new test class ran (executed count 1205 → 1209).
- **Suite baseline is 1205, not the plan's 1165/1169** (v1.2 tests landed since Phase 11 first executed). The gate is 0 failures + the count rising by exactly the 4 new tests → 1209; it held on every run.

## Threat Model Compliance

All `mitigate` dispositions applied: **T-11-09-01** (DoS on a null/destroyed card) — every hook is `FirstStepsCard.Instance?.RefreshFromFacts()` (null-conditional) and `Refresh`/`SetContentVisible` null-guard `botsParent`/`_cg`; **T-11-09-02** (a hook alters an auth path) — hooks are additive single lines OUTSIDE all `GetChild`/auth-request code, proven by `GetChild(3/4/5)`==21 and `auth/code|auth/2fa`==7 unchanged. The **accept** disposition **T-11-09-03** (card resurrects after 4/4) holds by construction — `ShouldShow` returns false whenever `ChecklistDone` is latched, and that PlayerPref is monotonic (never un-set). No new threat surface — client-only UI; no network/auth/schema change.

## Known Stubs

None — `FirstStepsCardVisibility.ShouldShow` returns a computed boolean, the card derives all state live from existing PlayerPrefs / `UploadedFilesStore` facts, and the five hooks are single fire-and-forget refresh lines. No hardcoded empty values, no placeholder copy.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- **D1 closed (code):** with zero bots the ShouldShow gate hides the card so only the EmptyState renders (no overlap on the carousel → wizard → back repro); the card first appears once ≥1 bot exists.
- **D3 closed (code):** immediately after bot creation the card shows the correct «N из 4» with rows 1-2 checked — no navigate-away-and-back needed; it re-derives on bot-created / channel-authed / wizard back-out / price-list upload / first-reply / return-to-Bots.
- **Preserved invariants:** the permanent 4/4 hide still latches and never resurrects; WhatsApp/Telegram auth flows are byte-identical (this plan added only fire-and-forget RefreshFromFacts calls); suite green at 1209/1209.
- **Device verdict rides 11-10** (Round-2 re-verify): the visual/interaction confirmation of D1 (no overlap) and D3 (correct checks with no navigation) on a real device is the owner gate, alongside the D2 standalone overlay from 11-08.

## Self-Check: PASSED

- Both created files present on disk with paired `.meta`; both modified runtime files carry the hooks.
- All four task commits present in git history (`18abd22`, `b0d0d7f`, `559b89d`, `c2b996c`); no file deletions across the plan's commit range.
- All acceptance greps re-run and pass; EditMode suite 1209/1209 green on fresh `Assembly-CSharp.dll` recompiles.

---
*Phase: 11-first-run-onboarding-flow*
*Completed: 2026-07-23*
