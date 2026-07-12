---
phase: 05-channel-aware-chatmanager-core
plan: 02
subsystem: api
tags: [unity, csharp, chatmanager, telegram, playerprefs, cache-isolation, empty-state, channel-seam]

# Dependency graph
requires:
  - phase: 05-01
    provides: "ChatChannel enum, WappiEndpoints, ChatIdFormat, ChannelTabStateResolver, OutboxEntry.channel"
provides:
  - "ChatManager.ActiveChannel (public read; persisted per bot in PlayerPrefs {botId}ActiveChatChannel)"
  - "SetActiveChannel(ChatChannel) — full SetActiveBot reset choreography + OnActiveChannelChanged event"
  - "Channel-aware GetActiveProfileId / GetCacheRoot (BotCache/{botId}/ vs .../telegram/) / empty-state / sync-gate"
  - "ResolveChannelForBot + pure ChannelResolver (auto-select connected channel on switch/startup, persist correction)"
  - "ChannelCachePath.SubDir pure channel->cache-subdir mapping"
  - "EmptyStateReason.BotHasNoTelegram + EmptyStateView Telegram copy"
affects: [05-03, 05-04, 05-06, phase-6-channel-switcher, phase-7-suggestions-dashboard]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure static decision seams (ChannelResolver, ChannelCachePath) — unit-testable, no I/O, WhatsAppSyncGate precedent"
    - "Single-home channel->field delegation (ProfileIdForChannel) so channel-aware call sites don't duplicate the switch"
    - "Reset-choreography reuse: SetActiveChannel mirrors SetActiveBot exactly so guards/queues/outbox reset identically"
    - "Defensive PlayerPrefs read: clamp out-of-range persisted ordinal to WhatsApp before any enum switch"

key-files:
  created:
    - Assets/Scripts/Main/ChatManager.Channel.cs
    - Assets/Tests/Editor/Chat/ChannelResolutionTests.cs
    - Assets/Tests/Editor/Chat/ChannelCacheRootTests.cs
  modified:
    - Assets/Scripts/Main/ChatManager.BotState.cs
    - Assets/Scripts/Main/ChatManager.cs
    - Assets/Scripts/UI/EmptyStateView.cs

key-decisions:
  - "Channel identity lives in a new ChatManager.Channel.cs partial (per D4 discretion) rather than bloating BotState.cs."
  - "Channel->profile-field switch extracted once to ProfileIdForChannel (Channel.cs), used by GetActiveProfileId + BeginLoadForActiveBot + RefreshActiveBotChats — DRY single home instead of inlining bot.telegramProfileId per call site."
  - "Telegram cache subdir is the pure constant ChannelCachePath.TelegramSubDir; WhatsApp branch keeps the exact legacy Path.Combine (byte-identical, no migration)."
  - "ComputeCurrentEmptyState now delegates to ChannelTabStateResolver directly (05-01 channel-neutral core) instead of the WhatsApp wrapper, mapping NoConnection -> channel-specific empty state."
  - "Sync window stays WhatsApp-only: IsWhatsAppSyncing is gated behind ActiveChannel==WhatsApp; Telegram writes no window and skips the sync-gate entirely."

patterns-established:
  - "Pattern 1: Channel-aware behavior reads ActiveChannel and routes field/path/empty-state selection through pure helpers; WhatsApp branch is preserved byte-identical as the regression net."
  - "Pattern 2: SetActiveChannel === SetActiveBot reset body (stop sync-wait, drop outbox, clear lists, StopAllCoroutines, zero guards, clear queues, BeginLoad) with channel persistence + OnActiveChannelChanged substituted for the bot equivalents."

requirements-completed: [CHAT-01, CHAT-11]

# Metrics
duration: 19min
completed: 2026-07-12
---

# Phase 5 Plan 02: ChatManager Identity Seam Summary

**ChatManager gained a channel dimension: ActiveChannel (persisted per bot), a SetActiveChannel that reuses SetActiveBot's full reset choreography, OnActiveChannelChanged, channel-aware profile/cache-root/empty-state/sync-gate, auto-selecting channel resolution on switch/startup, and a Telegram empty state — WhatsApp behavior byte-identical, EditMode suite 854/854 green.**

## Performance

- **Duration:** 19 min
- **Started:** 2026-07-12T17:26:22Z
- **Completed:** 2026-07-12T17:45:52Z
- **Tasks:** 3 (all `type=auto`)
- **Files modified:** 6 (3 created, 3 modified)

## Accomplishments

