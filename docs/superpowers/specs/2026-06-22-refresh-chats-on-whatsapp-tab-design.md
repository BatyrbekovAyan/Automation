# Refresh chat list when the WhatsApp tab is opened

**Date:** 2026-06-22
**Status:** Implemented (extended post-approval with a second trigger — see §Design.5)

## Problem

The chat list only re-syncs from the server when the active bot changes
(`ChatManager.SetActiveBot`). Navigating to the WhatsApp tab while staying on the
same bot shows whatever was last loaded — it can be stale. We want the list to
refresh more often: specifically, whenever the user taps the WhatsApp icon to
open the chats list.

## Goal

Trigger a **quiet background re-sync** of the active bot's chat list every time
the user navigates to the WhatsApp tab **or backs out of an open chat** (back
button / swipe-back) — no list clear, no spinner, no scroll reset. Rows only
change if the server response differs from the local cache.

## Non-goals

- No pull-to-refresh gesture.
- No periodic/timer-based polling.
- No refresh when re-tapping the tab you are already on (WhatsApp already open).
- No change to the bot-switch refresh path (`SetActiveBot`) beyond the guard
  bookkeeping noted below.

## Current behavior (as found)

- `ChatManager.SetActiveBot` ([ChatManager.BotState.cs:105](../../../Assets/Scripts/Main/ChatManager.BotState.cs))
  is the only refresh entry point: it clears the list, fires `OnActiveBotChanged`,
  `StopAllCoroutines()`, resets in-flight counters (`_chatFetchesInFlight = 0`),
  then `BeginLoadForActiveBot()` → `LoadChatsForActiveBot()` → `SyncAllChats()`.
- `SyncAllChats` ([ChatManager.cs:332](../../../Assets/Scripts/Main/ChatManager.cs))
  GETs `chats/filter`, and only if the response differs from the cached JSON
  writes the cache and calls `ParseChatsJson(newJson, false)` — `false` = quiet
  background diff that does **not** clear the UI. This is exactly the behavior we
  want for the tab-open refresh.
- The WhatsApp icon tap is handled by `BottomTabManager.SwitchTab(index)`
  ([BottomTabManager.cs:134](../../../Assets/Scripts/Main/BottomTabManager.cs)),
  which `SetActive`s each tab's `screenPanel`. It early-returns when the tapped
  tab is already active (`index == _activeTabIndex`).
- Scene tab order (from `Assets/Scenes/Main.unity`): **0 Whatsapp**, 1 Telegram,
  2 New, 3 Bots, 4 Profile. `defaultTabIndex: 0`, so the WhatsApp tab is also the
  startup tab. (`BottomTabManager.defaultTabIndex = 3` in code and its "Chats"
  comment are stale — the serialized scene value 0 wins.)

## Design

Three pieces.

### 1. `ChatManager.RefreshActiveBotChats()` — new public method

Lives next to `BeginLoadForActiveBot` in `ChatManager.BotState.cs`. A quiet
re-sync that reuses the existing background path:

```csharp
/// <summary>
/// Quietly re-sync the active bot's chat list against the server without
/// clearing the visible list. Called when the user navigates to the WhatsApp
/// tab so the list stays fresh between bot switches. No-ops when there is no
/// WhatsApp profile, the post-creation sync window is still open, or a
/// chat-list sync is already in flight.
/// </summary>
public void RefreshActiveBotChats()
{
    Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
    if (bot == null || !IsValidProfileId(bot.whatsappProfileId)) return; // empty card already shown
    if (IsWhatsAppSyncing(CurrentBotId, out _)) return;                  // syncing UI owns this case
    if (_chatListSyncing) return;                                        // collapse duplicate syncs

    string cachePath = Path.Combine(GetCacheRoot(), "chats.json");
    string cachedJson = File.Exists(cachePath) ? File.ReadAllText(cachePath) : "";
    StartCoroutine(SyncAllChats(cachePath, cachedJson));
}
```

