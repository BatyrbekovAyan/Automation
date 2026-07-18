---
phase: 11-first-run-onboarding-flow
plan: 06
subsystem: ui
tags: [onboarding, checklist, derived-state, deep-links, scene-builder, dotween, telegram-parity, unity]

# Dependency graph
requires:
  - phase: 11-first-run-onboarding-flow (plan 01)
    provides: pure FirstStepsChecklist (ChannelLabel/StepStates/AllDone) + OnboardingKeys + Bot.OpenSettingsAtGeneralTab/OpenSettingsAtProductTab deep-link seams
  - phase: 11-first-run-onboarding-flow (plan 05)
    provides: proven open-Editor builder/checkpoint flow (orchestrator mcp-unity execute_menu_item + save_scene, scene committed alone+immediately)
  - phase: existing
    provides: ChatManager events (OnBatchMessagesLoaded/OnLiveMessagesReceived), UploadedFilesStore, BotsPage.StartNewBot, BottomTabManager.WhatsAppTabIndex, NavRestructureBuilder/DashboardPageBuilder helper idioms
provides:
  - "«Первые шаги» checklist card lives in Main.unity under BotsPage (Header→ScrollView→FirstStepsCard→EmptyState), all 6 FirstStepsCard refs stamped"
  - "FirstStepsCard: 4 step states derived LIVE from facts every Refresh (never stored per-step), channel-aware row-2 label, per-row deep-links, 0.05s cascade, permanent 4/4 hide"
  - "First-reply proxy latch (FirstBotReplySeen) off ChatManager events (isIncoming==false)"
  - "FirstStepsCardBuilder: idempotent [MenuItem Tools/Onboarding/Build Checklist Card] + BuildHeadless sentinel for re-runs/recalibration"
affects: [first-run-device-uat, onboarding-verification]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Derived-state card: MonoBehaviour snapshots live facts → pure class derives states → render; only terminal latches persisted (ChecklistDone / FirstBotReplySeen)"
    - "Row cascade as CanvasGroup DOFade with .SetDelay(i*0.05f) — position stays owned by the rows' VerticalLayoutGroup"
    - "Reversible host-list inset: banner reserves BotsParent VLG top padding while visible, restores the authored value on permanent hide"

key-files:
  created:
    - Assets/Scripts/Main/Onboarding/FirstStepsCard.cs
    - Assets/Editor/FirstStepsCardBuilder.cs
  modified:
    - Assets/Scenes/Main.unity

key-decisions:
  - "Card is a BotsPage-level overlay sibling ordered before EmptyState (not a BotsParent child) — BotsParent.childCount stays the authoritative bot-exists fact and EmptyState remains the topmost zero-bot cover"
  - "Cascade animates CanvasGroup alpha (DOFade + SetDelay stagger) instead of position — rows are laid out by a VerticalLayoutGroup, which would fight a position tween"
  - "Row-4 hint rendered as an always-visible caption under the rows rather than a tap-triggered reveal — the copy is permanently surfaced, zero extra state"
  - "Banner reserves the bots-list top padding (reversible, authored value cached) so the first bot card clears the card — restored on permanent hide"

patterns-established:
  - "Live-derived checklist card over a pure derivation class (FirstStepsChecklist) — the anti-storage discipline the spec mandates (T-11-06-01)"

requirements-completed: [ONB-04, ONB-05]

# Metrics
duration: ~13 min
completed: 2026-07-18
---

# Phase 11 Plan 06: «Первые шаги» Derived-State Checklist Card Summary

**BotsPage now carries the «Первые шаги» card — 4 rows derived LIVE from facts via the pure FirstStepsChecklist (never stored per-step), channel-aware «Подключить WhatsApp/Telegram» label, per-row deep-links (create → AddBotPanel, connect → General tab, upload → Product tab «Прайс-листы», first-reply → Chats), a 0.05s-stagger cascade, a first-reply proxy latch off ChatManager events, and a permanent hide latched at 4/4 — scene built through the open Editor and committed immediately, suite green at 1165/1165.**

## Performance

- **Duration:** ~13 min active (16:43Z → 16:56Z, including one checkpoint round-trip for the builder run)
- **Started:** 2026-07-18T16:43:00Z
- **Completed:** 2026-07-18T16:56:00Z
- **Tasks:** 2 (both auto; Task 2's builder execution went through a checkpoint resolved by the orchestrator's mcp-unity)
- **Files modified:** 5 (2 created + metas, Main.unity)

## Accomplishments

