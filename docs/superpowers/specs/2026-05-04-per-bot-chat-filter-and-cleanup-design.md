# Per-Bot Chat Filter and Deletion Cleanup — Design

**Date:** 2026-05-04
**Scope:** WhatsApp chats page (`Screen_Whatsapp`) and bot deletion flow.
**Related code:** `Assets/Scripts/Main/ChatManager.cs`, `Assets/Scripts/Main/Bot.cs`, `Assets/Scripts/Chat/ChatHistoryCache.cs`, `Assets/Scripts/Chat/MediaCacheManager.cs`, `Assets/Scripts/UI/ChatListView.cs`.

## Problem

Two related problems on the WhatsApp chats page today:

1. The chats list is fetched with a hardcoded global `profileId = "af80627e-6d9d"` ([ChatManager.cs:35](../../../Assets/Scripts/Main/ChatManager.cs)) that does not correspond to any specific bot. With multiple bots — each having their own connected WhatsApp account (`whatsappProfileId`) — the user sees a single shared inbox that is unrelated to the bot they're currently working with. There is no way to switch between different bots' inboxes.

2. Cached chats (`all_chats_cache.json`), per-chat message histories (`chat_{chatId}.json`), and downloaded media (`MediaCache/{md5}.jpg`) persist on disk after a bot is deleted. None of these caches are touched by [Bot.cs:115-187](../../../Assets/Scripts/Main/Bot.cs) `DeleteBot()`, so a deleted bot's chat data continues to appear in the UI indefinitely.

## Goals

- The chats page filters its data by which bot is currently selected, fetched from that bot's `whatsappProfileId`.
- A bot selector in the chats page header lets the user switch between bots.
- Deleting a bot purges all of its cached chat data — chat list, message history, and media — from local disk.
- The selected bot persists across app sessions.
- The change is one-time backwards-compatible: existing flat caches are discarded on first launch after the update.

## Non-goals

- **Telegram chats.** No Telegram chat fetcher exists in the codebase yet. This spec is WhatsApp-only. The cache layout is structured so a future Telegram chat fetch can drop in without rework, but no Telegram code is added here.
- **Chat assignment across bots.** Each bot has its own WhatsApp account/profile; chats are owned by exactly one bot via that profile. There is no cross-bot chat sharing or assignment logic.
- **Server-side or cloud-side deletion of chat data** (other than the existing `profile/delete` calls that already happen). Only local on-device cache is purged on bot deletion.
- **Deleting in-flight downloads or running coroutines.** Cache deletion happens after the user confirms bot deletion via the existing confirm popup.

## Architecture

### Cache layout — per-bot subfolders

Cache moves from a flat global layout to per-bot subtrees, keyed by the bot's GameObject name (`transform.name`, e.g. `Bot0`). This is the same key the project already uses for PlayerPrefs (`Bot0Name`, `Bot0Products0`, etc.).

```
{Application.persistentDataPath}/BotCache/
  Bot0/
    chats.json                  # the bot's chat list (replaces all_chats_cache.json)
    messages/{chatId}.json      # per-chat message history (replaces flat chat_{chatId}.json)
    media/{md5}.{ext}           # per-bot media cache (replaces flat MediaCache/{md5}.jpg)
  Bot1/
    chats.json
    messages/...
    media/...
```

**Why GameObject name and not `whatsappProfileId`:** the rest of the app already keys per-bot state by `transform.name`. A bot whose WhatsApp gets disconnected and reconnected will rotate its `whatsappProfileId`, but its identity to the user — and to all other persisted state — is the bot itself. `whatsappProfileId` is used only at the API call boundary.

**Cache root resolution:**

```csharp
// In ChatManager
public string GetCacheRoot()
    => Path.Combine(Application.persistentDataPath, "BotCache", CurrentBotId);
```

`ChatHistoryCache` and `MediaCacheManager` are refactored so all path construction routes through `ChatManager.Instance.GetCacheRoot()` (or accepts a base directory parameter). All callers within the existing chat code path use the active bot's root.

### ChatManager becomes bot-aware

Public surface added to `ChatManager`:

| Member | Purpose |
|---|---|
| `string CurrentBotId { get; }` | The active bot's GameObject name. |
| `void SetActiveBot(string botId)` | Persist `LastSelectedBotForChats`, fire `OnActiveBotChanged`, clear list, refresh. |
| `void PurgeCacheForBot(string botId)` | Delete `BotCache/{botId}/` recursively (try/catch). If `botId == CurrentBotId`, activate the next remaining bot under `BotsParent` (or fire `OnEmptyState(NoBotsExist)` if none remain). |
| `string GetCacheRoot()` | Path to current bot's cache directory. |
| `event Action<string> OnActiveBotChanged` | Fires after `SetActiveBot` switches state. |
| `event Action<EmptyStateReason> OnEmptyState` | Fires when the chats list cannot show data; the empty-state view subscribes. |