- Reads the current `chats.json` purely as the diff baseline. It does **not**
  call `ParseChatsJson(_, true)`, so the visible list is never cleared.
- Bonus: if the initial load had failed and the list is empty, the unchanged-vs-
  empty-cache path still populates it via `ParseChatsJson(newJson, false)`.

### 2. `_chatListSyncing` re-entrancy guard on `SyncAllChats`

- New `private bool _chatListSyncing;` field in `ChatManager.cs` (where
  `SyncAllChats` lives).
- Wrap the `SyncAllChats` body in `try { _chatListSyncing = true; ... } finally
  { _chatListSyncing = false; }`. `yield return` is legal inside a try that has a
  `finally` (just not a `catch`), so the existing `yield return
  www.SendWebRequest()` and `yield break` paths are fine; `finally` runs on both
  normal completion and `yield break`.
- `StopCoroutine`/`StopAllCoroutines` abandon a coroutine **without** running its
  `finally`. `SetActiveBot` already accounts for this for `_chatFetchesInFlight`;
  add `_chatListSyncing = false;` on the same line block, right after
  `_chatFetchesInFlight = 0;`, so a sync killed mid-flight on bot switch does not
  leave the guard stuck `true`.

### 3. `BottomTabManager` triggers the refresh

- Add `public const int WhatsAppTabIndex = 0;` near the existing
  `BotsTabIndex = 3` constant, with a comment pointing at the scene tab order.
- In `SwitchTab`, capture whether this is the initial selection, then after the
  tab state is applied, fire the refresh when switching **to** the WhatsApp tab —
  but not on the very first selection at startup (ChatManager runs its own first
  load on launch, so refreshing there would be a redundant double-sync):

```csharp
public void SwitchTab(int index)
{
    if (!IsValidIndex(index)) { /* existing warning */ return; }
    if (index == _activeTabIndex) return;

    bool isInitialSelection = _activeTabIndex == -1;
    _activeTabIndex = index;

    for (int i = 0; i < tabs.Count; i++)
        ApplyTabState(tabs[i], i == index);

    if (TabRefreshGate.ShouldRefreshChats(index, isInitialSelection, WhatsAppTabIndex))
        ChatManager.Instance?.RefreshActiveBotChats();
}
```

- `ChatManager` is in the global namespace; `BottomTabManager` is in namespace
  `Main`. Confirm the reference resolves (use `global::ChatManager` only if
  needed).

### 4. `TabRefreshGate` — pure, testable decision helper

Mirrors the project's existing pure-logic helpers (`WhatsAppTabStateResolver`,
`WhatsAppSyncGate`, `ScrollFabMath`) so the trigger decision is unit-testable in
EditMode without a scene:

```csharp
public static class TabRefreshGate
{
    /// <summary>
    /// True when switching to <paramref name="newIndex"/> should quietly refresh
    /// the chat list: it must be the WhatsApp tab, and not the initial startup
    /// selection (ChatManager does its own first load then).
    /// </summary>
    public static bool ShouldRefreshChats(int newIndex, bool isInitialSelection, int whatsAppTabIndex)
        => newIndex == whatsAppTabIndex && !isInitialSelection;
}
```

Lives alongside the other resolver helpers (same folder as
`WhatsAppTabStateResolver`).

### 5. Second trigger — refresh on return from the messages panel

Added after the initial approval, on the same `RefreshActiveBotChats` machinery.

Both ways out of an open chat converge on one event:

- **Back button / programmatic** → `ChatManager.ShowChatList(false)` →
  `SwipeToBack.SlideOutToChatList(false)` → `SnapToPosition(screenWidth,
  triggerBack: true, …)` → fires `SwipeToBack.OnSlideOutComplete`.
- **Swipe-back gesture** → also `SnapToPosition(…, triggerBack: true, …)` → fires
  the same `OnSlideOutComplete`.
