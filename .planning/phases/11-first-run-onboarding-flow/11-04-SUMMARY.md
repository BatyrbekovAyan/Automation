---
phase: 11-first-run-onboarding-flow
plan: 04
subsystem: ui
tags: [onboarding, success-moment, auth, coroutine, deep-link, telegram-parity, unity, manager]

# Dependency graph
requires:
  - phase: 11-first-run-onboarding-flow (plan 01)
    provides: SuccessCtaSelector.Choose + SuccessCta enum, Bot.OpenSettingsAtProductTab deep-link, OnboardingKeys
  - phase: 11-first-run-onboarding-flow (plan 03)
    provides: existing-user auto-flag + gate already landed in Manager.cs (LoadBots) — read current file state, not plan line numbers
  - phase: existing
    provides: ShowAuthSuccess coroutine, CreateBotFromForm wizard, WhatsappAuthSuccessPanel/TelegramAuthSuccessPanel scene objects, BottomTabManager consts, UploadedFilesStore, Manager.openBot/openBotSettings
provides:
  - "ShowInteractiveSuccessMoment(Bot, useTelegram): the FINAL «Бот подключён!» beat — per-channel panel/field selection, host-hierarchy reactivation, files-exist CTA, wait-for-user dismissal"
  - "CloseSuccessAndOverlay: deferred authPage deactivation + AddBotPanel close + land on Bots tab"
  - "Per-channel [SerializeField] private success-panel button/label field sets (wa*/tg*) for Plan 05's OnboardingAuthBlocksBuilder to stamp"
  - "Re-sequenced ShowAuthSuccess: transient 2s check + LoadingPanel cover + authPage deactivation now gated to the intermediate WhatsApp-of-both step ONLY; final creating-bot auth + settings re-auth hand off to the interactive moment"
affects: [onboarding-auth-blocks-builder, first-run-device-uat]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Single interactive success moment keyed to the created/existing bot, fired from exactly two named Manager sites (creation post-Step-6 + settings re-auth else branch), guarded against double-fire by isCreatingBot"
    - "Per-channel field-set selection (wa*/tg*) by a useTelegram bool because the two success panels live in separate hierarchies — a shared label/Button cannot child both"
    - "Deferred host-hierarchy deactivation: reactivate authPage before showing a nested overlay, defer SetActive(false) to dismissal"

key-files:
  created:
    - .planning/phases/11-first-run-onboarding-flow/11-04-SUMMARY.md
  modified:
    - Assets/Scripts/Main/Manager.cs

key-decisions:
  - "Reused the existing method-scoped useTelegram local in CreateBotFromForm instead of the plan's re-declared bool (would not compile — already in scope; semantics identical: selectedPlatform 2||3)"
  - "moreAuthSteps computed as isCreatingBot && selectedPlatform==3 && authPage!=TelegramAuth so the transient path is unreachable during settings re-auth (isCreatingBot false), keeping the else-if the sole settings entry"
  - "Left CanvasGroup interaction-blocking on the transient (moreAuthSteps) path only; the final/settings paths never touch it, so no lingering non-interactive authPage"

patterns-established:
  - "Onboarding success moment: null-guarded per-channel field sets stamped by a later-wave builder; every use survives a null (mid-phase state stays shippable-green)"

requirements-completed: [ONB-03, ONB-05]

# Metrics
duration: ~13 min
completed: 2026-07-18
---

# Phase 11 Plan 04: Interactive «Бот подключён!» Success Moment (Logic) Summary

**The transient 2s auth-success checkmark is replaced by a single interactive «Бот подключён!» moment — fired once on the FINAL channel after the bot exists (and on settings re-auth with a files-exist fallback), selecting the channel's OWN nested success panel + field set, reactivating its host auth hierarchy, and deep-linking «Загрузить прайс-лист» into the bot's «Прайс-листы» tab — with the auth code-entry flow byte-identical and the EditMode suite green at 1165/1165.**

## Performance

- **Duration:** ~13 min
- **Started:** 2026-07-18T15:38:00Z
- **Completed:** 2026-07-18T15:51:00Z
- **Tasks:** 2 (both auto)
- **Files modified:** 1 (Manager.cs)