- **FirstStepsCard (Task 1):** thin MonoBehaviour over the Plan-01 pure `FirstStepsChecklist`. Every `Refresh()` snapshots live facts — `botsParent.childCount>0`, per-bot `isOnWhatsapp/isOnTelegram` flags, `Bot.UnauthedProfileSentinel`-guarded profile-id auth, `UploadedFilesStore.Load(bot,"product"/"service")`, and the `FirstBotReplySeen` latch — and derives the 4 states via `StepStates`; nothing per-step is ever persisted (T-11-06-01). Row 2's label is `$"Подключить {FirstStepsChecklist.ChannelLabel(isWa,isTg)}"`. Completed rows: green `#23A55A` circle + white tick, strikethrough + muted label, chevron hidden. Progress: «N из 4» + slim Primary fill over an `#EDEFF3` track.
- **Deep-links:** row 1 → `BotsPage.Instance?.StartNewBot()`; row 2 → `bot?.OpenSettingsAtGeneralTab()` (General tab — the connect toggles, NOT the Product fallback); row 3 → `bot?.OpenSettingsAtProductTab()` («Прайс-листы»); row 4 → `SwitchTab(BottomTabManager.WhatsAppTabIndex)` with the hint «Попросите знакомого написать вам — и посмотрите, как бот ответит» rendered under the rows.
- **First-reply latch (T-11-06-03, accepted proxy):** subscribes `OnBatchMessagesLoaded`/`OnLiveMessagesReceived` in OnEnable (unsubscribes in OnDisable); any `isIncoming==false` message latches `OnboardingKeys.FirstBotReplySeen` once and re-renders.
- **Permanent hide (T-11-06-02):** top of `Refresh()` early-returns `gameObject.SetActive(false)` when `OnboardingKeys.ChecklistDone` is set; the latch is written the moment `AllDone(steps)` is true. Never resurrects, even if facts later regress.
- **Builder (Task 2):** `FirstStepsCardBuilder` clones the NavRestructureBuilder/DashboardPageBuilder envelope verbatim (font GUIDs, `DestroyAllByName` idempotency, Image+sprite icons only, deferred RoundedCorners bake, SerializedObject stamping). Card = white rounded 40-radius banner below the 300-unit header: Head (bold 44 title + Primary 32 progress), 20-unit progress bar, 4 rows (84 units, 48-unit check circle, 38 label, chevron-right, transparent-hit Button + CanvasGroup), 30 muted hint.
- **Scene (Task 2):** built through the open Editor (orchestrator ran `Tools/Onboarding/Build Checklist Card` + save via mcp-unity; sentinel logged, zero console errors) and committed immediately. Payload grep-verified: all 6 stamped refs non-zero (`botsParent`=759505691, the authored BotsParent RT), BotsPage child order Header→ScrollView→FirstStepsCard→EmptyState (EmptyState last), `BotsParent.m_Children` still `[]` (bot-count fact intact), EmptyState scene count 4→4 (no sibling clobber).
- **Zero regression (ONB-05):** EditMode suite 1165/1165 green on freshly compiled assemblies after the code landed (16:15:44Z → 16:49:41Z), and again after the scene save (data-only — stamp correctly unchanged).

## Task Commits

1. **Task 1: FirstStepsCard component** - `9397267` (feat)
2. **Task 2: FirstStepsCardBuilder (code half)** - `e026d43` (feat)
3. **Task 2: scene build — card under BotsPage + 6 stamps** - `032b418` (docs — Main.unity alone, committed immediately after the builder run per the parallel-scene-clobber rule)

**Plan metadata:** committed separately with STATE/ROADMAP/REQUIREMENTS updates (docs).

## Files Created/Modified

- `Assets/Scripts/Main/Onboarding/FirstStepsCard.cs` - Derived-state checklist card: live facts → pure derivation → render; cascade, deep-links, first-reply latch, permanent 4/4 hide, reversible list inset.
- `Assets/Editor/FirstStepsCardBuilder.cs` - Idempotent [MenuItem "Tools/Onboarding/Build Checklist Card"] + `BuildHeadless` (exact sentinel `[FirstStepsCardBuilder] Headless build + save complete`); builds the card + stamps all 6 refs.
- `Assets/Scenes/Main.unity` - FirstStepsCard subtree under BotsPage (Head/ProgressBar/Rows Row0..Row3/Hint), EmptyState kept last.

## Decisions Made