- **Startup** `ShowChatList(true)` takes the `instant: true` branch, which never
  calls `SnapToPosition`, so the event does **not** fire — no launch double-sync,
  structurally (same property the tab trigger relies on, by a different mechanism).

So `SwipeToBack.OnSlideOutComplete` (a `static event Action`) is the single
canonical "returned to the chat list" signal — already the established pattern,
with `MessageListView`, `ReactionBarController`, and `EmojiPickerController` all
subscribing to it in `OnEnable`/`OnDisable`. `ChatManager` subscribes
`RefreshActiveBotChats` to it the same way:

```csharp
void OnEnable()  => SwipeToBack.OnSlideOutComplete += RefreshActiveBotChats;
void OnDisable() => SwipeToBack.OnSlideOutComplete -= RefreshActiveBotChats;
```

`RefreshActiveBotChats`'s signature (`void()`) matches `System.Action`, so it
subscribes directly — no wrapper. All existing guards apply unchanged. No new
pure logic, so no new unit test; covered by the full suite staying green + manual
verification. (You cannot switch bottom tabs while a chat is open — the message
panel covers the tab bar — so this never races the tab-open trigger.)

## Data flow

```
User taps WhatsApp icon
  └─ BottomTabManager.SwitchTab(0)
       └─ TabRefreshGate.ShouldRefreshChats(0, isInitial, 0) == true
            └─ ChatManager.RefreshActiveBotChats()
                 ├─ guards: valid WhatsApp profile? not syncing? not already syncing?
                 └─ SyncAllChats(cachePath, currentCacheJson)   // _chatListSyncing gated
                      └─ chats/filter GET → if changed → ParseChatsJson(json, false)  // quiet diff, no clear
```

## Edge cases

- **Startup:** `SwitchTab(0)` fires with `_activeTabIndex == -1` →
  `ShouldRefreshChats` returns false → no double-sync with ChatManager's launch load.
- **Re-tap WhatsApp tab while on it:** `SwitchTab` early-returns at
  `index == _activeTabIndex` → no refresh (matches "open the chats list").
- **No WhatsApp on active bot / no bots:** `RefreshActiveBotChats` returns early;
  empty-state card stays as-is.
- **Inside post-creation sync window:** returns early; the existing syncing UI /
  wait-routine still owns the eventual load.
- **Rapid tab toggling:** `_chatListSyncing` collapses overlapping syncs into one
  in-flight request.
- **Bot switched mid-sync:** `SetActiveBot` resets `_chatListSyncing = false`
  after `StopAllCoroutines()`.

## Testing

- EditMode unit tests for `TabRefreshGate.ShouldRefreshChats`:
  - WhatsApp tab, not initial → true
  - WhatsApp tab, initial selection → false
  - non-WhatsApp tab (any) → false
  - boundary: a non-zero `whatsAppTabIndex` parameter still matched correctly
- Place in `Assets/Tests/Editor/Chat/` (compiles into `Assembly-CSharp-Editor`,
  no asmdef), following the existing pure-helper test files.
- Run via the test bridge / `Tools/run-tests-headless.sh`.
- Manual device/editor check: switch from another tab to WhatsApp → list updates
  quietly when the server differs, no flash; re-tapping WhatsApp does nothing.

## Files touched

- `Assets/Scripts/Main/ChatManager.BotState.cs` — add `RefreshActiveBotChats`,
  reset `_chatListSyncing` in `SetActiveBot`.
- `Assets/Scripts/Main/ChatManager.cs` — add `_chatListSyncing` field + try/finally
  guard in `SyncAllChats`; add `OnEnable`/`OnDisable` subscribing
  `RefreshActiveBotChats` to `SwipeToBack.OnSlideOutComplete` (§Design.5).
- `Assets/Scripts/Main/BottomTabManager.cs` — `WhatsAppTabIndex` const + refresh
  call in `SwitchTab`.
- New: `TabRefreshGate` pure helper (alongside `WhatsAppTabStateResolver`).
- New: EditMode test file for `TabRefreshGate`.
```
