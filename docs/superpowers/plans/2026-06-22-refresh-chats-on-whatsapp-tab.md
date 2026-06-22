# Refresh Chat List on WhatsApp Tab Open — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Quietly re-sync the active bot's chat list every time the user navigates to the WhatsApp tab, without clearing the visible list.

**Architecture:** A new pure helper (`TabRefreshGate`) decides whether a tab switch should refresh. `BottomTabManager.SwitchTab` consults it and calls a new `ChatManager.RefreshActiveBotChats()`, which re-runs the existing background `SyncAllChats` path (quiet diff, no UI clear). A `_chatListSyncing` guard prevents overlapping syncs.

**Tech Stack:** Unity 6 (C# 9), `UnityWebRequest` coroutines, NUnit EditMode tests (compile into `Assembly-CSharp-Editor`, no asmdef).

**Spec:** [docs/superpowers/specs/2026-06-22-refresh-chats-on-whatsapp-tab-design.md](../specs/2026-06-22-refresh-chats-on-whatsapp-tab-design.md)

---

## Background the engineer needs

- The chat list is owned by `ChatManager` (a `[DefaultExecutionOrder(-100)]` singleton, `ChatManager.Instance`). It is a `partial class` split across `ChatManager.cs` and `ChatManager.BotState.cs`.
- `SyncAllChats(cachePath, cachedJson)` ([Assets/Scripts/Main/ChatManager.cs:332](../../../Assets/Scripts/Main/ChatManager.cs)) GETs `chats/filter` and, only when the response differs from `cachedJson`, writes the cache and calls `ParseChatsJson(newJson, false)`. The `false` means a **quiet background diff that does not clear the UI** — exactly the refresh behavior we want.
- The bottom nav is `BottomTabManager` (global namespace, `[DefaultExecutionOrder(100)]`). `SwitchTab(index)` toggles each tab's `screenPanel` and **early-returns when the tapped tab is already active**. Scene tab order: **0 Whatsapp**, 1 Telegram, 2 New, 3 Bots, 4 Profile (`defaultTabIndex: 0`, so the WhatsApp tab is also the startup tab).
- Pure decision helpers in this codebase are global-namespace `static` classes (e.g. `WhatsAppTabStateResolver` in `Assets/Scripts/Main/WhatsAppTabState.cs`) with matching NUnit tests in `Assets/Tests/Editor/Chat/` (e.g. `WhatsAppSyncTests.cs`). Follow that pattern exactly.

## Running EditMode tests

- **Editor closed:** `Tools/run-tests-headless.sh '<testFilter regex>'` — launches batch-mode Unity, parses NUnit3 results into `Tools/test-output/`. Omit the filter to run the whole suite.
- **Editor open (focused):** drop `Temp/claude/run-tests.trigger`, then read `Temp/claude/test-summary.json`.
- Either path requires the whole project to compile, so it also catches errors in the `ChatManager`/`BottomTabManager` edits below.

## New `.meta` files

New `.cs` files (`TabRefreshGate.cs`, `TabRefreshGateTests.cs`) need Unity to generate their `.meta` on import. Running the headless test script (or letting the open Editor recompile) generates them. Stage **both** the `.cs` and the generated `.meta` in every commit.

## File structure

- **Create** `Assets/Scripts/Main/TabRefreshGate.cs` — pure refresh-trigger decision.
- **Create** `Assets/Tests/Editor/Chat/TabRefreshGateTests.cs` — unit tests for it.
- **Modify** `Assets/Scripts/Main/ChatManager.cs` — add `_chatListSyncing` field + try/finally guard in `SyncAllChats`.
- **Modify** `Assets/Scripts/Main/ChatManager.BotState.cs` — add `RefreshActiveBotChats()`; reset `_chatListSyncing` in `SetActiveBot`.
- **Modify** `Assets/Scripts/Main/BottomTabManager.cs` — add `WhatsAppTabIndex` const + refresh call in `SwitchTab`.

---

## Task 1: `TabRefreshGate` pure helper + tests

**Files:**
- Create: `Assets/Scripts/Main/TabRefreshGate.cs`
- Test: `Assets/Tests/Editor/Chat/TabRefreshGateTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/TabRefreshGateTests.cs`:

```csharp
using NUnit.Framework;

public class TabRefreshGateTests
{
    const int WhatsAppTab = 0;

    [Test] public void WhatsAppTab_NotInitial_True()
        => Assert.IsTrue(TabRefreshGate.ShouldRefreshChats(WhatsAppTab, false, WhatsAppTab));

    [Test] public void WhatsAppTab_InitialSelection_False()
        => Assert.IsFalse(TabRefreshGate.ShouldRefreshChats(WhatsAppTab, true, WhatsAppTab));

    [Test] public void OtherTab_NotInitial_False()
        => Assert.IsFalse(TabRefreshGate.ShouldRefreshChats(3, false, WhatsAppTab));

    [Test] public void OtherTab_InitialSelection_False()
        => Assert.IsFalse(TabRefreshGate.ShouldRefreshChats(1, true, WhatsAppTab));

    [Test] public void NonZeroWhatsAppIndex_Matched()
        => Assert.IsTrue(TabRefreshGate.ShouldRefreshChats(2, false, 2));

    [Test] public void NonZeroWhatsAppIndex_OtherTab_False()
        => Assert.IsFalse(TabRefreshGate.ShouldRefreshChats(0, false, 2));
}
```

- [ ] **Step 2: Run the test to verify it fails (does not compile)**

Run: `Tools/run-tests-headless.sh 'TabRefreshGate'`
Expected: FAIL — compile error, `TabRefreshGate` does not exist.

- [ ] **Step 3: Write the minimal implementation**

Create `Assets/Scripts/Main/TabRefreshGate.cs`:

```csharp
/// <summary>
/// Pure decision helper for the bottom-nav chat-list refresh trigger. Kept
/// separate from BottomTabManager so the rule is unit-testable without a scene.
/// </summary>
public static class TabRefreshGate
{
    /// <summary>
    /// True when switching to <paramref name="newIndex"/> should quietly refresh
    /// the chat list: it must be the WhatsApp tab, and must not be the initial
    /// startup selection (ChatManager runs its own first load on launch).
    /// </summary>
    public static bool ShouldRefreshChats(int newIndex, bool isInitialSelection, int whatsAppTabIndex)
        => newIndex == whatsAppTabIndex && !isInitialSelection;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `Tools/run-tests-headless.sh 'TabRefreshGate'`
Expected: PASS — 6/6 tests green.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/TabRefreshGate.cs Assets/Scripts/Main/TabRefreshGate.cs.meta \
        Assets/Tests/Editor/Chat/TabRefreshGateTests.cs Assets/Tests/Editor/Chat/TabRefreshGateTests.cs.meta
git commit -m "feat(chat): add TabRefreshGate for WhatsApp-tab refresh trigger"
```

---

## Task 2: `ChatManager` — `_chatListSyncing` guard + `RefreshActiveBotChats()`

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` (`SyncAllChats`, new field)
- Modify: `Assets/Scripts/Main/ChatManager.BotState.cs` (`SetActiveBot`, new method)

> No new unit test: this path is a coroutine on a scene singleton touching files/PlayerPrefs, which the EditMode suite cannot drive. Verification is the project compiling and the full suite staying green (Step 4), plus the manual check in Task 3.

- [ ] **Step 1: Add the `_chatListSyncing` field**

In `Assets/Scripts/Main/ChatManager.cs`, immediately above the `SyncAllChats` method (line ~332), add the field:

```csharp
    /// <summary>
    /// True while a chats/filter sync is in flight. Guards RefreshActiveBotChats
    /// against launching overlapping syncs on rapid WhatsApp-tab re-entry. Reset
    /// in SetActiveBot because StopAllCoroutines abandons the coroutine without
    /// running its finally.
    /// </summary>
    private bool _chatListSyncing;
```

- [ ] **Step 2: Wrap `SyncAllChats` in a try/finally guard**

Replace the entire `SyncAllChats` method body in `Assets/Scripts/Main/ChatManager.cs` with:

```csharp
    IEnumerator SyncAllChats(string cachePath, string cachedJson)
    {
        _chatListSyncing = true;
        try
        {
            string activeProfileId = GetActiveProfileId();
            if (string.IsNullOrEmpty(activeProfileId))
            {
                OnEmptyState?.Invoke(EmptyStateReason.BotHasNoWhatsApp);
                yield break;
            }
            string url = $"https://wappi.pro/api/sync/chats/filter?profile_id={activeProfileId}";

            using UnityWebRequest www = UnityWebRequest.Get(url);
            www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
            www.timeout = 30;
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success) yield break;

            var text = www.downloadHandler.text;
            System.IO.File.WriteAllText(
                Application.persistentDataPath + "/response.txt",
                text
            );
            Debug.Log("Saved to: " + Application.persistentDataPath);

            string newJson = www.downloadHandler.text;

            if (newJson != cachedJson)
            {
                System.IO.File.WriteAllTextAsync(cachePath, newJson);
                ParseChatsJson(newJson, false); // FALSE = Background sync, DO NOT CLEAR THE UI!
            }
        }
        finally
        {
            _chatListSyncing = false;
        }
    }