- `ChatManager.ActiveChannel` — public-read `ChatChannel` property defaulting to WhatsApp, persisted per bot in PlayerPrefs `{botId}ActiveChatChannel` (int ordinal). Missing key => WhatsApp; out-of-range on-disk value clamped to WhatsApp (T-0502-01).
- `SetActiveChannel(ChatChannel)` reproduces `SetActiveBot`'s exact reset set (stop `_syncWaitRoutine`, `_outbox = null`, clear `Chats`/`chatLookup`, `OnChatListCleared`, `StopAllCoroutines`, zero `_chatFetchesInFlight`, `_chatListSyncing = false`, clear vthumb + media queues, `BeginLoadForActiveBot`), routing through `ShowChatList()` first when a chat is open, and fires the new `OnActiveChannelChanged` event (Phase 6 consumes it).
- `GetActiveProfileId()` now follows `ActiveChannel` (WhatsApp or Telegram profile id) through the same `IsValidProfileId` guard — zero call-site churn via the single-home `ProfileIdForChannel` helper.
- `GetCacheRoot()` isolates caches: WhatsApp keeps the legacy `BotCache/{botId}/` (byte-identical, no migration); Telegram nests `BotCache/{botId}/telegram/`. `PurgeCacheForBot` already recurses `BotCache/{botId}` so both channels are swept (verified by test, not restructured) — CHAT-11.
- Channel resolution on bot switch/startup: `SetActiveBot` and `InitializeActiveBotNextFrame` set `ActiveChannel = ResolveChannelForBot(botId)` before load; if the persisted channel is unconnected but the other IS connected, the connected one is auto-selected and the correction persisted.
- Telegram-only bots surface `EmptyStateReason.BotHasNoTelegram` ("Telegram not connected" / "Connect Telegram to this bot to see its chats." / "Connect Telegram", reusing the `OpenCurrentBotAuth` CTA) instead of dead-ending on WhatsApp copy. The sync-gate applies only when `ActiveChannel==WhatsApp`.
- Pure, unit-tested seams: `ChannelResolver.Resolve(persisted, waConnected, tgConnected)` and `ChannelCachePath.SubDir(channel)` — following the WhatsAppSyncGate/ChannelTabStateResolver pure-seam precedent.

## Task Commits

1. **Task 1: ChatManager.Channel.cs identity seam** — `48d2872` (feat)
   - `ActiveChannel`, persistence helpers, `OnActiveChannelChanged`, `SetActiveChannel`, `ResolveChannelForBot`, `ProfileIdForChannel`, plus pure `ChannelResolver` + `ChannelCachePath`. Suite 839/839 green.
2. **Task 2: channel-aware profile/cache-root/empty-state/sync-gate + restore** — `29f3177` (feat)
   - `EmptyStateReason.BotHasNoTelegram`; `GetActiveProfileId`/`GetCacheRoot`/`BeginLoadForActiveBot`/`RefreshActiveBotChats`/`ComputeCurrentEmptyState`/`SyncAllChats` channel-aware; `NoConnectionEmptyState` helper; channel restore in `SetActiveBot` + `InitializeActiveBotNextFrame`. Suite 839/839 green.
3. **Task 3: Telegram empty-state copy + channel resolution/cache-root tests** — `8ad829e` (test)
   - `EmptyStateView` `BotHasNoTelegram` case; `ChannelResolutionTests` (10 cases, full matrix + clamp) + `ChannelCacheRootTests` (5 cases, isolation + safe segment). Suite 854/854 green (+15).

## Files Created/Modified

- `Assets/Scripts/Main/ChatManager.Channel.cs` (created) — channel identity partial + pure `ChannelResolver` + `ChannelCachePath`.
- `Assets/Scripts/Main/ChatManager.BotState.cs` (modified) — channel-aware `GetActiveProfileId`/`GetCacheRoot`/`ComputeCurrentEmptyState`/`BeginLoadForActiveBot`/`RefreshActiveBotChats`; `NoConnectionEmptyState`; channel restore in `SetActiveBot`/`InitializeActiveBotNextFrame`.
- `Assets/Scripts/Main/ChatManager.cs` (modified) — `EmptyStateReason.BotHasNoTelegram`; `SyncAllChats` null-profile fire is channel-aware.
- `Assets/Scripts/UI/EmptyStateView.cs` (modified) — `BotHasNoTelegram` case mirroring WhatsApp copy, same `OpenCurrentBotAuth` CTA.
- `Assets/Tests/Editor/Chat/ChannelResolutionTests.cs` (created) — `ChannelResolver.Resolve` matrix + out-of-range/negative clamp.
- `Assets/Tests/Editor/Chat/ChannelCacheRootTests.cs` (created) — `ChannelCachePath.SubDir` WhatsApp-empty vs telegram, isolation, safe-segment (no separator/`..`).

## Decisions Made

