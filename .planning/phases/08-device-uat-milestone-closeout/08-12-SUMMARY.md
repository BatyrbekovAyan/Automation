---
phase: 08-device-uat-milestone-closeout
plan: 12
subsystem: ui
tags: [telegram, chat-list, sync-indicator, canvasgroup, dotween, coroutine, min-visible-duration, editmode-tests]

# Dependency graph
requires:
  - phase: 08-device-uat-milestone-closeout
    provides: "08-09 ChatListSyncIndicator pill + ChatManager OnChatListSyncStart/End + IsChatListSyncing"
  - phase: 05-*
    provides: "SetActiveChannel/SetActiveBot reset choreography, OnActiveChannelChanged, ClearAllLocalHistory (privacy clear)"
provides:
  - "ChatListSyncIndicatorGate: pure minimum-visible-duration decision (RemainingVisibleSeconds + ShouldHideNow)"
  - "Telegram sync pill that reads as a cue even on a fast (sub-legible) list load"
  - "switch-to-Telegram-while-syncing pill show + deferred-hide that never strands the pill"
affects: [08-16 device re-verify, telegram-parity]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure UnityEngine-free gate + WaitForSecondsRealtime deferred-hide coroutine (mirrors OpenChatLivePollGate + SyncingView)"
    - "Runtime-assembly freshness check: verify Assembly-CSharp.dll mtime (not editor-assembly stamp) when a change is runtime-only"

key-files:
  created:
    - "Assets/Scripts/Chat/ChatListSyncIndicatorGate.cs"
    - "Assets/Tests/Editor/Chat/ChatListSyncIndicatorGateTests.cs"
  modified:
    - "Assets/Scripts/UI/ChatListSyncIndicator.cs"

key-decisions:
  - "MinVisibleSeconds = 0.6s legible floor (tunable const in the pure gate)"
  - "Code-only fix — H4 occlusion refuted, so NO scene stamp / Main.unity change"
  - "Hide THROUGH the gate on OnChatListSyncEnd; BeginSpin re-arms + kills any pending deferred-hide"

patterns-established:
  - "Minimum-visible-duration: hold a fast async cue for a legible floor via a pure gate + realtime deferred-hide"

requirements-completed: []

# Metrics
duration: 12min
completed: 2026-07-17
---

# Phase 8 Plan 12: D9 Telegram Sync-Pill Legibility Summary

**A fast Telegram chat-list sync now reads as a cue: the «Синхронизация…» pill holds for a 0.6s legible floor (pure `ChatListSyncIndicatorGate`) and shows on switch-to-Telegram-while-syncing, instead of a sub-legible flash — code-only, WhatsApp byte-identical.**

## Performance

- **Duration:** 12 min
- **Started:** 2026-07-17T07:14:19Z
- **Completed:** 2026-07-17T07:26:27Z
- **Tasks:** 2
- **Files modified:** 3 (2 created + 1 modified; +2 Unity .meta)

## Accomplishments
- Diagnosed D9 in the Editor (no device): recorded confirm/refute verdicts for H1–H5 with exact code evidence.
- Shipped a pure, tested `ChatListSyncIndicatorGate` (minimum-visible-duration decision) — 6 EditMode tests.
- Applied a code-only fix to `ChatListSyncIndicator`: hide-through-the-gate (H1) + switch-to-Telegram show (H3) + deferred-hide that never strands the pill (T-08-12-01) + privacy-clear rescue (H5).
- Full EditMode suite green at 1111/1111 (1105 baseline + 6 new) via the in-Editor bridge; WhatsApp byte-identical.

## Diagnosis — H1–H5 verdicts (Task 1)

Traced `SyncAllChats` (ChatManager.cs:451-492), `SetActiveChannel` (ChatManager.Channel.cs:50-100), `InitializeActiveBotNextFrame`/`BeginLoadForActiveBot` (ChatManager.BotState.cs:250-334), and `ClearAllLocalHistory` (ChatManager.PrivacyClear.cs:76-120).