```

Note: `yield return` / `yield break` are legal inside a `try` that has only a `finally` (no `catch`). The `finally` runs on normal completion and on `yield break`, but **not** when the coroutine is stopped — hence Step 3.

- [ ] **Step 3: Reset the guard in `SetActiveBot`**

In `Assets/Scripts/Main/ChatManager.BotState.cs`, in `SetActiveBot`, find the post-`StopAllCoroutines()` reset block (currently lines ~125-128):

```csharp
        StopAllCoroutines();        // also cancels in-flight thumbnail extraction coroutines
        _chatFetchesInFlight = 0;   // counter never decremented for the killed-mid-flight fetches
        ClearVideoThumbQueue();     // reset queue bookkeeping the cancelled coroutines never freed
        ClearMediaDownloadQueue();  // same for the serial media-download worker
```

Insert the guard reset right after the `_chatFetchesInFlight = 0;` line:

```csharp
        StopAllCoroutines();        // also cancels in-flight thumbnail extraction coroutines
        _chatFetchesInFlight = 0;   // counter never decremented for the killed-mid-flight fetches
        _chatListSyncing = false;   // a SyncAllChats killed mid-flight never runs its finally
        ClearVideoThumbQueue();     // reset queue bookkeeping the cancelled coroutines never freed
        ClearMediaDownloadQueue();  // same for the serial media-download worker