- **Single-home channel->field switch.** Rather than inlining `bot.whatsappProfileId` / `bot.telegramProfileId` at each of the three channel-aware call sites (`GetActiveProfileId`, `BeginLoadForActiveBot`, `RefreshActiveBotChats`), the switch lives once in `ProfileIdForChannel` (Channel.cs). This matches 05-01's established "single home / delegate" pattern and keeps the three methods DRY. Consequence: the literal string `telegramProfileId` and the lowercase `"telegram"` cache-subdir constant live in `ChatManager.Channel.cs`, not `ChatManager.BotState.cs` (see Deviations).
- **ComputeCurrentEmptyState uses the channel-neutral resolver directly.** Switched from the `WhatsAppTabStateResolver` wrapper to `ChannelTabStateResolver` (the 05-01 core) so `NoConnection` maps to the channel-specific empty state via `NoConnectionEmptyState()`. The WhatsApp wrapper remains for its own tests/call sites.
- **Telegram has no sync window.** No Telegram post-creation sync window is written at auth, so every sync-gate check is guarded by `ActiveChannel==WhatsApp`; Telegram loads its cache/chats immediately.

## Deviations from Plan

### Filing-only (not behavioral): channel->field switch + telegram subdir literal live in Channel.cs

- **What:** Task 2's per-task `<acceptance_criteria>` include `grep -q "telegramProfileId"` and `grep -q "telegram"` against `ChatManager.BotState.cs`. Because the channel->profile-field switch was extracted once into `ProfileIdForChannel` and the cache subdir into `ChannelCachePath.TelegramSubDir` (both in `ChatManager.Channel.cs`, per D4's "internal file organization is Claude's discretion"), those two literal strings resolve against `Channel.cs` rather than `BotState.cs`.
- **Why:** DRY — inlining the same switch in three methods duplicates logic; the single-home helper matches the phase's established delegation pattern (05-01).
- **Impact:** None on behavior. All frontmatter `must_haves` are met, the key_link pattern `"ActiveChannel"` is present in BotState.cs, and BotState.cs drives channel selection through `ActiveChannel` + the helpers. `ChatChannel.Telegram` appears in BotState.cs (NoConnectionEmptyState + sync-gate).
- No Rule 1-4 auto-fixes were required; all three tasks landed on-contract.

## Threat Register Coverage

- **T-0502-01 (Tampering, `{botId}ActiveChatChannel`):** mitigated — `ReadPersistedChannel` clamps any non-`{0,1}` on-disk value to WhatsApp before use, and `ChannelResolver.Resolve` re-clamps its int input; no out-of-range enum cast reaches a switch. Covered by `ChannelResolutionTests` out-of-range/negative cases.
- **T-0502-02 (Information Disclosure, cross-channel cache leakage):** mitigated — `GetCacheRoot` gives each channel a distinct sub-dir (asserted distinct by `ChannelCacheRootTests`), and `SetActiveChannel`'s full reset clears in-memory `Chats`/`chatLookup` + guards so a stale channel's data never renders after a switch.
- **T-0502-03 (Path traversal, telegram subdir):** accept — the channel segment is the hardcoded constant `"telegram"` (no separators, no `..`, asserted by `ChannelCacheRootTests`); botId already passes `SanitizeBotId`.

## Do-Not-Touch Carry-Forward (Phase 7)

`N8nSuggestionsProvider.cs` reads `bot.whatsappProfileId` directly and BYPASSES `GetActiveProfileId` — it is intentionally NOT routed through the channel seam in this phase. Leave it for Phase 7 (suggestions channel-awareness); do not "fix" it early.

## WhatsApp Regression Net

- `GetCacheRoot` WhatsApp branch is the exact legacy `Path.Combine(Application.persistentDataPath, "BotCache", botId)` — no `telegram` segment for WhatsApp.
- `WhatsappSyncUntil` read-path key + `IsWhatsAppSyncing` semantics unchanged; sync window still applies to WhatsApp.
- Empty-state/events/order identical for a WhatsApp-only bot (default channel; no channel key written until a switch occurs).
- Full EditMode suite: **854/854 green** via `Tools/run-tests-headless.sh` (Editor closed), up from the 839 wave-1 baseline (+15 new tests).

## Next Phase Readiness

- 05-03 (URL builder wiring + tapi parser divergences) and 05-04 (send-path branches) can read `ActiveChannel` and route via `WappiEndpoints.Sync(ActiveChannel, ...)`.
- Phase 6 (channel switcher UI) has its hook: call `SetActiveChannel` and subscribe to `OnActiveChannelChanged`.

## User Setup Required

None — no external service configuration.

## Self-Check: PASSED
