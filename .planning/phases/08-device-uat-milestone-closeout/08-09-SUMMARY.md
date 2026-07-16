---
phase: 08-device-uat-milestone-closeout
plan: 09
subsystem: ui
tags: [unity, telegram, localization, chat-list, sync-indicator, dotween, canvasgroup, headless-builder]

# Dependency graph
requires:
  - phase: 05
    provides: "ChatChannel/ActiveChannel + OnActiveChannelChanged + SetActiveBot/SetActiveChannel reset choreography (05-02); EmptyStateView channel-aware copy (05-02); SyncAllChats sync path (05-03/05-06)"
provides:
  - "Russian empty-state copy (create-bot + WhatsApp/Telegram connect states; brand names kept Latin)"
  - "ChatManager sync-lifecycle events OnChatListSyncStart / OnChatListSyncEnd (fired unconditionally around SyncAllChats' try/finally) + IsChatListSyncing getter"
  - "ChatListSyncIndicator MonoBehaviour — a Telegram-only chat-list sync pill (spinner + «Синхронизация…»), WhatsApp byte-identical"
  - "ChatListSyncIndicatorBuilder — idempotent headless builder stamping the pill into Screen_Whatsapp/ChatsPanel"
affects: [08-10 device re-verify, telegram-parity]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Channel-agnostic lifecycle events on ChatManager (fire for both channels) with display gating in the consumer MonoBehaviour — keeps WhatsApp byte-identical while adding a Telegram-only affordance"
    - "finally-fired end event PLUS defensive Hide on OnActiveBotChanged/OnActiveChannelChanged, because SetActiveBot/SetActiveChannel call StopAllCoroutines() and abandon the in-flight coroutine WITHOUT running its finally"

key-files:
  created:
    - Assets/Scripts/UI/ChatListSyncIndicator.cs
    - Assets/Editor/ChatListSyncIndicatorBuilder.cs
  modified:
    - Assets/Scripts/UI/EmptyStateView.cs
    - Assets/Scripts/Main/ChatManager.cs
    - Assets/Scenes/Main.unity

key-decisions:
  - "Sync events fire for BOTH channels (unconditional around SyncAllChats' try/finally); TG-gating lives in the indicator so WhatsApp's SyncingView window is untouched and byte-identical"
  - "Indicator hides on OnChatListSyncEnd AND on OnActiveBotChanged/OnActiveChannelChanged-away — a bot/channel switch StopAllCoroutines-abandons the in-flight SyncAllChats without running its finally, so the end event would never fire (T-08-09-01 stuck-pill mitigation)"
  - "IsChatListSyncing getter added for OnEnable catch-up — ChatsPanel is inactive at scene load, so a sync already in flight fired OnChatListSyncStart before the indicator subscribed"

patterns-established:
  - "Lifecycle event + consumer-side channel gate: channel-agnostic ChatManager signal, display decision in the view, so the other channel is provably unaffected"

requirements-completed: []

# Metrics
duration: ~20min
completed: 2026-07-16
---

# Phase 8 Plan 09: D8+D9 Polish (RU Empty-State Copy + Telegram Sync Indicator) Summary

**D8 RU-localised the empty-state copy; D9 added a Telegram-only chat-list sync pill (`ChatListSyncIndicator`) driven by new `OnChatListSyncStart`/`OnChatListSyncEnd` events around `SyncAllChats`, WhatsApp byte-identical.**

## Performance

- **Duration:** ~20 min
- **Started:** 2026-07-16T17:42Z (approx; first task commit 17:48)
- **Completed:** 2026-07-16T17:59:54Z (scene stamp) + continuation wrap-up
- **Tasks:** 3 (+ orchestrator scene stamp)
- **Files modified:** 5 (2 created, 3 modified)

## Accomplishments

- **D8 (RU copy):** replaced the 9 residual English empty-state literals in `EmptyStateView.cs` with Russian across all three reasons (create-bot, WhatsApp-connect, Telegram-connect); «WhatsApp»/«Telegram» brand names kept Latin. No English user-facing literal remains in the file.
- **D9 (TG sync indicator):** `ChatManager` now signals its chat-list sync lifecycle via `OnChatListSyncStart` / `OnChatListSyncEnd`, fired unconditionally in `SyncAllChats`' `try`/`finally`; a new `ChatListSyncIndicator` MonoBehaviour shows a small rotating spinner + «Синхронизация…» pill at the top of the list **only** on Telegram (WhatsApp ignores the start signal — its `SyncingView` window is untouched).
- **Scene:** the pill was built and stamped into `Screen_Whatsapp/ChatsPanel` by the idempotent headless `ChatListSyncIndicatorBuilder`, hidden by default (CanvasGroup alpha 0), and committed immediately with no sibling clobber.