## Accomplishments
- New `ShowInteractiveSuccessMoment(Bot bot, bool useTelegram)` coroutine: per-channel panel/host/field selection (BLOCKER fix — the two `SuccessOverlay` panels are separate GameObjects in separate hierarchies), `authPage.SetActive(true)` reactivation before show (SCENE FACT: the panel is nested inside `WhatsappAuth`/`TelegramAuth`), files-exist CTA via `SuccessCtaSelector.Choose`, verbatim copy deck, and a wait-on-`dismissed` loop that replaces the fixed `WaitForSeconds(2f)`.
- `CloseSuccessAndOverlay` helper: hides the panel, performs the DEFERRED `authPage.SetActive(false)`, closes the Add-Bot overlay, and lands on the Bots tab — the close/switch that `ShowAuthSuccess` used to do inline for final auth.
- Ten new `[SerializeField] private` success-panel fields (WhatsApp + Telegram × title/body/primaryButton/primaryLabel/laterButton) — the builder↔component contract Plan 05 stamps; all usage null-guarded so the scene + suite stay green until then.
- `ShowAuthSuccess` re-sequenced: `moreAuthSteps` computed first; the transient 2s check + `LoadingPanel.SetActive(true)` + `authPage.SetActive(false)` now run ONLY on the intermediate WhatsApp-of-both step; the settings re-auth else branch fires the interactive moment (gated `!isCreatingBot`); the final creating-bot path does nothing (leaves `authPage` active for the moment).
- `CreateBotFromForm` fires the moment ONCE after Step 6 on `newBotComp` — so a "both"-channel creation shows it exactly once (no double-fire), and the WhatsApp/Telegram `GetChild(3/4/5)` code-entry flow is untouched.

## Task Commits

Each task was committed atomically:

1. **Task 1: per-channel fields + interactive success-moment coroutine (+ CloseSuccessAndOverlay)** - `be01bc5` (feat)
2. **Task 2: re-sequence ShowAuthSuccess + fire once on final auth / settings re-auth** - `1ef4897` (feat)

**Plan metadata:** committed separately with STATE/ROADMAP/REQUIREMENTS updates (docs).

## Files Created/Modified
- `Assets/Scripts/Main/Manager.cs` - Added 10 per-channel success-panel `[SerializeField] private` fields; added `ShowInteractiveSuccessMoment` + `CloseSuccessAndOverlay`; re-sequenced `ShowAuthSuccess` (transient path gated to `moreAuthSteps`; settings re-auth else branch); fired the moment once from `CreateBotFromForm` after Step 6.

## Decisions Made
- **Reused the existing `useTelegram` local** (declared at the top of `CreateBotFromForm`, `selectedPlatform == 2 || 3`) rather than re-declaring `bool useTelegram` as the plan's snippet literally shows — a re-declaration would not compile (already in scope). Semantics are identical, and the local survives `ResetAddBotForm()` resetting `selectedPlatform`, so the moment still targets the correct final channel.
- **`moreAuthSteps` includes `isCreatingBot`** (`isCreatingBot && selectedPlatform == 3 && authPage != TelegramAuth`), matching the plan item-1 formula, which makes the transient path unreachable during settings re-auth (`isCreatingBot` false) and keeps the `else if` the single settings entry point.
- **CanvasGroup interaction-block stays on the transient path only** — the final/settings paths never add or toggle the `authPage` CanvasGroup, so there is no lingering non-interactive auth page after the moment.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Reused the in-scope `useTelegram` local instead of the plan's re-declared bool**
- **Found during:** Task 2 (fire the moment from `CreateBotFromForm`)
- **Issue:** The plan's item-2 snippet declares `bool useTelegram = selectedPlatform == 2 || selectedPlatform == 3;` immediately before the `StartCoroutine`, but `CreateBotFromForm` already declares `useTelegram` at method scope (line ~1363). Re-declaring it is a duplicate-local compile error (CS0136/CS0128).
- **Fix:** Dropped the re-declaration and passed the existing `useTelegram` local (identical value; unaffected by `ResetAddBotForm` clearing `selectedPlatform`).
- **Files modified:** Assets/Scripts/Main/Manager.cs
- **Verification:** Fresh Assembly-CSharp.dll recompile (20:48:50, past the edit) + EditMode suite 1165/1165 green; `grep -c "StartCoroutine(ShowInteractiveSuccessMoment(" == 2`.
- **Committed in:** `1ef4897` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking — plan snippet vs. actual in-scope variable).
**Impact on plan:** No scope change; the resolution preserves the plan's exact `selectedPlatform 2||3` semantics. All plan behavior delivered as written.

