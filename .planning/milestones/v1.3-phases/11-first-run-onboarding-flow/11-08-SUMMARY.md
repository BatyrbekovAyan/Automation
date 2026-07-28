---
phase: 11-first-run-onboarding-flow
plan: 08
subsystem: ui
tags: [onboarding, success-moment, standalone-overlay, auth, unity, manager, scene-builder, d2-gap-closure]

# Dependency graph
requires:
  - phase: 11-first-run-onboarding-flow (plan 01)
    provides: SuccessCtaSelector.Choose + SuccessCta enum, Bot.OpenSettingsAtProductTab deep-link
  - phase: 11-first-run-onboarding-flow (plan 04)
    provides: ShowInteractiveSuccessMoment / CloseSuccessAndOverlay + the two fire sites (CreateBotFromForm, ShowAuthSuccess else branch) — relocated here
  - phase: 11-first-run-onboarding-flow (plan 05)
    provides: OnboardingAuthBlocksBuilder envelope (helpers/tokens/font GUIDs/DestroyAllByName), SuccessCheckPop, trust cards, the two nested SuccessCta clusters — torn down here
provides:
  - "Standalone full-screen SuccessOverlay (Canvas-level, last sibling → renders ABOVE the auth pages) hosting ONE «Загрузить прайс-лист»/«Позже» celebration"
  - "ONE Manager success field set (SuccessOverlay + successTitleLabel/Body/PrimaryButton/PrimaryLabel/LaterButton) replacing the ten per-channel waSuccess*/tgSuccess* fields"
  - "ShowInteractiveSuccessMoment(Bot) — channel-agnostic, no authPage reactivation; deactivates both auth hierarchies up front; parameterless CloseSuccessAndOverlay"
  - "OnboardingAuthBlocksBuilder.BuildStandaloneOverlay — idempotent direct-child teardown + one Canvas-level overlay build + 6-field Manager re-stamp; trust cards untouched"
affects: [first-run-device-uat, onboarding-gap-round-2]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Full-screen celebration overlay relocated OUT of index-addressed auth panels into a Canvas-level sibling of the ScreenContainer (SetAsLastSibling), so a scene change never shifts the auth GetChild(3/4/5) indices"
    - "Idempotent DIRECT-CHILD-ONLY teardown when the target name collides with unrelated descendants (two nested per-channel panels are also named SuccessOverlay — a recursive DestroyAllByName would wipe them)"
    - "Single shared success field set for a single shared overlay hierarchy — retires the per-channel wa*/tg* duplication that only existed because the old panels lived in separate hierarchies"

key-files:
  created:
    - .planning/phases/11-first-run-onboarding-flow/11-08-SUMMARY.md
  modified:
    - Assets/Scripts/Main/Manager.cs
    - Assets/Editor/OnboardingAuthBlocksBuilder.cs
    - Assets/Scenes/Main.unity

key-decisions:
  - "GetComponentInParent<Canvas>(true).rootCanvas (include-inactive + root walk) to GUARANTEE the overlay parents to the ROOT Canvas (fileID 42635013), not a nested canvas or null from an inactive code panel — the plan snippet's bare GetComponentInParent<Canvas>() had neither guard"
  - "Idempotent teardown of the standalone overlay iterates canvas DIRECT children only — a recursive DestroyAllByName(canvas, \"SuccessOverlay\") would have deleted the two nested per-channel panels (WhatsappAuthSuccessPanel/TelegramAuthSuccessPanel), which are ALSO named SuccessOverlay and must survive (the moreAuthSteps 2s transient still uses one)"
  - "No AddRounded on the full-screen backdrop (it fills the whole screen) — net -5 RoundedCorners vs the two old sheets (8 → 3), which is exactly what the scene diff shows"

patterns-established:
  - "Relocating a celebration/modal above index-addressed panels: build it as a Canvas-level last sibling, deactivate the source hierarchies up front (no host reactivation), and prove m_Father == the root Canvas RectTransform"

requirements-completed: [ONB-03]

# Metrics
duration: ~26 min
completed: 2026-07-23
---

# Phase 11 Plan 08: D2 Standalone Success Overlay Summary

**The interactive «Бот подключён!» moment is relocated out of the auth screens onto a NEW standalone full-screen `SuccessOverlay` (a Canvas-level last sibling that renders ABOVE the auth pages) — the ten per-channel `waSuccess*`/`tgSuccess*` fields collapse to ONE set, `ShowInteractiveSuccessMoment` drops `useTelegram` and the `authPage.SetActive(true)` D2 hack, the builder tears down both nested `SuccessCta` clusters and builds one Canvas-level overlay (parented to the root Canvas RectTransform 42635013), and the WhatsApp/Telegram auth code flows stay byte-identical with the EditMode suite green at 1205/1205.**