- **H1 (fast sync / cached paint) — CONFIRMED (primary cause).** On the normal path `SyncAllChats` DOES fire `OnChatListSyncStart`→`BeginSpin` (alpha=1) and then always `yield return www.SendWebRequest()` (line 468), so the pill renders — but only for the network round-trip. On a fast device/connection with an already-cached list (which paints instantly), that is a sub-legible blink; the eye reads "list appeared, no cue". The single non-yielding path is the empty-profile `yield break` (line 458-461) = the no-connection state where no pill is wanted anyway. The literal "Show()→Hide() same frame" only occurs on that empty-profile path; the SPIRIT of H1 (sync faster than a *legible* frame) is the real defect. **Fix: minimum-visible floor so even a 50ms sync holds the pill ≥0.6s.**
- **H2 (missed during activation) — PARTIALLY CONFIRMED but benign / not the reported symptom.** A start+end pair CAN fire before the indicator's `OnEnable` subscribes (ChatsPanel is inactive at scene load — the startup sync at `InitializeActiveBotNextFrame`); the `OnEnable` catch-up only covers a STILL-in-flight sync. But this only affects a sync that runs before the user is even on the chats screen; the switch-to-TG sync the user triggers happens with the panel already active and subscribed. Not the reported failure; unchanged by (and not worsened by) this fix.
- **H3 (channel-read timing) — REFUTED as the described race; defensive show still added.** Startup sets `ActiveChannel = ResolveChannelForBot(...)` (BotState.cs:330) BEFORE the load (line 332), and `SetActiveChannel` sets `ActiveChannel` (Channel.cs:65) BEFORE the event+re-sync (line 99), so no Telegram-bound sync fires under a momentary WhatsApp with "no later re-fire" — and `SetActiveChannel` DOES re-sync after the flip (so `HandleSyncStart` sees Telegram and `BeginSpin`s). The specific "dropped with no re-fire" failure does not occur on the traced paths. Task 2 still adds `HandleActiveChannelChanged: if (IsChatListSyncing) BeginSpin()` as defense-in-depth (and per the plan) for any ordering where a sync is mid-flight at the moment the channel event fires.
- **H4 (z-order / alpha occlusion) — REFUTED.** The pill is the LAST child of ChatsPanel (renders above the list), `raycastTarget=false`, CanvasGroup alpha-toggled with no ancestor CanvasGroup forced to 0, using the exact sibling/CanvasGroup pattern as the working `SyncingView`/`EmptyStateView`. The H1 flash is proof that alpha reaches the screen (a fully occluded pill would never flash). **No scene z-order stamp needed — the fix stays code-only.**
- **H5 (privacy-clear strands the pill — the WR-01/02/03 StopAllCoroutines-without-events shape) — CONFIRMED-SAFE by design.** `ClearAllLocalHistory` does `StopAllCoroutines()` + `_chatListSyncing=false` WITHOUT firing `OnChatListSyncEnd`, then `BeginLoadForActiveBot()` re-syncs. The killed sync's end never fires, so the pill must NOT depend on it — and it does not: the resync's `OnChatListSyncStart`→`BeginSpin` re-arms `_shownAtRealtime` and kills any pending deferred-hide, and the resync's `OnChatListSyncEnd` hides through the gate. No `OnActiveBotChanged`/`OnActiveChannelChanged` fires during a privacy clear, so those hide paths do not interfere. Task 2 preserves this: `BeginSpin` calls `KillDeferredHide()` first.

## Applied fix (Task 2)