## Issues Encountered
- **Prior-wave drift in Manager.cs.** Plan line references (ShowAuthSuccess 1610-1651, call sites 2110/2621) were stale — 11-03 already landed its gate/auto-flag and other edits shifted the file. Anchored every edit on method names/snippets (`ShowAuthSuccess`, `CreateBotFromForm` Step 6, the two `ShowAuthSuccess` call sites at the WhatsApp/Telegram status polls) instead of offsets, per the environment note.
- **Editor was OPEN (as the environment stated).** Verified via the in-Editor `ClaudeTestBridge` (empty `Temp/claude/run-tests.trigger` + `open -a Unity` to fire the focus-gated poll). Runtime-only edit, so the freshness gate was the `Assembly-CSharp.dll` mtime advancing to 20:48:50Z (past the 20:46:56Z Manager.cs write) — a compile error would have produced a failed/absent run, not a green 1165.

## Known Stubs
- **Per-channel success-panel fields are null in-scene until Plan 05.** The 10 new `waSuccess*`/`tgSuccess*` `[SerializeField]`s are intentionally unstamped this wave — Plan 05's `OnboardingAuthBlocksBuilder` builds + stamps them. Every read/write in `ShowInteractiveSuccessMoment` is null-guarded (`titleLabel != null`, `primaryBtn != null`, `if (successPanel == null) yield break`), so the app and the EditMode suite stay green in this mid-phase state. This is a cross-wave seam, not a defect; the end-to-end success moment is verified after Plan 05 stamps the buttons and in Plan 07 device UAT.

## Threat Model Compliance
All five `mitigate` dispositions applied: T-11-04-01 (auth `GetChild`/cooldown/code-entry flow untouched — byte-identical), T-11-04-02 (`bot==null`/`successPanel==null` yield break; reactivates its host; `dismissed` set by BOTH buttons so it always resolves; fires only after the bot exists), T-11-04-03 (invoked from exactly two sites, `StartCoroutine(ShowInteractiveSuccessMoment(` count == 2, creation-flow else branch gated `!isCreatingBot`), T-11-04-04 (per-channel `wa*/tg*` field sets selected by `useTelegram`), T-11-04-05 (verbatim owner-approved copy deck). No new threat surface — client-side navigation only, no network/auth/schema changes.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- ONB-03 logic half is complete: the interactive moment fires once on final auth after the bot exists (and on settings re-auth with the files-exist fallback), selects the correct channel's panel/fields, reactivates the host auth hierarchy, deep-links «Загрузить прайс-лист» → `OpenSettingsAtProductTab`, and defers `authPage` deactivation to dismissal.
- Plan 05 (`OnboardingAuthBlocksBuilder`) can now build both success-panel button clusters (title/body/primary/later × WhatsApp+Telegram) and stamp the 10 `waSuccess*`/`tgSuccess*` refs via `SerializedObject` — the field names are the contract.
- Auth code-entry flow byte-identical; end-to-end success moment (panel visuals, DOScale check, dismissal → correct destination) rides Plan 05 (buttons) + Plan 07 device UAT.

## Self-Check: PASSED
- `Assets/Scripts/Main/Manager.cs` present with `ShowInteractiveSuccessMoment` + `CloseSuccessAndOverlay` + 10 `wa*/tg*` fields (verified via grep).
- Both task commits present in git history (`be01bc5`, `1ef4897`).
- All task acceptance greps re-run and PASS (`StartCoroutine(ShowInteractiveSuccessMoment(` == 2; 0 in BotSettings.Auth.cs; sole `WaitForSeconds(2f)` in ShowAuthSuccess under `moreAuthSteps`; `GetChild(3/4/5)` unchanged); EditMode suite 1165/1165 green on a fresh recompile.

---
*Phase: 11-first-run-onboarding-flow*
*Completed: 2026-07-18*
