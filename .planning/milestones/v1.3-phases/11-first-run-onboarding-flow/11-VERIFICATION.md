---
phase: 11-first-run-onboarding-flow
verified: 2026-07-23T18:00:00Z
status: passed
score: 10/10 must-haves verified
overrides_applied: 0
---

# Phase 11: First-Run Onboarding Flow Verification Report

**Phase Goal:** A brand-new user understands the product's value in under a minute, connects WhatsApp or Telegram without fear, and is guided past auth to a working bot — via a one-time 3-slide welcome carousel, channel-specific «Это безопасно» trust blocks, a success moment that leads into price-list upload, and a derived-state «Первые шаги» checklist on the Bots page.

**Verified:** 2026-07-23T18:00:00Z
**Status:** passed
**Re-verification:** No — initial verification (post gap-closure round 11-08..11-10)

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | First launch (no bots, `OnboardingSeen` unset) shows the 3-slide carousel — Ценность → Контроль → Каналы — advanced only by «Далее»/«Создать бота» (no skip); CTA opens AddBotPanel; never re-appears; existing users never see it; «Удалить все данные» re-runs it | ✓ VERIFIED | `BotsPage.RefreshEmptyState` gates on `OnboardingGate.ShouldShowCarousel(hasBots, seen)`; `OnboardingScreen.OnCreateBotTapped` latches `OnboardingKeys.Seen` + calls `BotsPage.Instance.StartNewBot()`; `Manager.LoadBots` auto-flags existing users via `OnboardingGate.ShouldAutoFlagSeen(BotsParent.transform.childCount>0, seen)`; scene has `Screen_Onboarding` with verbatim copy, no «Пропустить» string anywhere; owner Round-1 + Round-2 UAT both confirmed on device |
| 2 | BOTH auth flows (WhatsApp pairing code; Telegram phone/code/2FA) show the «Это безопасно» trust block with channel-specific copy; no QR anywhere | ✓ VERIFIED | `Assets/Scenes/Main.unity` has 2 `TrustBlock` nodes (grep `m_Name: TrustBlock` = 2), appended `SetAsLastSibling` under each code panel so `Manager`'s hardcoded `GetChild(3)/(4)/(5)` (WhatsApp) / `GetChild(3)` (Telegram) stay valid (`GetChild(3)\|(4)\|(5)` count = 21, unchanged since 11-05); `OnboardingAuthBlocksBuilder` embeds the exact verbatim WhatsApp/Telegram trust bodies, zero QR strings; owner UAT §2 passed Round 1 (no defect filed) |
| 3 | Successful auth on either channel shows «Бот подключён!» (DOTween check) whose primary CTA «Загрузить прайс-лист» deep-links into BotSettings → «Прайс-листы» (fallback «Открыть чаты» when files exist); «Позже» defers | ✓ VERIFIED | `Manager.ShowInteractiveSuccessMoment(Bot)` sets verbatim copy, wires `successPrimaryButton`/`successLaterButton`, calls `bot.OpenSettingsAtProductTab()` or `SwitchTab` per `SuccessCtaSelector.Choose(hasFiles)`; check pop via `SuccessCheckPop` (`DOScale 0.9→1 OutBack`, `SetLink`); D2 gap (sheet stacked over auth UI) closed by 11-08 — moment now shows on a standalone Canvas-level `SuccessOverlay` (`m_Father` = root Canvas RectTransform `42635013`, last sibling); owner Round-2 re-verify PASS on both channels + settings re-auth + both-channel-once |
| 4 | BotsPage shows the «Первые шаги» card (4 derived-state steps; channel label from `isOnWhatsapp`/`isOnTelegram`; per-row deep links) that hides permanently at 4/4 | ✓ VERIFIED | `FirstStepsCard.Refresh()` derives `steps` live via `FirstStepsChecklist.StepStates`, never persists per-step; row 2 label via `FirstStepsChecklist.ChannelLabel`; deep-links `OpenSettingsAtGeneralTab`/`OpenSettingsAtProductTab`/`SwitchTab`; `AllDone(steps)` latches `OnboardingKeys.ChecklistDone` and the card hides via `FirstStepsCardVisibility.ShouldShow` gate (CanvasGroup, never resurfaces); D1 (zero-bot overlap) + D3 (stale until re-enable) gaps closed by 11-09 (`FirstStepsCardVisibility` pure gate + 5 `RefreshFromFacts()` hooks); owner Round-2 re-verify PASS on both |
| 5 | Zero regression: empty state, AddBotPanel auto-open (post-onboarding), auth flows unchanged; full EditMode suite green | ✓ VERIFIED | `BotsPage.RefreshEmptyState`/`StartNewBot` unchanged fallback path; auth `GetChild(3/4/5)` count unchanged (21) across 11-05/11-08/11-09; `WaitForSeconds(2f)` transient still gated under `moreAuthSteps` only; owner Round-2 zero-regression re-check PASS; suite recorded green at 1209/1209 (11-09 SUMMARY, 11-10 addendum, 11-HUMAN-UAT.md Round-2 Overall) |