```

- [ ] **Step 4: Add `RefreshActiveBotChats()`**

In `Assets/Scripts/Main/ChatManager.BotState.cs`, add this method directly after `LoadChatsForActiveBot()` (it ends at line ~229, before `WaitForWhatsAppSyncRoutine`). `using System.IO;` is already present at the top of the file:

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

- [ ] **Step 5: Run the full suite to verify the project still compiles and is green**

Run: `Tools/run-tests-headless.sh`
Expected: PASS — whole EditMode suite green (includes Task 1's 6 tests). Any compile error in the edits above fails here.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs Assets/Scripts/Main/ChatManager.BotState.cs
git commit -m "feat(chat): add RefreshActiveBotChats with in-flight sync guard"
```

---

## Task 3: `BottomTabManager` — fire the refresh on WhatsApp-tab open

**Files:**
- Modify: `Assets/Scripts/Main/BottomTabManager.cs` (`WhatsAppTabIndex` const, `SwitchTab`)

- [ ] **Step 1: Add the `WhatsAppTabIndex` constant**

In `Assets/Scripts/Main/BottomTabManager.cs`, directly below the existing `BotsTabIndex` constant (line ~74), add:

```csharp
    /// <summary>
    /// Index of the WhatsApp (Chats) tab in the navigation bar. Navigating to it
    /// quietly re-syncs the chat list. Matches the scene tab order (0 = Whatsapp).
    /// </summary>
    public const int WhatsAppTabIndex = 0;
```

- [ ] **Step 2: Trigger the refresh in `SwitchTab`**

Replace the `SwitchTab` method body (lines ~134-153) with:

```csharp
    public void SwitchTab(int index)
    {
        if (!IsValidIndex(index))
        {
            Debug.LogWarning($"[BottomTabManager] SwitchTab({index}) is out of range. " +
                             $"Valid range: 0 – {tabs.Count - 1}.");
            return;
        }

        // Skip if this tab is already active (avoids redundant UI work)
        if (index == _activeTabIndex) return;

        bool isInitialSelection = _activeTabIndex == -1;
        _activeTabIndex = index;

        for (int i = 0; i < tabs.Count; i++)
        {
            bool isActive = (i == index);
            ApplyTabState(tabs[i], isActive);
        }

        // Quietly re-sync the chat list whenever the user navigates to the WhatsApp
        // tab so it stays fresh between bot switches. Skip the initial startup
        // selection — ChatManager runs its own first load on launch.
        if (TabRefreshGate.ShouldRefreshChats(index, isInitialSelection, WhatsAppTabIndex))
            ChatManager.Instance?.RefreshActiveBotChats();
    }
```

- [ ] **Step 3: Run the full suite to verify the project compiles and is green**