The existing hardcoded `profileId` field at [ChatManager.cs:35](../../../Assets/Scripts/Main/ChatManager.cs) is removed. `chats/filter`, `messages/get`, and any other Wappi calls that take a profile ID derive it from the active bot's `Bot.whatsappProfileId` at call time. If the active bot's `whatsappProfileId` is empty/null, the call is skipped and `OnEmptyState(BotHasNoWhatsApp)` is fired instead.

`EmptyStateReason` enum:

```csharp
public enum EmptyStateReason
{
    NoBotsExist,         // no bots at all (E3)
    BotHasNoWhatsApp,    // active bot has no whatsappProfileId yet (E1)
}
```

(E2 — current bot deleted mid-session — does not surface as an empty state; we auto-fall-back to the first remaining bot.)

### Last-selected persistence

Single PlayerPrefs key: `LastSelectedBotForChats` (string, the bot's GameObject name).

In `ChatManager.Awake` (after the bots have been instantiated under `BotsParent` — coordinate via `Manager.Instance` initialization order; if necessary, defer to first frame in `Start`):

1. Read `LastSelectedBotForChats`.
2. If the pref exists and a child of `BotsParent` with that name exists → activate it.
3. Else if `BotsParent.childCount > 0` → activate `BotsParent.GetChild(0).name` and write that as the new pref.
4. Else → fire `OnEmptyState(NoBotsExist)`. Do not fetch.

### Bot deletion cleanup

In [Bot.cs:115-187](../../../Assets/Scripts/Main/Bot.cs) `DeleteBot()`, alongside the existing PlayerPrefs clear and `Manager.Instance.DeleteProfilesAndWorkflows()` call:

```csharp
ChatManager.Instance.PurgeCacheForBot(this.transform.name);
```

`PurgeCacheForBot(string botId)`:

1. Compute `Path.Combine(Application.persistentDataPath, "BotCache", botId)`.
2. If the directory exists → `Directory.Delete(path, recursive: true)` wrapped in try/catch (defensive: cache may not have been created yet).
3. If `botId == CurrentBotId`:
   - Pick the next bot to activate: first remaining child of `BotsParent` not equal to the just-deleted bot.
   - If one exists → `SetActiveBot(nextBotId)`.
   - Else → clear `LastSelectedBotForChats`, fire `OnEmptyState(NoBotsExist)`.

The user-facing confirmation dialog is already provided by [BotSettingsDeleteBotPopupBuilder](../../../Assets/Editor/BotSettingsDeleteBotPopupBuilder.cs); cache wipe runs as part of the same confirmed delete and does not require additional confirmation.

### One-time migration on first launch after the update

PlayerPrefs flag: `BotCacheV1MigrationDone` (int, 0 or 1).

In `ChatManager.Awake`, before any cache reads:

1. If `PlayerPrefs.GetInt("BotCacheV1MigrationDone", 0) == 0`:
   - Delete legacy `all_chats_cache.json` if present.
   - Delete every legacy `chat_*.json` file in `Application.persistentDataPath`.
   - Delete legacy `MediaCache/` directory recursively if present.
   - `PlayerPrefs.SetInt("BotCacheV1MigrationDone", 1); PlayerPrefs.Save();`
2. Continue normal initialization.

User impact: first time they open a bot's chat list after the update, they wait for one fresh network fetch (typical ~1–2s on the existing fetch path). This is acceptable for a one-time event and is reported in the `ChatListView` empty/loading state already present.

## UI design

### Title bar of `Screen_Whatsapp` becomes a bot-switcher button

A horizontal row centered (or left-aligned, matching the existing header alignment), as a single Button:

- Avatar circle, 24×24, left.
- Bot name, TMP 18sp semibold.
- Chevron-down icon, 16×16, right.

Spacing: 8dp between avatar and name, 4dp between name and chevron. Tappable area extends 8dp around for a 44dp minimum touch target.

State binding:
- `OnActiveBotChanged` updates avatar + name in place.
- DOTween fade out (0.15s) → swap → fade in (0.15s) so the change reads as deliberate.
- If the active bot has no WhatsApp connected, a small grey dot overlays the bottom-right of the avatar (10×10).

### `Sheet_BotSwitcher` (new)

Bottom sheet, slides up from below.

- **Backdrop:** black at 40% opacity, fade in 0.25s; tap to dismiss.
- **Sheet panel:** rounded top corners (16dp via RoundedCorners package), white background, full screen width, dynamic height capped at 60% of screen, slides in 0.3s with `Ease.OutCubic` via DOTween.
- **Sheet header:** `"Select bot"` (TMP 16sp semibold, 16dp top, 24dp bottom padding).
- **Bot list:** vertical scrollable list of `BotSwitcherRow` instances; one per child of `BotsParent`.

### `BotSwitcherRow` prefab

- Height 64dp, 16dp horizontal padding, 8dp vertical, 4dp left/right inset within the sheet so selection highlight reads as a pill.
- **Avatar:** 40×40 circle on the left. Pulled from `GreenApiAvatarFetcher` if available; falls back to a colored initials avatar.
- **Stack to the right of avatar:**
  - Bot name — TMP 16sp medium (or semibold when selected).
  - Sub-line — TMP 12sp regular, muted color: `"WhatsApp connected"` (green dot prefix) or `"WhatsApp not connected"` (grey dot prefix), or the bot's phone number if known.
- **Selected state:** the entire row's background fills with the brand accent color at ~10% opacity, rounded 8dp; an accent left bar (1.5dp wide) on the leading edge for additional emphasis; bot name renders semibold instead of medium. **No radio or check icon.**
- **Tap feedback:** `DOPunchScale(Vector3.one * 0.04f, 0.18s)` then `ChatManager.Instance.SetActiveBot(botId)` → sheet animates closed (reverse of entry, 0.25s) → header title updates → chats list refreshes from cache (instant) and quietly re-syncs from the server.

### `EmptyStateView` (new, single component handling all empty states)

Sibling of `ChatListView` inside `Screen_Whatsapp`. Centered vertical stack:
- Icon, 64×64.
- Title — TMP 18sp semibold.
- Supporting text — TMP 14sp regular, muted.
- Primary button, 44dp tall.

Configured presets (driven by `EmptyStateReason`):

| Reason | Title | Body | Button label | Action |
|---|---|---|---|---|
| `BotHasNoWhatsApp` | "WhatsApp not connected" | "Connect WhatsApp to this bot to see its chats." | "Connect WhatsApp" | Opens the active bot's `BotSettings` Auth tab. |
| `NoBotsExist` | "No bots yet" | "Create your first bot to start managing chats." | "Create your first bot" | Opens the existing bot creation wizard via `Manager.Instance`. |

Listens to `ChatManager.OnEmptyState`. When fired, hides `ChatListView.content` parent and shows itself with the right preset; when `OnActiveBotChanged` fires (or chat data arrives), hides itself and re-shows the list.

## Data flow (after this change)

```
User opens Screen_Whatsapp
  └── ChatManager.Awake/Start runs migration if needed,
       reads LastSelectedBotForChats, calls SetActiveBot
       └── If no whatsappProfileId → fire OnEmptyState(BotHasNoWhatsApp), stop
       └── Else → load BotCache/{botId}/chats.json instantly into UI
                  → kick off SyncAllChats coroutine using bot.whatsappProfileId
                  → diff and update UI as new data arrives

User taps title bar → Sheet_BotSwitcher opens
  └── User taps row → ChatManager.SetActiveBot(newBotId)
       └── ClearChatList event → UI clears
       └── PlayerPrefs.LastSelectedBotForChats = newBotId
       └── OnActiveBotChanged → header updates
       └── Repeat fetch cycle for newBotId

User deletes a bot in BotsPage/BotSettings
  └── Existing confirm popup → Bot.DeleteBot()
       └── PlayerPrefs cleared (existing)
       └── Manager.DeleteProfilesAndWorkflows() (existing)
       └── ChatManager.PurgeCacheForBot(botId) (NEW)
            └── Directory.Delete(BotCache/{botId}, recursive)
            └── If was current → SetActiveBot(nextBot) or OnEmptyState(NoBotsExist)
       └── GameObject destroyed (existing)
```

## Files touched

### New files

- `Assets/Scripts/UI/BotSwitcherSheet.cs` — sheet controller (open/close, populate rows, dismiss on backdrop tap).
- `Assets/Scripts/UI/BotSwitcherRowView.cs` — row-prefab MonoBehaviour, binds bot data + selected state + tap.
- `Assets/Scripts/UI/EmptyStateView.cs` — single empty-state surface, configured by `EmptyStateReason`.
- `Assets/Editor/BotSwitcherSheetBuilder.cs` — Editor builder that constructs `Sheet_BotSwitcher` and the `BotSwitcherRow` prefab programmatically (per [.claude/rules/editor-scripts.md](../../../.claude/rules/editor-scripts.md) builder pattern).
- `Assets/Editor/Screen_WhatsappHeaderRebuilder.cs` — Editor builder that rebuilds the `Screen_Whatsapp` header to use the new tappable bot-switcher title.

### Modified files

- `Assets/Scripts/Main/ChatManager.cs` — bot-aware fetching, removal of hardcoded `profileId`, `CurrentBotId`/`SetActiveBot`/`PurgeCacheForBot`/`GetCacheRoot`, `OnActiveBotChanged`/`OnEmptyState` events, one-time migration on Awake.
- `Assets/Scripts/Chat/ChatHistoryCache.cs` — paths route through `ChatManager.Instance.GetCacheRoot()` (or accept a base directory parameter).
- `Assets/Scripts/Chat/MediaCacheManager.cs` — paths route through `ChatManager.Instance.GetCacheRoot()`.
- `Assets/Scripts/Main/Bot.cs` — `DeleteBot()` calls `ChatManager.PurgeCacheForBot(transform.name)`.
- `Assets/Scripts/UI/ChatListView.cs` — minor: hides itself when `EmptyStateView` is showing; subscribes/unsubscribes to `OnActiveBotChanged` for clean reset.

## Component boundaries (isolation check)

- **ChatManager** owns: active-bot state, cache root resolution, fetch orchestration, empty-state events. Does not own UI.
- **BotSwitcherSheet** owns: sheet open/close lifecycle, row instantiation. Calls `ChatManager.SetActiveBot`. Does not read cache or PlayerPrefs directly.
- **BotSwitcherRowView** owns: rendering one row's visual state. Does not call APIs or touch cache.
- **EmptyStateView** owns: rendering empty-state UI and dispatching the CTA button to the correct existing flow. Does not own cache or active-bot state.
- **Bot** owns: per-bot data and the delete flow. Calls `ChatManager.PurgeCacheForBot` as a one-line hook.
- **ChatHistoryCache / MediaCacheManager** own: file I/O for their respective concerns. The base directory is injected by `ChatManager.GetCacheRoot()` so the cache code stays oblivious to active-bot semantics.

This keeps the bot-aware-state in exactly one place (`ChatManager`) and gives every other component a single tiny seam.

## Error handling

- **Missing cache directory:** `PurgeCacheForBot` and `Directory.Delete` are wrapped in try/catch; missing directory is a no-op.
- **`whatsappProfileId` empty/null on active bot:** fetch is skipped; `OnEmptyState(BotHasNoWhatsApp)` fires.
- **`BotsParent.childCount == 0`:** all paths that try to activate a bot detect this and fire `OnEmptyState(NoBotsExist)` instead of throwing.
- **Migration failures (file delete throws):** caught and logged; flag still set so we don't re-attempt forever. Worst case: user has a few orphan files in `persistentDataPath` that are never read by the new code path.
- **Race: bot deleted while its chats are loading.** `Bot.DeleteBot()` calls `PurgeCacheForBot` synchronously; any in-flight coroutine should be stopped on `ChatManager.SetActiveBot` (existing pattern — `OnChatListCleared` already drives UI reset). New work: ensure existing fetch coroutines are cancellable and respect the active-bot id at completion time (drop result if `CurrentBotId` no longer matches the bot the fetch was started for).

## Testing plan

Manual smoke tests in Unity Editor and on device:

1. **Filter:** Two bots with separate WhatsApp accounts. Verify chats page shows only Bot0's chats when Bot0 is selected; switch to Bot1, verify only Bot1's chats render.
2. **Persistence:** Select Bot1, close app, reopen app — Bot1 is still selected.
3. **First-launch migration:** Pre-populate `persistentDataPath` with a fake `all_chats_cache.json` and `MediaCache/x.jpg`; launch the build once; verify they're gone and `BotCacheV1MigrationDone == 1`.
4. **Bot delete cleanup:** Create Bot0, sync chats, verify `BotCache/Bot0/` exists with files; delete Bot0; verify `BotCache/Bot0/` is gone.
5. **Delete current bot fallback:** Select Bot1, delete Bot1; verify Bot0 (or first remaining) becomes active and its chats render.
6. **Delete only bot:** Single-bot account; delete that bot; verify `EmptyStateView` shows "No bots yet" with working CTA to creation wizard.
7. **Empty WhatsApp:** Bot with no `whatsappProfileId` (e.g., new bot before connecting); verify "Connect WhatsApp" empty state with working CTA into `BotSettings` Auth tab.
8. **Sheet UX:** Open sheet, verify selected row highlight follows current bot, switching closes sheet and updates header.

## Open questions for implementation

- Confirm the exact GameObject hierarchy of the existing `Screen_Whatsapp` header so the rebuilder targets the right layer; will read scene at plan time.
- Confirm whether `GreenApiAvatarFetcher` exposes the active bot's avatar URL or if the bot avatar in the sheet falls back to initials in v1.
- Confirm the existing accent color value used elsewhere (e.g., the bottom-tab active state) so the selected-row tint and accent bar match the rest of the app.