- **BotsPage-level sibling, not a BotsParent child.** The plan's "order it before the located BotsParent" cannot be taken literally — BotsParent is nested inside ScrollView/Viewport, not a BotsPage sibling, and parenting the card into BotsParent would corrupt `childCount` (the bot-exists fact) and wrap it in the list's VLG. The card sits between ScrollView and EmptyState: visually above the list, EmptyState still the topmost zero-bot cover.
- **CanvasGroup fade cascade.** Rows live under a VerticalLayoutGroup that owns their positions, so the 0.05s-stagger cascade tweens alpha (`DOFade(1, 0.3f).SetDelay(i*0.05f)`) — the ui-scripts stagger idiom without fighting the layout system.
- **Hint always visible.** The row-4 hint is a permanent caption under the rows (builder seeds it, runtime re-stamps it). "Tapping also surfaces the hint" is satisfied trivially — the guidance is already on screen when the user reaches row 4, with zero extra visibility state.
- **Reversible list inset.** While the banner is visible it reserves the bots-list VLG top padding (authored value cached at first reserve, restored on permanent hide) so the first bot card is never hidden under the card.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical] Reversible bots-list top inset so the card never covers the first bot card**
- **Found during:** Task 1/2 (placement analysis)
- **Issue:** The card is an overlay anchored below the header; the bots ScrollView spans the same region, so with ≥1 bot the first card would render underneath the banner — the checklist targets exactly that one-bot user.
- **Fix:** `FirstStepsCard` reserves `BotsParent`'s VLG top padding (serialized `reservedListTopPadding`, default 700) while visible and restores the cached authored value on permanent hide.
- **Files modified:** Assets/Scripts/Main/Onboarding/FirstStepsCard.cs
- **Verification:** Suite green; padding restore path runs in the same Refresh that latches ChecklistDone.
- **Committed in:** `9397267`

**2. [Environment override] Builder run through the open Editor; scene commit split from the plan's single-commit acceptance**
- **Found during:** Task 2
- **Issue:** The plan's primary path (`Tools/run-editor-builder.sh`) requires a closed Editor; the Editor was open (PID 1327) and this executor cannot drive Unity menus. The plan's acceptance also wanted `Main.unity` + both new `.cs` in ONE commit, while the environment's scene-commit discipline requires the scene alone+immediately.
- **Resolution:** Checkpoint → orchestrator ran the menu item + save via mcp-unity (sentinel logged 21:52:42 local, zero console errors) → scene committed alone+immediately (`032b418`); the component and builder each had their own per-task commit. All three subjects carry `11-06`. Identical to the 11-03/11-05 documented pattern.
- **Impact:** None on outcome; all payload acceptance checks pass.

---

**Total deviations:** 1 auto-fixed (missing-critical placement fix) + 1 environment-driven execution-path substitution. No scope change; all plan behavior delivered as written.

## Issues Encountered

- **New-file import quirk (known):** focusing the Editor alone imported 0 assets — the two new `.cs` files got their `.meta`s only when the ClaudeTestBridge's own pre-run `AssetDatabase.Refresh()` fired on the armed trigger. Both metas verified present before relying on the compile.
- **Scene diff churn (known-benign):** the save produced an 18.9k-line diff (layout zeroing + material regen per project memory). Verified per-fileID instead: FirstStepsCard 0→2 occurrences, EmptyState count 4→4, BotsParent children `[]`→`[]`, all 6 stamps non-zero.

## Known Stubs

None — the card is fully wired end-to-end: the builder stamps all 6 refs, every row derives its state from live facts and deep-links to a real navigation target, and both latches persist through PlayerPrefs. Visual calibration on device/Game view rides the phase's normal UAT tail.

## Threat Model Compliance

All three `mitigate` dispositions applied: T-11-06-01 (states derived LIVE every Refresh via pure `FirstStepsChecklist`; grep-provable — the only PlayerPrefs writes are the two spec'd latches), T-11-06-02 (`ChecklistDone` early-return at the top of `Refresh` deactivates and returns; never resurrects), T-11-06-04 (Main.unity committed alone in the builder-run task, immediately after the save). T-11-06-03 `accept`: the `isIncoming==false` first-reply proxy is documented in-code as covering both a bot reply and an owner outgoing message. No new threat surface — client-only UI over existing facts and nav entry points.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- ONB-04 complete: the install→working-bot leg now has a persistent, self-updating guide from bot creation through the first live reply, with a channel-true connect row.
- Plan 11-07 (phase tail) proceeds against an unchanged auth/nav flow; the builder is idempotent, so visual recalibration is a re-run of `Tools/Onboarding/Build Checklist Card` away.
- Device/Game-view visual pass (cascade feel, card spacing next to the bot card, strikethrough rendering) rides the phase UAT.

## Self-Check: PASSED

- Both created files present on disk with paired `.meta`s; Main.unity payload grep-verified (6 stamps non-zero, child order, BotsParent untouched).
- All three task commits present in git history (`9397267`, `e026d43`, `032b418`).
- All acceptance criteria re-run and PASS; EditMode suite 1165/1165 green after both the code compile (fresh 16:49:41Z stamps) and the scene save.

---
*Phase: 11-first-run-onboarding-flow*
*Completed: 2026-07-18*