Run: `Tools/run-tests-headless.sh`
Expected: PASS — whole EditMode suite green. A namespace-resolution error on `ChatManager.Instance` (BottomTabManager is global namespace, so it should resolve) would surface here; if it does, qualify as `global::ChatManager.Instance`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/BottomTabManager.cs
git commit -m "feat(chat): refresh chat list when WhatsApp tab is opened"
```

---

## Task 3b: Refresh on return from messages panel (added post-approval)

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` (add `OnEnable`/`OnDisable`)

> No new unit test: pure event wiring. The "no startup double-sync" property is
> structural — `SwipeToBack.OnSlideOutComplete` only fires from the animated
> slide-out (`SnapToPosition` with `triggerBack: true`), which the startup
> `ShowChatList(true)` instant path never reaches. Verified by the full suite
> staying green + manual checks in Task 4.

- [ ] **Step 1: Subscribe `RefreshActiveBotChats` to the return-from-chat event**

In `Assets/Scripts/Main/ChatManager.cs`, immediately after the `Start()` method,
add (mirrors the existing `OnSlideOutComplete` subscribers — `MessageListView`,
`ReactionBarController`, `EmojiPickerController`):

```csharp
    void OnEnable()
    {
        // Returning from an open chat — back button OR swipe-back — fires
        // OnSlideOutComplete (both routes go through SnapToPosition with triggerBack).
        // Quietly re-sync the chat list so previews/unread reflect anything that changed
        // while the chat was open. The startup ShowChatList(true) uses the instant path,
        // which never raises this event, so there's no launch-time double sync.
        SwipeToBack.OnSlideOutComplete += RefreshActiveBotChats;
    }

    void OnDisable()
    {
        SwipeToBack.OnSlideOutComplete -= RefreshActiveBotChats;
    }
```

- [ ] **Step 2: Recompile and confirm the full suite stays green**

Recompile (`mcp__mcp-unity__recompile_scripts` / Assets Refresh) → 0 errors, then
run the full EditMode suite via the bridge. Expected: 555/555 pass.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): refresh chat list when returning from messages panel"
```

---

## Task 4: Manual verification (device or Editor Play mode)

Cannot be unit-tested; verify by hand.

- [ ] **Step 1: Verify refresh-on-open**

With a WhatsApp-connected bot active and past its sync window: switch to another tab (e.g. Bots), then back to the WhatsApp tab. Confirm the chat list updates quietly when the server differs from cache — **no list clear, no scroll reset, no spinner**.

- [ ] **Step 1b: Verify refresh-on-return-from-chat (Task 3b)**

Open a chat, then return to the list two ways: (a) the back button, and (b) the swipe-back gesture. Both should quietly re-sync the list (previews/unread reflect server) with no flash. Confirm a cold launch still fires only the single startup sync, not an extra one from this hook.

- [ ] **Step 2: Verify no startup double-sync**

Cold-launch the app (WhatsApp is the default tab). Confirm only one `chats/filter` request fires at startup (ChatManager's own load), not two. (Check the request log / `Debug.Log("Saved to: ...")` count.)

- [ ] **Step 3: Verify re-tap is a no-op**

While already on the WhatsApp tab, tap the WhatsApp icon again. Confirm nothing refreshes (SwitchTab early-returns).

- [ ] **Step 4: Verify empty / no-WhatsApp states are untouched**

With a bot that has no WhatsApp profile (or no bots), open the WhatsApp tab. Confirm the empty-state card shows unchanged and no request is sent.

---

## Self-review notes

- **Spec coverage:** RefreshActiveBotChats (§Design.1) → Task 2.4; `_chatListSyncing` guard (§Design.2) → Task 2.1-2.3; BottomTabManager trigger (§Design.3) → Task 3; `TabRefreshGate` (§Design.4) → Task 1. All four spec pieces mapped. Edge cases (startup, re-tap, no-WhatsApp, sync-window, rapid toggle, bot-switch reset) → Task 4 + the guards in Tasks 2-3.
- **Type consistency:** `TabRefreshGate.ShouldRefreshChats(int, bool, int)` defined in Task 1, called identically in Task 3. `RefreshActiveBotChats()` defined in Task 2.4, called in Task 3.2. `WhatsAppTabIndex` defined in Task 3.1, used in Task 3.2. `_chatListSyncing` defined in Task 2.1, used in Tasks 2.2/2.3/2.4.
- **No placeholders:** every code step shows complete code.