## Task Commits

Each task was committed atomically:

1. **Task 1 (D8): Russianise the empty-state copy** — `8bf9271` (feat)
2. **Task 2 (D9): ChatManager sync-lifecycle events + ChatListSyncIndicator MonoBehaviour** — `9de709c` (feat)
3. **Task 3 (D9): Headless builder (ChatListSyncIndicatorBuilder)** — `fd8772b` (feat)
   - New-file `.meta` for the two new scripts (bridge-generated on import) — `3ebe2ae` (chore)
4. **Task 3b (D9): Scene stamp into Main.unity** — `7649da8` (feat, made by the orchestrator after the scene-stamp checkpoint)

**Plan metadata:** `docs(08-09): complete D8+D9 polish plan` (this commit)

## Files Created/Modified

- `Assets/Scripts/UI/EmptyStateView.cs` — 9 English literals → Russian (create-bot title/body/CTA; WhatsApp + Telegram «не подключён» title/body/CTA); brand names preserved Latin.
- `Assets/Scripts/Main/ChatManager.cs` — declared `OnChatListSyncStart`/`OnChatListSyncEnd` (≈125/128), added `IsChatListSyncing` getter (≈443) over the existing `_chatListSyncing` flag, and fired the two events inside `SyncAllChats` (start right after `_chatListSyncing = true`, end in the `finally` alongside `_chatListSyncing = false`). Both fire for BOTH channels — WhatsApp is unaffected because nothing consumes them there.
- `Assets/Scripts/UI/ChatListSyncIndicator.cs` (created) — CanvasGroup-toggled pill; subscribes in `OnEnable` to `OnChatListSyncStart`/`OnChatListSyncEnd`, `OnActiveChannelChanged`, `OnActiveBotChanged`; `IsTelegram()`-gated display; DOTween linear spinner (`SetUpdate(true)`); `IsChatListSyncing` catch-up on enable; every ref null-guarded (missing stamp or null ChatManager ⇒ clean no-op). Never intercepts taps (`interactable/blocksRaycasts = false`).
- `Assets/Editor/ChatListSyncIndicatorBuilder.cs` (created) — `[MenuItem("Tools/Chat List Sync Indicator/Build")]` + `StampHeadless` entrypoint; idempotent delete-and-rebuild; builds the rounded #EFEFF0 pill + #2AABEE ring spinner (reusing `Loading.png`) + «Синхронизация…» TMP label as the LAST child of `Screen_Whatsapp/ChatsPanel`, CanvasGroup alpha 0 default; stamps the `spinner`/`label` `[SerializeField]` refs via `SerializedObject`; saves the scene (Edit-Mode only).
- `Assets/Scenes/Main.unity` — one `ChatListSyncIndicator` GameObject/component stamped under ChatsPanel (committed by the orchestrator as `7649da8`).

## Decisions Made