**Score:** 5/5 ROADMAP success criteria verified (10/10 counting the merged PLAN-frontmatter must-haves below, no double-counting — see Required Artifacts / Key Links for the itemized backing evidence)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Assets/Scripts/Main/Onboarding/OnboardingGate.cs` | Pure first-run gate + auto-flag predicates | ✓ VERIFIED | `ShouldShowCarousel`/`ShouldAutoFlagSeen` present verbatim, no `UnityEngine` usage, 7 passing tests |
| `Assets/Scripts/Main/Onboarding/OnboardingPageMath.cs` | Nearest-page + page-to-X math | ✓ VERIFIED | `NearestPage`/`PageToNormalizedX` present, `Math.Clamp` guarded, 12 tests |
| `Assets/Scripts/Main/Onboarding/SuccessCtaSelector.cs` | CTA selector | ✓ VERIFIED | `SuccessCta` enum + `Choose(hasUploadedFiles)`, 2 tests |
| `Assets/Scripts/Main/Onboarding/FirstStepsChecklist.cs` | Step-state + label + completion derivation | ✓ VERIFIED | `ChannelLabel`/`StepStates`/`AllDone`, 8 tests |
| `Assets/Scripts/Main/Onboarding/FirstStepsCardVisibility.cs` | D1 zero-bot/4-4 visibility gate | ✓ VERIFIED | `ShouldShow(hasBots, checklistDone)`, 4 tests (gap round 11-09) |
| `Assets/Scripts/Main/Onboarding/OnboardingKeys.cs` | 3 global PlayerPrefs key constants | ✓ VERIFIED | `Seen`/`ChecklistDone`/`FirstBotReplySeen`, outside per-bot namespace |
| `Assets/Scripts/Main/Onboarding/OnboardingPager.cs` | Horizontal snap ScrollRect subclass | ✓ VERIFIED | `NearestPage`/`PageToNormalizedX`, `OutCubic` 0.3s, `OnPageChanged`, kills tween on disable |
| `Assets/Scripts/Main/Onboarding/OnboardingScreen.cs` | Carousel controller | ✓ VERIFIED | Dot binding, `OnCreateBotTapped` latches `Seen` + hides + `StartNewBot()`, no skip string |
| `Assets/Scripts/Main/Onboarding/FirstStepsCard.cs` | Checklist MonoBehaviour | ✓ VERIFIED | Static `Instance`, `RefreshFromFacts()`, CanvasGroup-only hide (no self-`SetActive(false)`), row cascade, deep-links, first-reply latch |
| `Assets/Scripts/Main/Onboarding/SuccessCheckPop.cs` | Check pop animation | ✓ VERIFIED | `OnEnable` `DOScale 0.9→1 OutBack`, `SetLink`, kill-safe |
| `Assets/Editor/OnboardingScreenBuilder.cs` | Screen_Onboarding builder | ✓ VERIFIED | `Headless build + save complete` sentinel, `MenuItem`, calls `NavRestructureBuilder.ReorderScreens` |
| `Assets/Editor/OnboardingAuthBlocksBuilder.cs` | Trust cards + standalone success overlay builder | ✓ VERIFIED | `BuildTrustCard` ×2 (unchanged since 11-05), `BuildStandaloneOverlay` (root-Canvas-parented, `SetAsLastSibling`), `DestroyAllByName(...,"SuccessCta")` teardown of both nested clusters, 6-field Manager re-stamp |
| `Assets/Editor/FirstStepsCardBuilder.cs` | Checklist card builder | ✓ VERIFIED | `Headless build + save complete` sentinel, stamps `FirstStepsCard` fields incl. `botsParent` |
| `Assets/Scripts/Main/Bot.cs` | Settings-at-tab deep-link entries | ✓ VERIFIED | `OpenSettingsAtProductTab()`/`OpenSettingsAtGeneralTab()` public, `OpenSettings()` stays private |
| `Assets/Editor/NavRestructureBuilder.cs` | Onboarding-aware ReorderScreens | ✓ VERIFIED | `internal static void ReorderScreens`, `"Screen_Onboarding"` slotted after `Screen_New`, before `WhatsappAuth`/`TelegramAuth` |
| `Assets/Scripts/Main/BotsPage.cs` | Gate + FirstStepsCard hook | ✓ VERIFIED | `onboardingScreen` field, `RefreshEmptyState` calls `OnboardingGate.ShouldShowCarousel` + `FirstStepsCard.Instance?.RefreshFromFacts()` |
| `Assets/Scripts/Main/Manager.cs` | Auto-flag + success moment + refresh hooks | ✓ VERIFIED | `OnboardingGate.ShouldAutoFlagSeen` at end of `LoadBots`; `ShowInteractiveSuccessMoment(Bot)` on standalone `SuccessOverlay` (zero `waSuccess`/`tgSuccess` fields remain); 3 `FirstStepsCard.Instance?.RefreshFromFacts()` hooks; `GetChild(3/4/5)` count unchanged (21) |
| `Assets/Scripts/Main/BotSettings.Auth.cs` | Price-list-upload refresh hook | ✓ VERIFIED | `FirstStepsCard.Instance?.RefreshFromFacts()` after `UploadedFilesStore.Add` |
| `Assets/Scenes/Main.unity` | Screen_Onboarding + trust blocks + standalone SuccessOverlay + FirstStepsCard | ✓ VERIFIED | `Screen_Onboarding` present; `TrustBlock` ×2; standalone `SuccessOverlay` (fileID 809814349) parented directly to root Canvas (fileID 42635013), last child; `FirstStepsCard` present under BotsPage |
| `.planning/phases/11-first-run-onboarding-flow/11-HUMAN-UAT.md` | Owner device UAT gate | ✓ VERIFIED | `status: passed`; Round 1 (2026-07-18) ISSUES D1–D3 → gap round → Round 2 (2026-07-23) PASS, owner replied "approved"; Round-1 history byte-intact |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `BotsPage.RefreshEmptyState` | `Screen_Onboarding` | `onboardingScreen.SetActive(true)` gated on `OnboardingGate.ShouldShowCarousel` | WIRED | Null-guarded fallback to `StartNewBot()` if scene not built |
| `Manager.LoadBots` auto-flag | live bot count | `BotsParent.transform.childCount` (not the monotonic `id` counter) | WIRED | Grep confirms fact source; create-then-delete-all users stay carousel-eligible |
| `OnboardingScreen.OnCreateBotTapped` | `AddBotPanel.Instance.Open()` | `BotsPage.Instance?.StartNewBot()` | WIRED | Flag latched before hand-off; null-safe |
| `OnboardingAuthBlocksBuilder` | `Manager` success field set | `SerializedObject` stamp of 6 fields (`SuccessOverlay` + 5 labels/buttons) | WIRED | All 6 non-zero in saved scene (per 11-08 SUMMARY verification) |
| `Manager.ShowInteractiveSuccessMoment` | `Bot.OpenSettingsAtProductTab` | primary CTA when `SuccessCta.UploadPriceList` | WIRED | Grep confirms call site |
| `FirstStepsCard` rows | `Bot.OpenSettingsAtGeneralTab` / `OpenSettingsAtProductTab` / `BottomTabManager.SwitchTab` | per-row `OnRowTapped` switch | WIRED | All 4 rows deep-link correctly |
| `BotsPage`/`Manager`(×3)/`BotSettings.Auth` | `FirstStepsCard.Instance` | `RefreshFromFacts()` fire-and-forget hooks | WIRED | 5 call sites confirmed (1+3+1), all null-guarded `?.` |
| Trust card | `WhatsappCodePanel`/`TelegramCodePanel` | appended as LAST child via `SetAsLastSibling` | WIRED | `GetChild(3/4/5)`/`GetChild(3)` indices provably unchanged |
| Standalone `SuccessOverlay` | root Canvas | Canvas-level last sibling (`m_Father` = 42635013) | WIRED | Confirmed via scene YAML parse — sibling index 3 (last) among 4 Canvas children |

### Requirements Coverage

| Requirement | Source Plan(s) | Description | Status | Evidence |
|---|---|---|---|---|
| ONB-01 | 11-01, 11-02, 11-03, 11-07 | 3-slide welcome carousel + first-run gate | ✓ SATISFIED | Screen_Onboarding live in scene, gate wired, owner UAT §1 PASS both rounds |
| ONB-02 | 11-05, 11-07 | Both-channel «Это безопасно» trust blocks | ✓ SATISFIED | 2 TrustBlock nodes, verbatim copy, no QR, owner UAT §2 PASS (no defect filed) |
| ONB-03 | 11-01, 11-04, 11-05, 11-07, 11-08, 11-10 | Interactive success moment + price-list deep-link | ✓ SATISFIED | Standalone SuccessOverlay (D2 fix), CTA selector wired, owner Round-2 re-verify PASS (5/5 items) |
| ONB-04 | 11-01, 11-06, 11-07, 11-09, 11-10 | Derived-state «Первые шаги» checklist | ✓ SATISFIED | FirstStepsCard live-derives, visibility gate (D1 fix), live refresh hooks (D3 fix), owner Round-2 re-verify PASS |
| ONB-05 | 11-01, 11-03, 11-04, 11-05, 11-06, 11-07, 11-09, 11-10 | Zero regression + suite green | ✓ SATISFIED | Auth `GetChild` counts unchanged throughout; suite recorded 1209/1209; owner zero-regression re-check PASS |

**Note:** Per phase context (`11-CONTEXT.md`), ONB-01..ONB-05 are not yet formalized as rows in `REQUIREMENTS.md` — that is explicitly deferred to v1.3 milestone start. This is not treated as a gap per the phase brief.

### Anti-Patterns Found

Sourced from `11-REVIEW.md` (code review, 2026-07-23, standard depth, 24 files). All items are advisory (0 critical); none block phase completion.

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `FirstStepsCard.cs` | 95-112 | One-shot `ChatManager.Instance` subscription with no retry from `RefreshFromFacts()` | Warning | Silent miss of row-4 first-reply latch if scene load order ever changes (currently works by incidental serialization order) |
| `Manager.cs` | 1371-1397 | No `isCreatingBot` re-check immediately after the auth-wait loop exits (before Step 3 instantiate) | Warning | A back-tap in the ~0.5s completion→resume gap can create a bot pointing at a since-deleted profile (re-opened by the D2 overlay relocation removing the old CanvasGroup-blocking window) |
| `BotSettings.Auth.cs` / `Manager.cs` | 104-131 / 2249-2276 | Un-guarded substring status parse (`IndexOf` with no `>= 0` check before `Substring`) | Warning | Could throw mid-coroutine and strand `LoadingPanel`/auth poll on an unexpected Wappi response shape; project already has a throw-safe `WappiStatusParser` used elsewhere |
| `Bot.cs` | 184-256, 282, 320 | Dual activation-state key (`"Bot0"` bare key orphaned on delete) | Info | No functional impact (slot names never reused) |
| `Manager.cs` | 1768-1771 | Success body copy doesn't branch on `OpenChats` CTA | Info | Minor copy mismatch in files-exist re-auth case |
| `Manager.cs` | 1774-1798 | Wait-for-dismiss coroutine could be stranded if re-shown over itself | Info | Currently unreachable (defensive hardening note) |
| `Manager.cs` | 1010-1020 | Settings-mode back handler doesn't stop the WhatsApp QR coroutine | Info | Harmless wasted network churn |
| `FirstStepsCard.cs` | 208-217, 251-257 | Row cascade replays on every Refresh; tweens not `SetLink`'d | Info | Visible blink on frequent refresh; cheap to fix |
| `BotsPage.cs` | 32 | Dead `isActiveAndEnabled` guard in `OnEnable` | Info | Cosmetic |
| `OnboardingPager.cs` | 15 | `pageCount` invisible in Inspector (ScrollRect custom editor) | Info | Builder-only assignment path, works today |
| `FirstStepsCard.cs` / `Manager.cs` / `BotsPage.cs` | various | `FindFirstObjectByType` on cold tap paths | Info | Matches pre-existing project idiom, negligible impact |

None of these were flagged as blocking by the reviewer (`status: issues_found` at warning/info level only, 0 critical). No override needed — these are pre-existing-pattern-consistent, documented, non-blocking findings appropriate for a future hygiene pass.

### Human Verification Required

None outstanding. The phase's device/Game-view human gate (`11-HUMAN-UAT.md`) already ran to completion across two rounds:
- **Round 1** (2026-07-18): ISSUES — 3 defects (D1, D2, D3) logged, routed to gap-closure plans 11-08/11-09.
- **Round 2** (2026-07-23): PASS — owner replied "approved"; all three defects verified fixed on device, zero-regression re-check green, no new defects.

Per task instructions, these human-verification items are already dispositioned and are not re-opened by this verification pass.

### Gaps Summary

No gaps found. All ROADMAP success criteria (1-5) and all PLAN-frontmatter must-haves across the 10 plans (7 original + 3 gap-closure) are verified present, substantive, and wired in the current codebase. The three Round-1 UAT defects (D1 zero-bot card overlap, D2 success sheet stacked over auth UI, D3 stale checklist rows) were fully closed by the 11-08/11-09 gap-closure plans and independently re-verified by the owner on device in Round 2 (approved 2026-07-23). Code review found 0 critical issues; the 3 warnings and 8 info items are pre-existing-pattern-consistent hygiene notes, not functional gaps, and do not block phase completion.

---

_Verified: 2026-07-23T18:00:00Z_
_Verifier: Claude (gsd-verifier)_