Code-only baseline (H1 primary + H3 defense-in-depth), in `ChatListSyncIndicator.cs`:
- `_shownAtRealtime` set in `BeginSpin` via `Time.realtimeSinceStartup` (consistent with the DOTween spinner's `SetUpdate(true)`).
- `HandleSyncEnd` hides THROUGH `ChatListSyncIndicatorGate.ShouldHideNow(...)`: if the floor elapsed, hide now; else start a `DeferredHideRoutine` that `WaitForSecondsRealtime(RemainingVisibleSeconds)` then hides IFF no new sync re-armed it. A leading `alpha <= 0f` guard keeps WhatsApp a no-op (byte-identical) and never starts a deferred coroutine off-Telegram.
- `HandleActiveChannelChanged`: on switch TO Telegram, `if (IsChatListSyncing) BeginSpin()`; switch-away still `Hide()`s.
- `BeginSpin` and every hide path (`Hide`, `OnDisable`, `OnActiveBotChanged`→`Hide`, switch-away) call `KillDeferredHide()` so a pending deferred-hide can never outlive a bot/channel switch (T-08-12-01).
- **Chosen `MinVisibleSeconds = 0.6f`** (tunable const, documented in the gate).
- **Scene stamp needed? NO** — H4 refuted; `ChatListSyncIndicatorBuilder`/Main.unity untouched.

## Task Commits

1. **Task 1 (RED): failing gate + tests** - `14ecfe8` (test) — stub gate + 6 tests; 4 meaningful assertions fail (1111 total, 4 failed).
2. **Task 1 (GREEN): gate implementation** - `d172213` (feat) — real min-visible logic; 1111/1111.
3. **Task 2: apply gate + switch-to-TG show** - `4d189f3` (feat) — indicator fix; 1111/1111.
4. **Unity .meta for new files** - `328b809` (chore).

**Plan metadata:** (final docs commit)

## Files Created/Modified
- `Assets/Scripts/Chat/ChatListSyncIndicatorGate.cs` - Pure minimum-visible-duration decision (`MinVisibleSeconds`, `RemainingVisibleSeconds`, `ShouldHideNow`); UnityEngine-free.
- `Assets/Tests/Editor/Chat/ChatListSyncIndicatorGateTests.cs` - 6 EditMode tests: hide blocked while in-flight, hide blocked before floor, hide allowed once settled/elapsed, boundary inclusive, clamp-at-zero, positive-before-elapsed.
- `Assets/Scripts/UI/ChatListSyncIndicator.cs` - Gate-driven deferred hide, switch-to-Telegram show, deferred-hide lifecycle (kill on switch/disable/new-sync).

## Decisions Made
- **MinVisibleSeconds = 0.6s** — long enough to read a spinning cue, short enough to stay out of the way.
- **Code-only fix, no scene change** — H4 occlusion refuted; a scene stamp would have been escalated to a checkpoint but was not needed.
- **Hide through the gate on end + re-arm on start** — a fast sync holds the pill; a new sync always re-owns it (also the H5 privacy-clear rescue).

## Deviations from Plan

None - plan executed exactly as written. The code-only baseline (H1 + H3) matched the confirmed diagnosis; H4 was refuted so the escalation-to-checkpoint branch was not taken.

## Issues Encountered
- **Stale-run confusion (resolved):** the GREEN gate run reported `editorAssemblyWrittenUtc` unchanged from the RED run. Root cause: `ChatListSyncIndicatorGate` is RUNTIME code (`Assembly-CSharp`), and the test signature was unchanged, so Unity did not rebuild the EDITOR assembly the bridge stamps. Confirmed the run was genuinely fresh by checking `Assembly-CSharp.dll` mtime (07:19:21Z, post-edit) and by the decisive logic RED=4-fail → GREEN=0-fail (a stale run would have reproduced the 4 failures). Task 2's run rebuilt BOTH assemblies (07:23:52Z / 07:23:54Z, post-edit) and advanced `finishedAt`, so no ambiguity there.

## Test Verification
- In-Editor bridge (Unity Editor open, `run-tests-headless.sh` correctly refuses the single-instance lock).
- RED: `14ecfe8` state — 1111 total, 1107 passed, 4 failed (the meaningful gate assertions), stamp 07:17:28Z.
- GREEN gate: `d172213` state — 1111/1111 passed (runtime `Assembly-CSharp.dll` rebuilt 07:19:21Z post-edit).
- Task 2: `4d189f3` state — 1111/1111 passed, fresh run (`finishedAt` 2917.72 > 2619.89; both assemblies rebuilt 07:23:52Z/07:23:54Z post-edit), overall Passed.

## Next Phase Readiness
- **Device re-verify rides 08-16** (Gate A round 2): opening/refreshing the Telegram list shows the pill on a normal (fast) load; WhatsApp shows none; the pill never sticks after a bot/channel switch or a privacy clear.
- No blockers. No new threat surface (client-only display timing; no server/n8n/schema change). Threat register T-08-12-01/02 both mitigated.

## Self-Check: PASSED

- All created/modified files exist on disk (3 .cs + 2 .meta + SUMMARY.md).
- All 4 task commits present in git (14ecfe8, d172213, 4d189f3, 328b809).
- Gate acceptance: `ShouldHideNow` + `MinVisibleSeconds` present, no `using UnityEngine` (pure), 6 `[Test]` (≥4).
- Task 2 acceptance: `ChatListSyncIndicatorGate` consumed, `IsChatListSyncing` inside `HandleActiveChannelChanged`, `realtime` tracked, `IsTelegram` gate intact, 4 subs / 4 unsubs, deferred-hide killed on Hide/OnDisable/BeginSpin.
- EditMode suite 1111/1111 green (fresh run, both assemblies rebuilt post-edit).

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-17*