## Performance

- **Duration:** ~26 min
- **Started:** 2026-07-23T10:40:42Z
- **Completed:** 2026-07-23T11:06:56Z
- **Tasks:** 3 (all auto; Task 3's builder run went through an orchestrator mcp-unity checkpoint)
- **Files modified:** 3 (Manager.cs, OnboardingAuthBlocksBuilder.cs, Main.unity)

## Accomplishments

- **Task 1 (Manager.cs):** Replaced the ten per-channel `waSuccess*`/`tgSuccess*` `[SerializeField]`s with ONE set (`SuccessOverlay` + `successTitleLabel`/`Body`/`PrimaryButton`/`PrimaryLabel`/`LaterButton`). Rewrote `ShowInteractiveSuccessMoment` to `ShowInteractiveSuccessMoment(Bot)` — no `useTelegram`, no `authPage.SetActive(true)`; it deactivates BOTH auth hierarchies up front so nothing of the code UI shows beneath the overlay, keeps the files-exist CTA (`SuccessCtaSelector.Choose`) + verbatim copy deck + wait-on-`dismissed` loop, and shows `SuccessOverlay`. Made `CloseSuccessAndOverlay` parameterless (the authPage-deferral hack died). Dropped the second arg from both call sites (`CreateBotFromForm`, `ShowAuthSuccess` else branch). Left the `moreAuthSteps` 2s transient and all auth `GetChild(3/4/5)` untouched.
- **Task 2 (OnboardingAuthBlocksBuilder.cs):** Added idempotent `DestroyAllByName(waSuccessPanel.transform, "SuccessCta")` / `tgSuccessPanel` teardown (panels stay — the transient still uses `WhatsappAuthSuccessPanel`), replaced the two `BuildSuccessSheet` calls + the 10-field stamp with a single `BuildStandaloneOverlay(Canvas, out GameObject)` that builds one Canvas-level overlay (backdrop + animated check + title + body + full-width Primary CTA + ghost «Позже») and a 6-field Manager re-stamp. Trust cards + the headless sentinel are byte-identical.
- **Task 3 (Main.unity):** Orchestrator ran `Tools/Onboarding/Build Auth Blocks` via mcp-unity (both descriptive sentinels, zero console errors) + saved the scene; committed alone+immediately per the parallel-scene-clobber rule. Verified: `m_Name: SuccessOverlay` 2→3, the NEW overlay's Transform `m_Father` == root Canvas RectTransform **42635013** (and it is the LAST Canvas child → renders above the ScreenContainer), `m_Name: SuccessCta` 2→0, all 6 Manager refs stamped non-zero, `TrustBlock` still the last child of both code panels (10 children each, `GetChild(3/4/5)` targets intact).
- **Zero regression (ONB-05 slice):** EditMode suite 1205/1205 green after Task 1 (fresh Assembly-CSharp.dll recompile), after Task 2 (fresh Assembly-CSharp-Editor.dll recompile, no compilation failure), and after the scene save (data-only, editor-asm stamp unchanged).

## Task Commits

Each task was committed atomically:

1. **Task 1: collapse to one success field set + rewrite the moment onto a standalone overlay** - `fc7a55e` (feat)
2. **Task 2: builder — tear down nested SuccessCta clusters + build standalone SuccessOverlay** - `7808aa0` (feat)
3. **Task 3: run the builder + commit Main.unity alone+immediately** - `a4fba79` (docs — scene, committed alone per the parallel-scene-clobber rule)

**Plan metadata:** committed separately with STATE/ROADMAP/REQUIREMENTS updates (docs).

## Files Created/Modified

- `Assets/Scripts/Main/Manager.cs` - One success field set + `SuccessOverlay` ref; `ShowInteractiveSuccessMoment(Bot)` standalone-overlay rewrite (no authPage reactivation, both auth hierarchies deactivated up front); parameterless `CloseSuccessAndOverlay`; both call sites arg-free; auth code flow untouched.
- `Assets/Editor/OnboardingAuthBlocksBuilder.cs` - `DestroyAllByName` teardown of both nested `SuccessCta` clusters; `BuildSuccessSheet` → `BuildStandaloneOverlay(Canvas, out GameObject)` (root-Canvas-parented, SetAsLastSibling, direct-child idempotent teardown); 6-field Manager re-stamp; trust cards + sentinel unchanged.
- `Assets/Scenes/Main.unity` - Standalone `SuccessOverlay` as the last child of the root Canvas; both nested `SuccessCta` clusters removed; 6 Manager refs stamped.

## Decisions Made

- **`GetComponentInParent<Canvas>(true).rootCanvas`** (hardening over the plan's bare `GetComponentInParent<Canvas>()`): `include-inactive` avoids a null when the code panel is inactive at build time, and `.rootCanvas` guarantees the overlay parents to the ROOT canvas (42635013) rather than any nested canvas — directly satisfying the tightened acceptance criterion.
- **Direct-child-only idempotent teardown** for the standalone overlay: a recursive `DestroyAllByName(canvas, "SuccessOverlay")` would have destroyed the two nested per-channel panels (also named `SuccessOverlay`). Iterating the canvas's direct children only keeps re-runs safe.
- **No rounded corners on the full-screen backdrop** — it fills the entire screen; this is the sole reason the scene diff shows a net -5 `RoundedCorners` (old: rounded sheet bg + 3 rounded children ×2 = 8; new: 3 rounded children = 3).
- **Scene committed alone+immediately** (parallel-scene-clobber rule / environment constraint) rather than folded into a task commit — mirrors 11-03/11-05.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Hardened the root-Canvas resolution so the overlay lands on fileID 42635013**
- **Found during:** Task 2 (builder — resolving the parent Canvas)
- **Issue:** The plan's snippet `waCodePanel.GetComponentInParent<Canvas>()` returns null if the code panel is inactive at build time, and returns the NEAREST canvas (possibly a nested one) rather than the root — either would break the tightened criterion "new overlay's `m_Father` == root Canvas RectTransform 42635013".
- **Fix:** `waCodePanel.GetComponentInParent<Canvas>(true)` (include-inactive) + `canvas = canvas.rootCanvas` before building; explicit `InvalidOperationException` if still null.
- **Files modified:** Assets/Editor/OnboardingAuthBlocksBuilder.cs
- **Verification:** Scene parse confirms the new overlay GO (809814349) Transform `m_Father` == 42635013 (the Canvas RectTransform, class 224, GO named "Canvas" with a Canvas component), and it is the last Canvas child.
- **Committed in:** `7808aa0` (Task 2 commit)

**2. [Rule 2 - Missing critical] Direct-child-only idempotent teardown of the standalone overlay**
- **Found during:** Task 2 (builder — re-run safety)
- **Issue:** The plan mandated "a single standalone SuccessOverlay build" but the two pre-existing nested per-channel panels are ALSO named `SuccessOverlay`; a naive recursive `DestroyAllByName(canvas, "SuccessOverlay")` for idempotency would delete `WhatsappAuthSuccessPanel`/`TelegramAuthSuccessPanel` (the transient 2s check still uses one).
- **Fix:** Iterate `canvas.transform` DIRECT children only, destroying just a prior standalone overlay.
- **Files modified:** Assets/Editor/OnboardingAuthBlocksBuilder.cs
- **Verification:** Scene shows `m_Name: SuccessOverlay` 2→3 (the two nested panels survive); `WhatsappAuthSuccessPanel`/`TelegramAuthSuccessPanel` refs intact; suite 1205/1205.
- **Committed in:** `7808aa0` (Task 2 commit)

**3. [Environment override] Builder run through the open Editor; scene committed alone+immediately**
- **Found during:** Task 3
- **Issue:** The Editor was OPEN (PID 6443), so this executor cannot drive Unity menus; and the scene-commit discipline requires the scene alone+immediately.
- **Resolution:** Checkpoint → orchestrator ran `Tools/Onboarding/Build Auth Blocks` + `save_scene` via mcp-unity (both descriptive sentinels, zero console errors) → scene committed alone (`a4fba79`); code tasks kept their own per-task commits. All subjects carry 11-08. Identical to 11-05's documented pattern.
- **Impact:** None on outcome; all acceptance checks pass.

---

**Total deviations:** 2 auto-fixed (1 blocking root-canvas hardening, 1 missing-critical teardown safety) + 1 environment-driven execution-path substitution. No scope change; all plan behaviour delivered as written.

## Issues Encountered

- **Two Task-2 acceptance greps are internally inconsistent / miscounted — the true intent is satisfied and verified:**
  - `grep -c "waSuccess\|tgSuccess" == 0` collides with criterion 1, which literally MANDATES `DestroyAllByName(waSuccessPanel.transform, "SuccessCta")`. The remaining 5 matches are ONLY the pre-existing panel LOCAL VARS `waSuccessPanel`/`tgSuccessPanel` (resolved from Manager's `WhatsappAuthSuccessPanel`/`TelegramAuthSuccessPanel` refs), which the plan requires the teardown to use. The real intent — the TEN per-channel FIELD stamps removed — is met exactly (`grep -c` of the 10 field names == 0).
  - `grep -c "BuildTrustCard" == 2` expects 2 but the actual (byte-identical-to-11-05) count is 3 (2 calls + 1 method declaration). Trust cards are genuinely untouched — the git diff shows zero changes to any `BuildTrustCard` line.
  - Both are plan-authoring inaccuracies, not code problems; verified via intent-precise greps + git diff.
- **Editor was OPEN (as the environment stated).** Verified via the in-Editor `ClaudeTestBridge` (empty `Temp/claude/run-tests.trigger` + `osascript activate` to keep Unity focused so the EditMode runner ticks at full speed — an unfocused Editor throttles the update loop and starves the run). Task-1 freshness gated on `Assembly-CSharp.dll` (runtime edit) advancing to 10:40:18-ish → 10:41:18Z; Task-2 on `Assembly-CSharp-Editor.dll` advancing to 10:59:12Z with no compilation failure; the scene save was correctly data-only (stamp unchanged).
- **Suite baseline is 1205, not the prompt's 1165.** v1.2 Phases 9/10 added tests since Phase 11 first executed; 0 failures is the gate and it held on every run.

## Threat Model Compliance

All two `mitigate` dispositions applied: **T-11-08-01** (auth code flow untouched — `ShowWhatsappAuth`/`ShowTelegramAuth` and every `GetChild(3/4/5)` unchanged at 21 references; both code panels still 10 children with `TrustBlock` last; the byte-identical greps pass), **T-11-08-02** (`bot==null`/`SuccessOverlay==null` yield-break; `dismissed` set by BOTH buttons; parameterless `CloseSuccessAndOverlay` always hides the overlay + lands on Bots). The **accept** disposition **T-11-08-03** holds by construction — the nested panels stay; only their injected `SuccessCta` child clusters are removed, and the `WaitForSeconds(2f)` transient stays under the `moreAuthSteps` branch. No new threat surface — client-only UI relocation, no network/auth/schema change.

## Known Stubs

None — the standalone overlay is fully built, copy-complete and wired: the 6 Manager refs are stamped non-zero, `ShowInteractiveSuccessMoment` drives the labels/buttons at runtime, and the green check pops via the existing `SuccessCheckPop`. Visual calibration on device rides the phase's UAT tail (11-10 Round-2 re-verify).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- **D2 closed (code + scene):** the «Бот подключён!» moment now renders on a standalone full-screen overlay above the auth pages; nothing of the code UI shows beneath. «Загрузить прайс-лист» → «Прайс-листы» tab; «Позже» → Bots; «Открыть чаты» files-exist fallback preserved on settings re-auth.
- **Auth byte-identical:** the WhatsApp pairing-code and Telegram phone→code→2FA flows are unchanged (the relocation is the only behavioural change).
- **Unblocks 11-09** (D1+D3 checklist gap, wave 2 depends on 11-08) and **11-10** (Round-2 device re-verify addendum, wave 3). The end-to-end D2 verdict (clean full-screen celebration on a real device) rides the 11-10 owner gate.

## Self-Check: PASSED

- `.planning/phases/11-first-run-onboarding-flow/11-08-SUMMARY.md` present on disk.
- All three task commits present in git history (`fc7a55e`, `7808aa0`, `a4fba79`).
- Modified files carry the expected payload: `Manager.cs` has `SuccessOverlay`; `OnboardingAuthBlocksBuilder.cs` has `BuildStandaloneOverlay`; `Main.unity` has `m_Name: SuccessOverlay` (×3).
- All task acceptance greps re-run and pass (Task-1 auth-byte-identical set; Task-2 intent-precise set; Task-3 scene set: overlay `m_Father`==42635013, `SuccessCta` 2→0, 6 refs non-zero, TrustBlock last child); EditMode suite 1205/1205 green on fresh recompiles + the data-only scene-save run.

---
*Phase: 11-first-run-onboarding-flow*
*Completed: 2026-07-23*
</content>
</invoke>