- **Events are channel-agnostic; gating is in the view.** `SyncAllChats` fires the lifecycle events for both channels; only `ChatListSyncIndicator` consumes them and only on Telegram. This is the cleanest way to add a Telegram affordance while keeping WhatsApp (and its separate window-based `SyncingView`) byte-identical.
- **The end signal alone is not enough to guarantee the pill hides.** A bot or channel switch calls `StopAllCoroutines()`, which abandons an in-flight `SyncAllChats` WITHOUT running its `finally` — so `OnChatListSyncEnd` would never fire on a switch. The indicator therefore also hides on `OnActiveBotChanged` and on `OnActiveChannelChanged` away from Telegram (mirrors `SyncingView`). This directly hardens threat T-08-09-01 (indicator stuck "syncing").

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Hardened the stuck-pill (T-08-09-01) mitigation beyond the plan's single hide trigger**
- **Found during:** Task 2 (ChatListSyncIndicator implementation)
- **Issue:** The plan named only "channel-switch away from Telegram" as the extra hide trigger. But `SetActiveBot`/`SetActiveChannel` both call `StopAllCoroutines()`, which abandons an in-flight `SyncAllChats` WITHOUT executing its `finally` — so on a bot switch (and on the switch-TO side of a channel switch) `OnChatListSyncEnd` never fires and the pill could stay stuck. Additionally, `ChatsPanel` is inactive at scene load, so a sync already in flight fires `OnChatListSyncStart` before the indicator's `OnEnable` subscribes — the pill would miss the start and never appear (or appear without a catch-up path).
- **Fix:** (a) Added a public `IsChatListSyncing` getter on `ChatManager` and used it in the indicator's `OnEnable` to catch up (`if (IsTelegram() && IsChatListSyncing) BeginSpin()`); (b) subscribed the indicator to `OnActiveBotChanged → Hide` in addition to `OnActiveChannelChanged → Hide`. Mirrors the existing `SyncingView` lifecycle handling. This strengthens the T-08-09-01 register mitigation (indicator never stuck "syncing").
- **Files modified:** `Assets/Scripts/Main/ChatManager.cs` (getter), `Assets/Scripts/UI/ChatListSyncIndicator.cs` (catch-up + bot-switch hide)
- **Verification:** In-Editor EditMode suite green 1091/1091 FRESH; code review confirms every subscribe in `OnEnable` has its matching unsubscribe in `OnDisable`.
- **Committed in:** `9de709c` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 missing-critical, Rule 2)
**Impact on plan:** Necessary for correctness of the T-08-09-01 mitigation — an in-scope hardening of the exact stuck-pill threat the plan's threat model calls out. No architectural changes, no scope creep, WhatsApp byte-identical.

## Known Stubs

None. The RU copy is static UI text; the indicator is wired to live `ChatManager` sync events and a real DOTween spinner (no placeholder data path).

## Threat Flags

None. This plan adds static UI copy plus a client-only display pill; no new network endpoint, auth path, file access, or schema change. No server/n8n changes.

## Issues Encountered

None. Implementation and the scene stamp went as planned. The scene mutation required a checkpoint (the owner's Unity Editor was open, holding the project lock, so the headless builder correctly refused); the orchestrator ran `Tools/Chat List Sync Indicator/Build` via the Unity MCP, saved, verified by grep, and committed the scene immediately as `7649da8`.

## Test Status

- **EditMode suite: 1091/1091 FRESH green** via the in-Editor bridge (`Temp/claude/test-summary.json`: `overall: Passed`, `editorAssemblyWrittenUtc` 2026-07-16T12:53:27Z — the fresh compile that covered every 08-09 `.cs`, including the builder). The owner's Editor (PID 2797) is open, so `Tools/run-tests-headless.sh` correctly refuses the held lock.
- **Continuation suite re-confirmation:** not re-triggered. No `.cs` changed after 12:53:27Z (the only later change is the data-only scene stamp `7649da8`), so Unity would not bump `editorAssemblyWrittenUtc` and a re-run proves nothing new — suite last verified fresh-green 1091/1091 at 12:53:27Z; scene stamp is data-only. Scene payload verified instead by GUID/name grep (below).
- **Scene stamp verified (no clobber):** `ChatListSyncIndicator` `m_Script` GUID `2a5df4fe73799411fa70f4b07ea1a99d` present once; `m_Name: ChatListSyncIndicator` = 1; component `Assembly-CSharp::ChatListSyncIndicator` = 1; sibling components intact — `SyncingView` = 1, `EmptyStateView` = 1, `ChannelSwitcherView` = 1.

## Next Phase Readiness

- **08-10 (device re-verify) is the only remaining Phase-8 plan.** D8 (RU empty-state copy visible) and D9 (the sync pill appears during a Telegram chat-list load/refresh and never sticks) ride 08-10's consolidated on-device re-verify of D1–D9. The milestone is NOT complete until owner sign-off at 08-10.

## Self-Check: PASSED

- Created files exist: `Assets/Scripts/UI/ChatListSyncIndicator.cs` (+.meta), `Assets/Editor/ChatListSyncIndicatorBuilder.cs` (+.meta), `08-09-SUMMARY.md`.
- Modified files exist: `Assets/Scripts/UI/EmptyStateView.cs`, `Assets/Scripts/Main/ChatManager.cs`, `Assets/Scenes/Main.unity`.
- Commits exist: `8bf9271` (D8 RU copy), `9de709c` (D9 events + indicator), `fd8772b` (D9 builder), `3ebe2ae` (new-file .meta), `7649da8` (scene stamp).

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-16*
