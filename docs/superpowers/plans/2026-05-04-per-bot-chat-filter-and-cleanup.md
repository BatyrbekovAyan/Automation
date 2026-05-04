# Per-Bot Chat Filter and Deletion Cleanup — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the WhatsApp chats page filter to the selected bot's account, add a header bot-switcher, and purge a bot's cached chats/messages/media when the bot is deleted.

**Architecture:** Cache moves from a flat global layout to per-bot subfolders rooted at `BotCache/{transform.name}/`. ChatManager owns active-bot state (`CurrentBotId`) and routes all cache I/O through `GetCacheRoot()`. The chats page header becomes a tappable bot-switcher that opens a bottom sheet. Bot deletion calls `ChatManager.PurgeCacheForBot` to wipe the bot's subtree.

**Tech Stack:** Unity 6 (6000.3.9f1), C#, URP, TextMeshProUGUI, DOTween, RoundedCorners, Newtonsoft.Json (already in project), UnityWebRequest with coroutines, PlayerPrefs.

**Spec:** [docs/superpowers/specs/2026-05-04-per-bot-chat-filter-and-cleanup-design.md](../specs/2026-05-04-per-bot-chat-filter-and-cleanup-design.md)

**Verification model:** This is a Unity project with no automated test suite. Each task has a manual verification step run inside the Unity Editor (compile + smoke test in Play mode, or executing a `[MenuItem]` for Editor scripts). Each task ends with a commit.

---

## File structure

**New files:**

| File | Responsibility |
|---|---|
| `Assets/Scripts/UI/EmptyStateView.cs` | Single empty-state surface; configures itself from `EmptyStateReason` and dispatches the CTA. |
| `Assets/Scripts/UI/BotSwitcherRowView.cs` | Single row in the bot-switcher sheet; binds bot data + selected state + tap. |
| `Assets/Scripts/UI/BotSwitcherSheet.cs` | Bottom-sheet controller; opens/closes, instantiates rows, dismisses on backdrop tap. |
| `Assets/Editor/BotSwitcherSheetBuilder.cs` | `[MenuItem]` builder that constructs `Sheet_BotSwitcher` + `BotSwitcherRow` prefab in the scene. |
| `Assets/Editor/Screen_WhatsappHeaderRebuilder.cs` | `[MenuItem]` builder that rewires `Screen_Whatsapp` header to be a tappable bot-switcher. |

**Modified files:**

| File | Change |
|---|---|
| `Assets/Scripts/Main/ChatManager.cs` | Adds `CurrentBotId`, `SetActiveBot`, `GetCacheRoot`, `PurgeCacheForBot`, `OnActiveBotChanged`, `OnEmptyState`, `EmptyStateReason`. Removes hardcoded `profileId`. Wires legacy-cache migration in `Awake`. Routes all cache I/O through `GetCacheRoot`. |
| `Assets/Scripts/Chat/ChatHistoryCache.cs` | `SaveHistory`/`LoadHistory` accept a `baseDir` parameter; build paths under `{baseDir}/messages/{chatId}.json`. |
| `Assets/Scripts/Chat/MediaCacheManager.cs` | `cacheDirectory` is computed lazily as `{ChatManager.Instance.GetCacheRoot()}/media`; create on first access. |
| `Assets/Scripts/Main/Bot.cs` | `DeleteBot()` calls `ChatManager.Instance.PurgeCacheForBot(transform.name)` before destroying GameObject. |
| `Assets/Scripts/Main/Manager.cs` | Adds `public Bot FindBotByName(string botName)` helper and exposes `BotsParent` via a public read-only accessor. |
| `Assets/Scripts/UI/ChatListView.cs` | Subscribes to `OnEmptyState` / `OnActiveBotChanged`; hides itself when an empty state is active, re-shows on bot change. |

**Order rationale:** Foundation first (ChatManager state, cache routing, lookup helper), then features that depend on it (active-bot fetch, empty states, deletion cleanup), then UI (sheet + header + builders), then end-to-end smoke.

---

## Task 1: Add `EmptyStateReason` enum and bot-state events to `ChatManager`

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

This is purely additive — no behavior change yet. Sets up the API surface that later tasks fill in.

- [ ] **Step 1: Read the current `ChatManager.cs` to confirm line layout.**

Open `Assets/Scripts/Main/ChatManager.cs`. Note the events block at lines 26-30 and the `State` block at lines 33-35.

- [ ] **Step 2: Add the `EmptyStateReason` enum and two new events.**

In `Assets/Scripts/Main/ChatManager.cs`, find the existing events block:

```csharp
    // Events
    public event Action<ChatViewModel> OnChatAdded;
    public event Action OnChatListCleared;
    public event Action<string> OnChatSelected;
    public event Action<List<MessageViewModel>, bool, bool> OnBatchMessagesLoaded;
    public event Action<List<MessageViewModel>> OnLiveMessagesReceived;
```

Append the two new events directly after `OnLiveMessagesReceived`:

```csharp
    // Events
    public event Action<ChatViewModel> OnChatAdded;
    public event Action OnChatListCleared;
    public event Action<string> OnChatSelected;
    public event Action<List<MessageViewModel>, bool, bool> OnBatchMessagesLoaded;
    public event Action<List<MessageViewModel>> OnLiveMessagesReceived;
    public event Action<string> OnActiveBotChanged;
    public event Action<EmptyStateReason> OnEmptyState;
```

At the very bottom of the file, after the closing brace of the `WappiSendTextResponse` class, add the enum:

```csharp
public enum EmptyStateReason
{
    NoBotsExist,
    BotHasNoWhatsApp,
}
```

- [ ] **Step 3: Compile in Unity Editor.**

Switch to the Unity Editor; let it auto-compile. Open the Console (Window → General → Console). Expected: zero compile errors. The new events have no subscribers yet, which is fine.

- [ ] **Step 4: Commit.**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): add OnActiveBotChanged/OnEmptyState events and EmptyStateReason enum"
```

---

## Task 2: Add `CurrentBotId` and `GetCacheRoot()` stubs to `ChatManager`

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

Adds the data members the rest of the plan needs. `CurrentBotId` is initialized to a stable sentinel (`"_default"`) so existing code paths still resolve to a single deterministic cache root until Task 6 reads the real value from PlayerPrefs.

- [ ] **Step 1: Add `using System.IO;` to `ChatManager.cs`.**

At the top of `Assets/Scripts/Main/ChatManager.cs`, ensure the `System.IO` using is present (the file already qualifies as `System.IO.File` and `System.IO.Path` inline; we'll use `Path` more, so add the using to keep code tidy):

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
```

- [ ] **Step 2: Add `CurrentBotId` field and `GetCacheRoot()` method.**

In `Assets/Scripts/Main/ChatManager.cs`, find the `// State` block:

```csharp
    // State
    public int currentPage = 1;
    private string currentChatId;
    private string profileId = "af80627e-6d9d";
```

Replace it with:

```csharp
    // State
    public int currentPage = 1;
    private string currentChatId;
    private string profileId = "af80627e-6d9d"; // TEMP: still used until Task 7; removed there.

    // Active-bot state
    public string CurrentBotId { get; private set; } = "_default";

    /// <summary>
    /// Per-bot cache root: {persistentDataPath}/BotCache/{CurrentBotId}/.
    /// Always exists after this call; safe for callers to write under.
    /// </summary>
    public string GetCacheRoot()
    {
        string path = Path.Combine(Application.persistentDataPath, "BotCache", CurrentBotId);
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }
```

- [ ] **Step 3: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 4: Verify directory creation by entering Play mode.**

Open Unity Editor → press Play → wait for the Main scene to initialize. In Finder/Explorer, navigate to `Application.persistentDataPath` (printed by Unity in Editor → e.g. `~/Library/Application Support/CompanyName/Automation` on macOS). Confirm `BotCache/_default/` was created. Exit Play mode.

If you can't find `persistentDataPath` directly, add a temporary `Debug.Log(Application.persistentDataPath);` to `ChatManager.Awake` and read the path from the Console; remove the log after verifying.

- [ ] **Step 5: Commit.**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): add CurrentBotId and GetCacheRoot() with sentinel default"
```

---

## Task 3: Route `ChatHistoryCache` paths through a `baseDir` parameter

**Files:**
- Modify: `Assets/Scripts/Chat/ChatHistoryCache.cs`
- Modify: `Assets/Scripts/Main/ChatManager.cs` (call sites)

Updates the static cache helper to accept a base directory and stores message JSON under `{baseDir}/messages/{chatId}.json`. All seven call sites in ChatManager pass `GetCacheRoot()`.

- [ ] **Step 1: Rewrite `ChatHistoryCache.cs` with `baseDir` parameters.**

Replace the entire content of `Assets/Scripts/Chat/ChatHistoryCache.cs` with:

```csharp
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ChatHistoryCache
{
    [System.Serializable]
    private class MessageListWrapper
    {
        public List<MessageViewModel> messages;
    }

    /// <summary>
    /// Saves a list of messages to {baseDir}/messages/{chatId}.json.
    /// </summary>
    public static void SaveHistory(string baseDir, string chatId, List<MessageViewModel> messages)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(chatId)) return;

        string messagesDir = Path.Combine(baseDir, "messages");
        if (!Directory.Exists(messagesDir)) Directory.CreateDirectory(messagesDir);

        string path = Path.Combine(messagesDir, $"{chatId}.json");

        MessageListWrapper wrapper = new MessageListWrapper { messages = messages };
        string json = JsonUtility.ToJson(wrapper);

        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads chat history from {baseDir}/messages/{chatId}.json.
    /// Returns an empty list if the file doesn't exist.
    /// </summary>
    public static List<MessageViewModel> LoadHistory(string baseDir, string chatId)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(chatId))
            return new List<MessageViewModel>();

        string path = Path.Combine(baseDir, "messages", $"{chatId}.json");

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            MessageListWrapper wrapper = JsonUtility.FromJson<MessageListWrapper>(json);

            if (wrapper != null && wrapper.messages != null)
            {
                return wrapper.messages;
            }
        }

        return new List<MessageViewModel>();
    }
}
```

- [ ] **Step 2: Update all `ChatHistoryCache` call sites in `ChatManager.cs`.**

There are seven call sites. Each one becomes `ChatHistoryCache.LoadHistory(GetCacheRoot(), chatId)` or `ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, messages)`.

Find each call and update:

**Call 1** (in `LoadMessagesForChat`, around line 198):

Replace:
```csharp
        List<MessageViewModel> cachedMessages = ChatHistoryCache.LoadHistory(chatId);
```
With:
```csharp
        List<MessageViewModel> cachedMessages = ChatHistoryCache.LoadHistory(GetCacheRoot(), chatId);
```

**Call 2** (inside `LoadMessagesForChat`'s `GetMessagesRoutine` callback, around line 214):

Replace:
```csharp
                if (newMessages.Count > 0) ChatHistoryCache.SaveHistory(chatId, newMessages);
```
With:
```csharp
                if (newMessages.Count > 0) ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, newMessages);
```

**Call 3** (in `SyncLatestMessages`, around line 283):

Replace:
```csharp
            ChatHistoryCache.SaveHistory(chatId, newMessages);
```
With:
```csharp
            ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, newMessages);
```

**Call 4, 5** (in `SendTextMessageRoutine`, around lines 650, 652):

Replace:
```csharp
    List<MessageViewModel> cachedList = ChatHistoryCache.LoadHistory(chatId);
    cachedList.Add(instantMessage);
    ChatHistoryCache.SaveHistory(chatId, cachedList);
```
With:
```csharp
    List<MessageViewModel> cachedList = ChatHistoryCache.LoadHistory(GetCacheRoot(), chatId);
    cachedList.Add(instantMessage);
    ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, cachedList);
```

**Call 6, 7** (in `SendTextMessageRoutine` post-server-success, around lines 682, 688):

Replace:
```csharp
            var cache = ChatHistoryCache.LoadHistory(chatId);
            var msg = cache.Find(m => m.messageId == tempId);
            if (msg != null)
            {
                msg.messageId = response.message_id;
                if (response.timestamp > 0) msg.timestamp = response.timestamp;
                ChatHistoryCache.SaveHistory(chatId, cache);
            }
```
With:
```csharp
            var cache = ChatHistoryCache.LoadHistory(GetCacheRoot(), chatId);
            var msg = cache.Find(m => m.messageId == tempId);
            if (msg != null)
            {
                msg.messageId = response.message_id;
                if (response.timestamp > 0) msg.timestamp = response.timestamp;
                ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, cache);
            }
```

- [ ] **Step 3: Search the rest of the codebase for any other `ChatHistoryCache.` calls and update them.**

Run from the project root:

```bash
grep -rn "ChatHistoryCache\." Assets/Scripts --include="*.cs"
```

Every match outside `ChatHistoryCache.cs` itself must use the new two-argument or three-argument form. If grep reports any unmatched call sites, update them to pass `ChatManager.Instance.GetCacheRoot()` (or `GetCacheRoot()` if called from inside ChatManager).

- [ ] **Step 4: Compile in Unity Editor.**

Console: zero errors. The compilation will fail loudly if any call site was missed.

- [ ] **Step 5: Smoke test in Play mode.**

Enter Play mode → open WhatsApp tab → tap a chat with prior history → confirm messages load from cache (instant, before any spinner). Send a test message → exit Play → re-enter Play → open the same chat → confirm the test message is still there (loaded from `BotCache/_default/messages/{chatId}.json`).

- [ ] **Step 6: Commit.**

```bash
git add Assets/Scripts/Chat/ChatHistoryCache.cs Assets/Scripts/Main/ChatManager.cs
git commit -m "refactor(chat): route ChatHistoryCache paths through ChatManager.GetCacheRoot()"
```

---

## Task 4: Route `MediaCacheManager` to a bot-rooted directory

**Files:**
- Modify: `Assets/Scripts/Chat/MediaCacheManager.cs`

`MediaCacheManager` had a fixed `cacheDirectory` set in `Awake`. Switch to a lazy accessor that asks `ChatManager.Instance.GetCacheRoot()` at call time, so per-bot media isolation works automatically once `CurrentBotId` becomes dynamic.

- [ ] **Step 1: Rewrite `MediaCacheManager.cs` with bot-rooted media directory.**

Replace the entire content of `Assets/Scripts/Chat/MediaCacheManager.cs` with:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public class MediaCacheManager : MonoBehaviour
{
    public static MediaCacheManager Instance;

    private const int MaxMemorySpriteCount = 100;
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, Sprite>>> spriteMemoryCache = new();
    private readonly LinkedList<KeyValuePair<string, Sprite>> spriteAccessOrder = new();

    // Memoized URL → cache-file path. Keyed by (botId, url) so a bot switch does not
    // serve another bot's file. Cleared on bot change.
    private readonly Dictionary<string, string> urlPathCache = new();
    private string cachedUrlBotId;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Per-bot media directory: {ChatManager.GetCacheRoot()}/media/.
    /// Created on first access.
    /// </summary>
    private string GetMediaDirectory()
    {
        string root = ChatManager.Instance != null
            ? ChatManager.Instance.GetCacheRoot()
            : Path.Combine(Application.persistentDataPath, "BotCache", "_default");

        string mediaDir = Path.Combine(root, "media");
        if (!Directory.Exists(mediaDir)) Directory.CreateDirectory(mediaDir);
        return mediaDir;
    }

    public Sprite GetSpriteFromMemory(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!spriteMemoryCache.TryGetValue(url, out var node)) return null;

        spriteAccessOrder.Remove(node);
        spriteAccessOrder.AddFirst(node);
        return node.Value.Value;
    }

    public void StoreSpriteInMemory(string url, Sprite sprite)
    {
        if (string.IsNullOrEmpty(url) || sprite == null) return;

        if (spriteMemoryCache.TryGetValue(url, out var existing))
        {
            spriteAccessOrder.Remove(existing);
            spriteAccessOrder.AddFirst(existing);
            return;
        }

        var node = new LinkedListNode<KeyValuePair<string, Sprite>>(
            new KeyValuePair<string, Sprite>(url, sprite));
        spriteAccessOrder.AddFirst(node);
        spriteMemoryCache[url] = node;

        while (spriteMemoryCache.Count > MaxMemorySpriteCount)
        {
            var tail = spriteAccessOrder.Last;
            spriteAccessOrder.RemoveLast();
            spriteMemoryCache.Remove(tail.Value.Key);
        }
    }

    public bool IsImageCached(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        string filePath = GetFilePathFromUrl(url);
        return File.Exists(filePath);
    }

    public void SaveImageToCache(string url, byte[] imageData)
    {
        if (string.IsNullOrEmpty(url) || imageData == null || imageData.Length == 0) return;

        string filePath = GetFilePathFromUrl(url);
        File.WriteAllBytesAsync(filePath, imageData);
    }

    public Texture2D LoadImageFromCache(string url)
    {
        if (!IsImageCached(url)) return null;

        string filePath = GetFilePathFromUrl(url);
        byte[] fileData = File.ReadAllBytes(filePath);

        Texture2D texture = new Texture2D(2, 2);
        if (texture.LoadImage(fileData))
        {
            return texture;
        }

        return null;
    }

    /// <summary>
    /// URL → MD5-hashed file path under the active bot's media directory.
    /// Memoization is invalidated when the active bot changes.
    /// </summary>
    public string GetFilePathFromUrl(string url)
    {
        string activeBotId = ChatManager.Instance != null ? ChatManager.Instance.CurrentBotId : "_default";

        if (cachedUrlBotId != activeBotId)
        {
            urlPathCache.Clear();
            cachedUrlBotId = activeBotId;
        }

        if (urlPathCache.TryGetValue(url, out var cachedPath)) return cachedPath;

        using MD5 md5 = MD5.Create();
        byte[] inputBytes = Encoding.UTF8.GetBytes(url);
        byte[] hashBytes = md5.ComputeHash(inputBytes);

        StringBuilder sb = new StringBuilder(hashBytes.Length * 2);
        for (int i = 0; i < hashBytes.Length; i++)
            sb.Append(hashBytes[i].ToString("X2"));

        string path = Path.Combine(GetMediaDirectory(), sb.ToString() + ".jpg");
        urlPathCache[url] = path;
        return path;
    }

    /// <summary>
    /// Clear the active bot's media cache. Used by ChatManager.PurgeCacheForBot
    /// when needed; routine deletion of a non-active bot wipes the directory directly.
    /// </summary>
    public void ClearCache()
    {
        string mediaDir = GetMediaDirectory();
        if (Directory.Exists(mediaDir))
        {
            DirectoryInfo dir = new DirectoryInfo(mediaDir);
            foreach (FileInfo file in dir.GetFiles())
            {
                file.Delete();
            }
        }
        urlPathCache.Clear();
    }
}
```

- [ ] **Step 2: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 3: Smoke test in Play mode.**

Enter Play mode → open WhatsApp tab → open a chat that has image messages → confirm the images load (downloading on first visit, instant on second). Exit Play mode → in Finder, confirm `BotCache/_default/media/` exists with `.jpg` files inside.

- [ ] **Step 4: Commit.**

```bash
git add Assets/Scripts/Chat/MediaCacheManager.cs
git commit -m "refactor(chat): root MediaCacheManager paths under per-bot cache directory"
```

---

## Task 5: Add `Manager.FindBotByName(string)` helper

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs`

ChatManager needs to look up a `Bot` instance by GameObject name to read its `whatsappProfileId`. `BotsParent` is private, so add a small accessor on `Manager`.

- [ ] **Step 1: Read Manager.cs around the BotsParent declaration.**

Open `Assets/Scripts/Main/Manager.cs` and locate line 20:

```csharp
    [SerializeField] private GameObject BotsParent;
```

- [ ] **Step 2: Add `BotsRoot` accessor and `FindBotByName` helper.**

Below the `BotsParent` field declaration (still at the top of the class, near the other field declarations), add:

```csharp
    /// <summary>Read-only access to the bots root transform. Used by ChatManager to enumerate bots.</summary>
    public Transform BotsRoot => BotsParent != null ? BotsParent.transform : null;

    /// <summary>
    /// Returns the Bot whose GameObject name matches botName, or null if not found.
    /// Bot names are "Bot0", "Bot1", etc. — they are persistent identifiers used for
    /// PlayerPrefs and per-bot cache directories.
    /// </summary>
    public Bot FindBotByName(string botName)
    {
        if (BotsParent == null || string.IsNullOrEmpty(botName)) return null;
        Transform t = BotsParent.transform.Find(botName);
        return t != null ? t.GetComponent<Bot>() : null;
    }
```

- [ ] **Step 3: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 4: Commit.**

```bash
git add Assets/Scripts/Main/Manager.cs
git commit -m "feat(manager): add BotsRoot accessor and FindBotByName helper"
```

---

## Task 6: `ChatManager.SetActiveBot` + last-selected persistence

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

Adds the method that switches the active bot, persists the choice, fires `OnActiveBotChanged`, and triggers a refresh. Also updates `Awake/Start` to read the persisted choice.

- [ ] **Step 1: Add `SetActiveBot` method to ChatManager.**

In `Assets/Scripts/Main/ChatManager.cs`, immediately after the `GetCacheRoot()` method (added in Task 2), add:

```csharp
    private const string LastSelectedBotPrefKey = "LastSelectedBotForChats";

    /// <summary>
    /// Switch the active bot. Persists the choice, fires OnActiveBotChanged,
    /// clears the current chat list, and triggers a fresh load.
    /// </summary>
    public void SetActiveBot(string botId)
    {
        if (string.IsNullOrEmpty(botId)) return;
        if (botId == CurrentBotId) return;

        CurrentBotId = botId;
        PlayerPrefs.SetString(LastSelectedBotPrefKey, botId);
        PlayerPrefs.Save();

        Chats.Clear();
        chatLookup.Clear();
        OnChatListCleared?.Invoke();
        OnActiveBotChanged?.Invoke(botId);

        StopAllCoroutines();
        BeginLoadForActiveBot();
    }

    /// <summary>
    /// Resolve the active bot's WhatsApp profile, then load cached chats and
    /// kick off a network sync. Fires OnEmptyState if the bot has no WhatsApp.
    /// </summary>
    private void BeginLoadForActiveBot()
    {
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
        if (bot == null || string.IsNullOrEmpty(bot.whatsappProfileId))
        {
            OnEmptyState?.Invoke(EmptyStateReason.BotHasNoWhatsApp);
            return;
        }

        string cachePath = Path.Combine(GetCacheRoot(), "chats.json");
        string cachedJson = "";
        if (File.Exists(cachePath))
        {
            cachedJson = File.ReadAllText(cachePath);
            ParseChatsJson(cachedJson, true);
        }

        StartCoroutine(SyncAllChats(cachePath, cachedJson));
    }
```

- [ ] **Step 2: Replace `Start()` body to read the persisted choice and call `BeginLoadForActiveBot`.**

Find the existing `Start()` method (lines ~42-56):

```csharp
    public void Start()
    {
        ShowChatList(true);
        
        string cachePath = System.IO.Path.Combine(Application.persistentDataPath, "all_chats_cache.json");
        string cachedJson = "";
        
        if (System.IO.File.Exists(cachePath))
        {
            cachedJson = System.IO.File.ReadAllText(cachePath);
            ParseChatsJson(cachedJson, true);
        }

        StartCoroutine(SyncAllChats(cachePath, cachedJson));
    }
```

Replace with:

```csharp
    public void Start()
    {
        ShowChatList(true);
        ResolveInitialActiveBot();
        BeginLoadForActiveBot();
    }

    /// <summary>
    /// Pick the active bot at startup: persisted choice if it still exists,
    /// otherwise the first bot, otherwise fire NoBotsExist.
    /// </summary>
    private void ResolveInitialActiveBot()
    {
        string saved = PlayerPrefs.GetString(LastSelectedBotPrefKey, "");
        if (!string.IsNullOrEmpty(saved) && Manager.Instance != null && Manager.Instance.FindBotByName(saved) != null)
        {
            CurrentBotId = saved;
            return;
        }

        Transform root = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (root != null && root.childCount > 0)
        {
            CurrentBotId = root.GetChild(0).name;
            PlayerPrefs.SetString(LastSelectedBotPrefKey, CurrentBotId);
            PlayerPrefs.Save();
            return;
        }

        OnEmptyState?.Invoke(EmptyStateReason.NoBotsExist);
    }
```

- [ ] **Step 3: Update `SyncAllChats` to use the per-bot cache file path.**

Find `SyncAllChats` (lines ~109-127). The cachePath is now passed in (already so) — but the URL still references the legacy `profileId`. We'll fix the URL in Task 7. The cachePath stays as the parameter, which `BeginLoadForActiveBot` now passes correctly. No code change needed in this step — confirm the method signature still accepts `cachePath` as the first arg, and the per-bot path now flows in.

- [ ] **Step 4: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 5: Smoke test — persistence.**

Enter Play mode. After the scene loads, in the Console (during a temporary `Debug.Log($"CurrentBotId: {ChatManager.Instance.CurrentBotId}");` call you can place in `BotsPage.Start` or anywhere convenient), confirm `CurrentBotId` resolves to the first bot (e.g. `"Bot0"`). Quit Play. Manually edit PlayerPrefs (via Unity Editor `Edit → Clear All PlayerPrefs` is destructive — instead use `PlayerPrefs.SetString("LastSelectedBotForChats", "Bot1")` from a temporary editor menu, or just rely on Task 11's UI test for full coverage).

For now, simpler verification: Play, confirm `BotCache/Bot0/` is now created (instead of `_default`) — this proves `CurrentBotId` resolved to a real bot name.

Remove any temporary `Debug.Log` after verification.

- [ ] **Step 6: Commit.**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): SetActiveBot + persisted last-selected bot startup resolution"
```

---

## Task 7: Replace hardcoded `profileId` with active bot's `whatsappProfileId`

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

`profileId` is used in four URL-builder spots: `SyncAllChats` (chats list), `SyncLatestMessages` (sync newest), `GetMessagesRoutine` (paginated history), `DownloadMediaRoutine` (media download), `SendTextMessageRoutine` (send). Each needs to use the active bot's `whatsappProfileId`. After this task, the hardcoded field is removed.

- [ ] **Step 1: Add a private `GetActiveProfileId()` helper.**

In `Assets/Scripts/Main/ChatManager.cs`, near `BeginLoadForActiveBot`, add:

```csharp
    /// <summary>
    /// Returns the active bot's WhatsApp profile ID, or null if missing.
    /// Coroutines guard on null and abort to avoid sending malformed requests.
    /// </summary>
    private string GetActiveProfileId()
    {
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
        return bot != null ? bot.whatsappProfileId : null;
    }
```

- [ ] **Step 2: Replace `profileId` in `SyncAllChats`.**

Find:
```csharp
        string url = $"https://wappi.pro/api/sync/chats/filter?profile_id={profileId}";
```

Replace with:
```csharp
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            OnEmptyState?.Invoke(EmptyStateReason.BotHasNoWhatsApp);
            yield break;
        }
        string url = $"https://wappi.pro/api/sync/chats/filter?profile_id={activeProfileId}";
```

- [ ] **Step 3: Replace `profileId` in `SyncLatestMessages`.**

Find:
```csharp
        string url = $"https://wappi.pro/api/sync/messages/get?profile_id={profileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset=0";
```

Replace with:
```csharp
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId)) yield break;
        string url = $"https://wappi.pro/api/sync/messages/get?profile_id={activeProfileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset=0";
```

- [ ] **Step 4: Replace `profileId` in `GetMessagesRoutine`.**

Find:
```csharp
        string url = $"https://wappi.pro/api/sync/messages/get?profile_id={profileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset={offset}";
```

Replace with:
```csharp
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            onComplete?.Invoke(new List<MessageViewModel>(), false);
            yield break;
        }
        string url = $"https://wappi.pro/api/sync/messages/get?profile_id={activeProfileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset={offset}";
```

- [ ] **Step 5: Replace `profileId` in `DownloadMediaRoutine`.**

Find:
```csharp
        string url = $"https://wappi.pro/api/sync/message/media/download?profile_id={profileId}&message_id={messageId}";
```

Replace with:
```csharp
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            onFailure?.Invoke();
            yield break;
        }
        string url = $"https://wappi.pro/api/sync/message/media/download?profile_id={activeProfileId}&message_id={messageId}";
```

- [ ] **Step 6: Replace `profileId` in `SendTextMessageRoutine`.**

Find:
```csharp
    string url = $"https://wappi.pro/api/sync/message/send?profile_id={profileId}";
```

Replace with:
```csharp
    string activeProfileId = GetActiveProfileId();
    if (string.IsNullOrEmpty(activeProfileId)) yield break;
    string url = $"https://wappi.pro/api/sync/message/send?profile_id={activeProfileId}";
```

- [ ] **Step 7: Remove the `profileId` field.**

Find and delete the line:
```csharp
    private string profileId = "af80627e-6d9d"; // TEMP: still used until Task 7; removed there.
```

- [ ] **Step 8: Compile in Unity Editor.**

Console: zero errors. Any leftover reference to `profileId` will cause a compile error and reveal a missed call site.

- [ ] **Step 9: Smoke test.**

Enter Play mode. Open WhatsApp tab. The selected bot's chats should now appear (assuming the bot has a valid `whatsappProfileId`). For a freshly-created bot with no WhatsApp connected yet, the `OnEmptyState(BotHasNoWhatsApp)` event fires (no UI handler exists yet — Task 12 wires it up — but the absence of crashes/errors confirms the guard works).

If you only have one bot and it has WhatsApp connected, you should see the same chats as before — but now keyed under `BotCache/{thatBot}/chats.json` rather than the legacy hardcoded ID.

- [ ] **Step 10: Commit.**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "refactor(chat): drive Wappi requests from active bot's whatsappProfileId"
```

---

## Task 8: One-time legacy cache migration on first launch

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

Wipes the old flat `all_chats_cache.json`, `chat_*.json`, and `MediaCache/` on first run after this update. Guarded by `BotCacheV1MigrationDone` PlayerPref so it runs exactly once.

- [ ] **Step 1: Add `MigrateLegacyCacheOnce()` to ChatManager.**

In `Assets/Scripts/Main/ChatManager.cs`, immediately below `GetCacheRoot()`, add:

```csharp
    private const string MigrationDoneKey = "BotCacheV1MigrationDone";

    /// <summary>
    /// One-time wipe of legacy flat cache files. Runs in Awake before any
    /// cache reads. Sets a PlayerPrefs flag to ensure single execution.
    /// </summary>
    private void MigrateLegacyCacheOnce()
    {
        if (PlayerPrefs.GetInt(MigrationDoneKey, 0) == 1) return;

        try
        {
            string root = Application.persistentDataPath;

            string legacyChatsList = Path.Combine(root, "all_chats_cache.json");
            if (File.Exists(legacyChatsList)) File.Delete(legacyChatsList);

            foreach (string legacyMessageFile in Directory.GetFiles(root, "chat_*.json", SearchOption.TopDirectoryOnly))
            {
                File.Delete(legacyMessageFile);
            }

            string legacyMediaDir = Path.Combine(root, "MediaCache");
            if (Directory.Exists(legacyMediaDir))
            {
                Directory.Delete(legacyMediaDir, recursive: true);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ChatManager] Legacy cache migration encountered an error: {e.Message}. Continuing.");
        }

        PlayerPrefs.SetInt(MigrationDoneKey, 1);
        PlayerPrefs.Save();
    }
```

- [ ] **Step 2: Call `MigrateLegacyCacheOnce` from `Awake`.**

Find:
```csharp
    public void Awake()
    {
        Instance = this;
    }
```

Replace with:
```csharp
    public void Awake()
    {
        Instance = this;
        MigrateLegacyCacheOnce();
    }
```

- [ ] **Step 3: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 4: Smoke test.**

Set up a clean migration scenario:
1. Quit Play mode if running.
2. In Finder, navigate to `Application.persistentDataPath`.
3. Manually create dummy files: `all_chats_cache.json` (any content), `chat_FAKE.json`, and a `MediaCache/` folder with one `dummy.jpg`.
4. In Unity, clear the flag with `Edit → Clear All PlayerPrefs` (or set `BotCacheV1MigrationDone` to 0 via a temporary one-line `PlayerPrefs.DeleteKey("BotCacheV1MigrationDone");` in a `[MenuItem]` script — but Clear All is fine for verification).
5. Enter Play mode.
6. Confirm the dummy files and `MediaCache/` folder are gone.
7. Confirm `BotCacheV1MigrationDone == 1` (re-check PlayerPrefs via `EditorPrefs Unity Window` or `PlayerPrefs.GetInt`).
8. Quit Play, re-create dummy files, enter Play again — they should remain (migration is one-shot).

If `Clear All PlayerPrefs` is too destructive (it nukes bot state), use a temporary editor menu item just to delete the migration key:

```csharp
#if UNITY_EDITOR
[UnityEditor.MenuItem("Tools/Reset Cache Migration Flag")]
static void ResetMigrationFlag() => PlayerPrefs.DeleteKey("BotCacheV1MigrationDone");
#endif
```

(You can put this in a temp file or in `ChatManager` itself behind `#if UNITY_EDITOR`. Remove after verifying.)

- [ ] **Step 5: Commit.**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): one-time migration of legacy flat cache on first launch"
```

---

## Task 9: `PurgeCacheForBot` in ChatManager

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

Deletes a bot's entire cache subtree. If the deleted bot is the active one, falls back to the next bot or fires `NoBotsExist`.

- [ ] **Step 1: Add `PurgeCacheForBot` to ChatManager.**

In `Assets/Scripts/Main/ChatManager.cs`, immediately below `MigrateLegacyCacheOnce`, add:

```csharp
    /// <summary>
    /// Deletes the cache subtree for a bot. If that bot was active, falls back
    /// to the first remaining bot or fires NoBotsExist. Called by Bot.DeleteBot.
    /// </summary>
    public void PurgeCacheForBot(string botId)
    {
        if (string.IsNullOrEmpty(botId)) return;

        try
        {
            string botCacheDir = Path.Combine(Application.persistentDataPath, "BotCache", botId);
            if (Directory.Exists(botCacheDir))
            {
                Directory.Delete(botCacheDir, recursive: true);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ChatManager] PurgeCacheForBot({botId}) failed: {e.Message}");
        }

        if (botId != CurrentBotId) return;

        // The active bot was deleted. Pick the next bot or empty out.
        Transform root = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        Transform next = null;
        if (root != null)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name != botId) { next = child; break; }
            }
        }

        if (next != null)
        {
            // SetActiveBot's early-return guard (`botId == CurrentBotId`) does not
            // fire here because next.name differs from the just-deleted bot we are
            // still nominally "on". SetActiveBot persists, clears list, and refreshes.
            SetActiveBot(next.name);
        }
        else
        {
            CurrentBotId = "_default";
            PlayerPrefs.DeleteKey(LastSelectedBotPrefKey);
            PlayerPrefs.Save();
            Chats.Clear();
            chatLookup.Clear();
            OnChatListCleared?.Invoke();
            OnEmptyState?.Invoke(EmptyStateReason.NoBotsExist);
        }
    }
```

- [ ] **Step 2: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 3: Smoke test (deferred to Task 10's smoke).**

`PurgeCacheForBot` has no caller yet. We'll trigger it through `Bot.DeleteBot` in Task 10 and verify there.

- [ ] **Step 4: Commit.**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): PurgeCacheForBot wipes cache subtree and falls back active bot"
```

---

## Task 10: Wire `Bot.DeleteBot` to call `PurgeCacheForBot`

**Files:**
- Modify: `Assets/Scripts/Main/Bot.cs`

One-line hook before the existing `Manager.DeleteProfilesAndWorkflows` call.

- [ ] **Step 1: Add the cache-purge call.**

In `Assets/Scripts/Main/Bot.cs`, find this block (lines ~180-187):

```csharp
            PlayerPrefs.DeleteKey(transform.name + "ServicesNumber");
        }

        Manager.Instance.DeleteProfilesAndWorkflows(whatsappProfileId, telegramProfileId, whatsappWorkflowId, telegramWorkflowId);

        Destroy(Manager.BotSettingsParentStatic.transform.GetChild(transform.GetSiblingIndex()).gameObject);
        Destroy(gameObject);
```

Replace with:

```csharp
            PlayerPrefs.DeleteKey(transform.name + "ServicesNumber");
        }

        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.PurgeCacheForBot(transform.name);
        }

        Manager.Instance.DeleteProfilesAndWorkflows(whatsappProfileId, telegramProfileId, whatsappWorkflowId, telegramWorkflowId);

        Destroy(Manager.BotSettingsParentStatic.transform.GetChild(transform.GetSiblingIndex()).gameObject);
        Destroy(gameObject);
```

- [ ] **Step 2: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 3: Smoke test deletion cleanup.**

1. Enter Play mode.
2. Create a bot or use an existing one (e.g. `Bot0`). Open the WhatsApp tab so its chats are fetched.
3. Exit Play mode briefly, in Finder navigate to `Application.persistentDataPath/BotCache/`. Confirm `Bot0/` exists with `chats.json` and likely `messages/` and `media/` subfolders.
4. Re-enter Play mode → BotsPage → tap delete on Bot0 → confirm.
5. Without exiting Play, check Finder: `BotCache/Bot0/` is gone.
6. Inside Play mode, the chats page should now show an empty UI (or default to whatever bot remains).

- [ ] **Step 4: Smoke test active-bot fallback.**

1. Add at least two bots if you don't have them.
2. Set Bot1 active by tapping it (we don't have UI yet — for this smoke, manually call `ChatManager.Instance.SetActiveBot("Bot1")` from a temp `[MenuItem]` or a `Debug.Log` in `BotsPage.Start`).
3. Delete Bot1.
4. Confirm via `Debug.Log(ChatManager.Instance.CurrentBotId)` that it falls back to `Bot0` (the next remaining bot).
5. Confirm `BotCache/Bot1/` was deleted.

Remove temporary debug code after verifying.

- [ ] **Step 5: Commit.**

```bash
git add Assets/Scripts/Main/Bot.cs
git commit -m "feat(bot): purge cache subtree on Bot.DeleteBot"
```

---

## Task 11: `EmptyStateView` MonoBehaviour

**Files:**
- Create: `Assets/Scripts/UI/EmptyStateView.cs`

Single empty-state surface driven by `EmptyStateReason`. Exposes serialized refs for icon, title, body, button. The runtime configures its content based on the reason.

- [ ] **Step 1: Create `Assets/Scripts/UI/EmptyStateView.cs`.**

The component must keep its GameObject active so it can stay subscribed to `ChatManager` events; it shows/hides the visual state via a `CanvasGroup` rather than `SetActive`.

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CanvasGroup))]
public class EmptyStateView : MonoBehaviour
{
    [Header("UI references")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI bodyLabel;
    [SerializeField] private Button primaryButton;
    [SerializeField] private TextMeshProUGUI primaryButtonLabel;

    [Header("Icons (drag in inspector)")]
    [SerializeField] private Sprite iconNoBots;
    [SerializeField] private Sprite iconNoWhatsApp;

    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        Hide();
    }

    private void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnEmptyState += HandleEmptyState;
            ChatManager.Instance.OnActiveBotChanged += HandleActiveBotChanged;
            ChatManager.Instance.OnChatAdded += HandleChatAdded;
        }
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnEmptyState -= HandleEmptyState;
            ChatManager.Instance.OnActiveBotChanged -= HandleActiveBotChanged;
            ChatManager.Instance.OnChatAdded -= HandleChatAdded;
        }
        if (primaryButton != null) primaryButton.onClick.RemoveAllListeners();
    }

    private void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    private void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void HandleEmptyState(EmptyStateReason reason)
    {
        ConfigureForReason(reason);
        Show();
    }

    private void HandleActiveBotChanged(string _) => Hide();

    private void HandleChatAdded(ChatViewModel _) => Hide();

    private void ConfigureForReason(EmptyStateReason reason)
    {
        switch (reason)
        {
            case EmptyStateReason.NoBotsExist:
                if (iconImage != null) iconImage.sprite = iconNoBots;
                if (titleLabel != null) titleLabel.text = "No bots yet";
                if (bodyLabel != null) bodyLabel.text = "Create your first bot to start managing chats.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Create your first bot";
                if (primaryButton != null)
                {
                    primaryButton.onClick.RemoveAllListeners();
                    primaryButton.onClick.AddListener(OpenCreateBotFlow);
                }
                break;

            case EmptyStateReason.BotHasNoWhatsApp:
                if (iconImage != null) iconImage.sprite = iconNoWhatsApp;
                if (titleLabel != null) titleLabel.text = "WhatsApp not connected";
                if (bodyLabel != null) bodyLabel.text = "Connect WhatsApp to this bot to see its chats.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Connect WhatsApp";
                if (primaryButton != null)
                {
                    primaryButton.onClick.RemoveAllListeners();
                    primaryButton.onClick.AddListener(OpenCurrentBotAuth);
                }
                break;
        }
    }

    private void OpenCreateBotFlow()
    {
        if (BotsPage.Instance != null)
        {
            BotsPage.Instance.gameObject.SetActive(true);
        }
    }

    private void OpenCurrentBotAuth()
    {
        if (ChatManager.Instance == null) return;
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(ChatManager.Instance.CurrentBotId) : null;
        if (bot == null) return;

        // Bot.EditButton is wired to the existing OpenSettings flow (parent activation
        // + slide-in animation). Invoking it avoids exposing OpenSettings publicly or
        // calling SendMessage by string name.
        if (bot.EditButton != null) bot.EditButton.onClick.Invoke();
    }
}
```

- [ ] **Step 2: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 3: Commit.**

```bash
git add Assets/Scripts/UI/EmptyStateView.cs
git commit -m "feat(ui): EmptyStateView component with NoBotsExist/BotHasNoWhatsApp presets"
```

(The component will be wired into the scene by Task 14's Editor builder, then verified end-to-end in Task 16.)

---

## Task 12: `BotSwitcherRowView` MonoBehaviour

**Files:**
- Create: `Assets/Scripts/UI/BotSwitcherRowView.cs`

Per-row view: bot name, sub-line, selected state highlight, tap handler.

- [ ] **Step 1: Create `Assets/Scripts/UI/BotSwitcherRowView.cs`.**

```csharp
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

public class BotSwitcherRowView : MonoBehaviour
{
    [Header("UI references")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI subLineLabel;
    [SerializeField] private Image statusDot;
    [SerializeField] private Image selectedBackground;
    [SerializeField] private Image selectedAccentBar;
    [SerializeField] private Button rowButton;

    [Header("Style")]
    [SerializeField] private Color statusConnectedColor = new Color(0.13f, 0.78f, 0.42f);
    [SerializeField] private Color statusDisconnectedColor = new Color(0.6f, 0.6f, 0.6f);
    [SerializeField] private Sprite avatarFallback;

    private string botId;
    private System.Action<string> onTap;

    public void Bind(Bot bot, bool isSelected, System.Action<string> tapHandler)
    {
        if (bot == null) return;

        botId = bot.transform.name;
        onTap = tapHandler;

        string botDisplayName = PlayerPrefs.GetString(botId + "Name", botId);
        if (nameLabel != null)
        {
            nameLabel.text = botDisplayName;
            nameLabel.fontStyle = isSelected ? FontStyles.Bold : FontStyles.Normal;
        }

        bool waConnected = !string.IsNullOrEmpty(bot.whatsappProfileId);
        if (subLineLabel != null)
        {
            subLineLabel.text = waConnected ? "WhatsApp connected" : "WhatsApp not connected";
        }
        if (statusDot != null)
        {
            statusDot.color = waConnected ? statusConnectedColor : statusDisconnectedColor;
        }

        if (avatarImage != null && avatarImage.sprite == null && avatarFallback != null)
        {
            avatarImage.sprite = avatarFallback;
        }

        if (selectedBackground != null) selectedBackground.gameObject.SetActive(isSelected);
        if (selectedAccentBar != null) selectedAccentBar.gameObject.SetActive(isSelected);

        if (rowButton != null)
        {
            rowButton.onClick.RemoveAllListeners();
            rowButton.onClick.AddListener(HandleTap);
        }
    }

    private void HandleTap()
    {
        if (string.IsNullOrEmpty(botId)) return;

        transform.DOPunchScale(Vector3.one * 0.04f, 0.18f, 1, 0.5f);
        onTap?.Invoke(botId);
    }

    private void OnDestroy()
    {
        if (rowButton != null) rowButton.onClick.RemoveAllListeners();
    }
}
```

- [ ] **Step 2: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 3: Commit.**

```bash
git add Assets/Scripts/UI/BotSwitcherRowView.cs
git commit -m "feat(ui): BotSwitcherRowView with selected highlight and tap feedback"
```

---

## Task 13: `BotSwitcherSheet` MonoBehaviour

**Files:**
- Create: `Assets/Scripts/UI/BotSwitcherSheet.cs`

Bottom-sheet controller. Owns the open/close animation, instantiates one row per bot under `BotsParent`, dispatches taps to `ChatManager.SetActiveBot`.

- [ ] **Step 1: Create `Assets/Scripts/UI/BotSwitcherSheet.cs`.**

```csharp
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class BotSwitcherSheet : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CanvasGroup backdropGroup;
    [SerializeField] private Button backdropButton;
    [SerializeField] private RectTransform sheetPanel;
    [SerializeField] private Transform rowContainer;
    [SerializeField] private BotSwitcherRowView rowPrefab;

    [Header("Animation")]
    [SerializeField] private float openDurationSeconds = 0.3f;
    [SerializeField] private float closeDurationSeconds = 0.25f;

    private bool isAnimating;
    private float panelHiddenY;
    private float panelShownY;

    private void Awake()
    {
        if (sheetPanel != null)
        {
            panelShownY = sheetPanel.anchoredPosition.y;
            // Hide below the screen by the sheet's height.
            panelHiddenY = panelShownY - (sheetPanel.rect.height > 0 ? sheetPanel.rect.height : 1200f);
            sheetPanel.anchoredPosition = new Vector2(sheetPanel.anchoredPosition.x, panelHiddenY);
        }
        if (backdropGroup != null)
        {
            backdropGroup.alpha = 0f;
            backdropGroup.blocksRaycasts = false;
        }
        if (backdropButton != null)
        {
            backdropButton.onClick.RemoveAllListeners();
            backdropButton.onClick.AddListener(Close);
        }
        gameObject.SetActive(false);
    }

    public void Open()
    {
        if (isAnimating) return;

        gameObject.SetActive(true);
        PopulateRows();

        isAnimating = true;
        if (backdropGroup != null)
        {
            backdropGroup.blocksRaycasts = true;
            backdropGroup.DOFade(0.4f, openDurationSeconds);
        }
        if (sheetPanel != null)
        {
            sheetPanel.DOAnchorPosY(panelShownY, openDurationSeconds)
                .SetEase(Ease.OutCubic)
                .OnComplete(() => isAnimating = false);
        }
        else
        {
            isAnimating = false;
        }
    }

    public void Close()
    {
        if (isAnimating) return;
        isAnimating = true;

        if (backdropGroup != null)
        {
            backdropGroup.DOFade(0f, closeDurationSeconds);
            backdropGroup.blocksRaycasts = false;
        }
        if (sheetPanel != null)
        {
            sheetPanel.DOAnchorPosY(panelHiddenY, closeDurationSeconds)
                .SetEase(Ease.InCubic)
                .OnComplete(() =>
                {
                    isAnimating = false;
                    gameObject.SetActive(false);
                });
        }
        else
        {
            isAnimating = false;
            gameObject.SetActive(false);
        }
    }

    private void PopulateRows()
    {
        if (rowContainer == null || rowPrefab == null) return;

        foreach (Transform existing in rowContainer)
        {
            Destroy(existing.gameObject);
        }

        Transform botsRoot = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (botsRoot == null) return;

        string activeBotId = ChatManager.Instance != null ? ChatManager.Instance.CurrentBotId : "";

        for (int i = 0; i < botsRoot.childCount; i++)
        {
            Bot bot = botsRoot.GetChild(i).GetComponent<Bot>();
            if (bot == null) continue;

            var row = Instantiate(rowPrefab, rowContainer);
            row.transform.localScale = Vector3.one;
            row.Bind(bot, isSelected: bot.transform.name == activeBotId, tapHandler: HandleRowTap);
        }
    }

    private void HandleRowTap(string botId)
    {
        if (ChatManager.Instance != null) ChatManager.Instance.SetActiveBot(botId);
        Close();
    }
}
```

- [ ] **Step 2: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 3: Commit.**

```bash
git add Assets/Scripts/UI/BotSwitcherSheet.cs
git commit -m "feat(ui): BotSwitcherSheet bottom-sheet controller"
```

---

## Task 14: Editor builder for `Sheet_BotSwitcher` and the row prefab

**Files:**
- Create: `Assets/Editor/BotSwitcherSheetBuilder.cs`

Programmatic constructor for the sheet GameObject hierarchy and the row prefab. Follows the project's `BotSettingsRebuilder` builder pattern. Run from `Tools → Bot Switcher → Build Sheet`.

- [ ] **Step 1: Create `Assets/Editor/BotSwitcherSheetBuilder.cs`.**

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class BotSwitcherSheetBuilder
{
    private const string SheetName = "Sheet_BotSwitcher";
    private const string RowName = "BotSwitcherRow";

    [MenuItem("Tools/Bot Switcher/Build Sheet")]
    public static void Build()
    {
        Canvas canvas = GameObject.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[BotSwitcherSheetBuilder] No Canvas found in scene. Open the Main scene first.");
            return;
        }

        // Remove existing instance
        Transform existing = canvas.transform.Find(SheetName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        // Root sheet object
        GameObject sheet = new GameObject(SheetName, typeof(RectTransform));
        sheet.transform.SetParent(canvas.transform, false);
        RectTransform sheetRT = sheet.GetComponent<RectTransform>();
        sheetRT.anchorMin = Vector2.zero;
        sheetRT.anchorMax = Vector2.one;
        sheetRT.offsetMin = Vector2.zero;
        sheetRT.offsetMax = Vector2.zero;

        BotSwitcherSheet controller = sheet.AddComponent<BotSwitcherSheet>();

        // Backdrop
        GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Button));
        backdrop.transform.SetParent(sheet.transform, false);
        RectTransform bdRT = backdrop.GetComponent<RectTransform>();
        bdRT.anchorMin = Vector2.zero;
        bdRT.anchorMax = Vector2.one;
        bdRT.offsetMin = Vector2.zero;
        bdRT.offsetMax = Vector2.zero;
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 1f); // alpha controlled by CanvasGroup
        var backdropCanvasGroup = backdrop.GetComponent<CanvasGroup>();
        backdropCanvasGroup.alpha = 0f;
        backdropCanvasGroup.blocksRaycasts = false;
        var backdropButton = backdrop.GetComponent<Button>();

        // Sheet panel
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(sheet.transform, false);
        RectTransform panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0f, 0f);
        panelRT.anchorMax = new Vector2(1f, 0f);
        panelRT.pivot = new Vector2(0.5f, 0f);
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta = new Vector2(0f, 720f); // dynamic in runtime via ContentSizeFitter
        panel.GetComponent<Image>().color = Color.white;

        var panelLayout = panel.GetComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(0, 0, 16, 16);
        panelLayout.spacing = 0;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        var panelFitter = panel.AddComponent<ContentSizeFitter>();
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Sheet header
        GameObject header = new GameObject("Header", typeof(RectTransform), typeof(TextMeshProUGUI));
        header.transform.SetParent(panel.transform, false);
        var headerText = header.GetComponent<TextMeshProUGUI>();
        headerText.text = "Select bot";
        headerText.fontSize = 16;
        headerText.fontStyle = FontStyles.Bold;
        headerText.color = new Color(0.1f, 0.1f, 0.1f);
        headerText.alignment = TextAlignmentOptions.Center;
        var headerLE = header.AddComponent<LayoutElement>();
        headerLE.minHeight = 56;
        headerLE.preferredHeight = 56;

        // Row container (vertical scroll viewport)
        GameObject scroll = new GameObject("RowScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scroll.transform.SetParent(panel.transform, false);
        var scrollImage = scroll.GetComponent<Image>();
        scrollImage.color = new Color(0, 0, 0, 0); // transparent viewport bg
        var scrollLE = scroll.AddComponent<LayoutElement>();
        scrollLE.minHeight = 320;
        scrollLE.preferredHeight = 480;

        var scrollRect = scroll.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scroll.transform, false);
        var viewportRT = viewport.GetComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;
        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 4;
        contentLayout.padding = new RectOffset(4, 4, 4, 4);
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        var contentFitter = content.GetComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRT;
        scrollRect.content = contentRT;

        // Row prefab
        GameObject row = BuildRowPrefab();

        // Wire controller serialized fields via SerializedObject
        var so = new SerializedObject(controller);
        so.FindProperty("backdropGroup").objectReferenceValue = backdropCanvasGroup;
        so.FindProperty("backdropButton").objectReferenceValue = backdropButton;
        so.FindProperty("sheetPanel").objectReferenceValue = panelRT;
        so.FindProperty("rowContainer").objectReferenceValue = contentRT;
        so.FindProperty("rowPrefab").objectReferenceValue = row.GetComponent<BotSwitcherRowView>();
        so.ApplyModifiedPropertiesWithoutUndo();

        sheet.SetActive(false);
        Debug.Log($"[BotSwitcherSheetBuilder] Built {SheetName} under {canvas.name}.");
        Selection.activeGameObject = sheet;
    }

    private static GameObject BuildRowPrefab()
    {
        // The row lives as a hidden template under the canvas; BotSwitcherSheet
        // instantiates it at runtime. We attach it under a special holder so it
        // does not render in the live scene.
        const string HolderName = "BotSwitcherRowPrefabHolder";
        Canvas canvas = GameObject.FindObjectOfType<Canvas>();
        Transform holder = canvas.transform.Find(HolderName);
        if (holder != null) Object.DestroyImmediate(holder.gameObject);

        GameObject holderGO = new GameObject(HolderName, typeof(RectTransform));
        holderGO.SetActive(false); // hides the template
        holderGO.transform.SetParent(canvas.transform, false);

        GameObject row = new GameObject(RowName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(holderGO.transform, false);
        RectTransform rowRT = row.GetComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, 64);
        var rowImage = row.GetComponent<Image>();
        rowImage.color = new Color(1, 1, 1, 0); // transparent base
        var rowButton = row.GetComponent<Button>();
        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(16, 16, 8, 8);
        rowLayout.spacing = 12;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;

        // Selected background (full-row, behind everything)
        GameObject selBg = new GameObject("SelectedBackground", typeof(RectTransform), typeof(Image));
        selBg.transform.SetParent(row.transform, false);
        var selBgRT = selBg.GetComponent<RectTransform>();
        selBgRT.anchorMin = Vector2.zero;
        selBgRT.anchorMax = Vector2.one;
        selBgRT.offsetMin = new Vector2(4, 0);
        selBgRT.offsetMax = new Vector2(-4, 0);
        var selBgImage = selBg.GetComponent<Image>();
        selBgImage.color = new Color(0.13f, 0.78f, 0.42f, 0.10f); // ~10% accent tint
        selBg.transform.SetAsFirstSibling(); // behind row content
        selBg.SetActive(false);

        // Selected accent bar (left edge)
        GameObject accentBar = new GameObject("SelectedAccentBar", typeof(RectTransform), typeof(Image));
        accentBar.transform.SetParent(row.transform, false);
        var barRT = accentBar.GetComponent<RectTransform>();
        barRT.anchorMin = new Vector2(0, 0);
        barRT.anchorMax = new Vector2(0, 1);
        barRT.pivot = new Vector2(0, 0.5f);
        barRT.sizeDelta = new Vector2(2, 0);
        barRT.anchoredPosition = new Vector2(4, 0);
        accentBar.GetComponent<Image>().color = new Color(0.13f, 0.78f, 0.42f);
        accentBar.SetActive(false);

        // Avatar
        GameObject avatar = new GameObject("Avatar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        avatar.transform.SetParent(row.transform, false);
        avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(40, 40);
        var avLE = avatar.GetComponent<LayoutElement>();
        avLE.preferredWidth = 40;
        avLE.preferredHeight = 40;
        var avImage = avatar.GetComponent<Image>();
        avImage.color = new Color(0.85f, 0.85f, 0.85f);

        // Stack: name + sub-line
        GameObject stack = new GameObject("Stack", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        stack.transform.SetParent(row.transform, false);
        var stackLayout = stack.GetComponent<VerticalLayoutGroup>();
        stackLayout.spacing = 2;
        stackLayout.childAlignment = TextAnchor.MiddleLeft;
        stackLayout.childForceExpandWidth = true;
        stackLayout.childForceExpandHeight = false;
        var stackLE = stack.GetComponent<LayoutElement>();
        stackLE.flexibleWidth = 1;

        GameObject nameGO = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(stack.transform, false);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = "Bot";
        nameText.fontSize = 16;
        nameText.color = new Color(0.1f, 0.1f, 0.1f);

        GameObject subGO = new GameObject("SubLine", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        subGO.transform.SetParent(stack.transform, false);
        var subLayout = subGO.GetComponent<HorizontalLayoutGroup>();
        subLayout.spacing = 6;
        subLayout.childAlignment = TextAnchor.MiddleLeft;
        subLayout.childForceExpandWidth = false;
        subLayout.childForceExpandHeight = false;

        GameObject statusDotGO = new GameObject("StatusDot", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        statusDotGO.transform.SetParent(subGO.transform, false);
        statusDotGO.GetComponent<RectTransform>().sizeDelta = new Vector2(8, 8);
        var dotLE = statusDotGO.GetComponent<LayoutElement>();
        dotLE.preferredWidth = 8;
        dotLE.preferredHeight = 8;
        var dotImage = statusDotGO.GetComponent<Image>();
        dotImage.color = new Color(0.6f, 0.6f, 0.6f);

        GameObject subTextGO = new GameObject("SubText", typeof(RectTransform), typeof(TextMeshProUGUI));
        subTextGO.transform.SetParent(subGO.transform, false);
        var subText = subTextGO.GetComponent<TextMeshProUGUI>();
        subText.text = "WhatsApp not connected";
        subText.fontSize = 12;
        subText.color = new Color(0.45f, 0.45f, 0.45f);

        // Wire up the BotSwitcherRowView component and its serialized fields.
        var rowView = row.AddComponent<BotSwitcherRowView>();
        var so = new SerializedObject(rowView);
        so.FindProperty("avatarImage").objectReferenceValue = avImage;
        so.FindProperty("nameLabel").objectReferenceValue = nameText;
        so.FindProperty("subLineLabel").objectReferenceValue = subText;
        so.FindProperty("statusDot").objectReferenceValue = dotImage;
        so.FindProperty("selectedBackground").objectReferenceValue = selBgImage;
        so.FindProperty("selectedAccentBar").objectReferenceValue = accentBar.GetComponent<Image>();
        so.FindProperty("rowButton").objectReferenceValue = rowButton;
        so.ApplyModifiedPropertiesWithoutUndo();

        return row;
    }
}
#endif
```

- [ ] **Step 2: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 3: Run the builder.**

Open the Main scene → menu `Tools → Bot Switcher → Build Sheet` → confirm in the Hierarchy that `Sheet_BotSwitcher` exists under the canvas with `Backdrop`, `Panel`, and `BotSwitcherRowPrefabHolder/BotSwitcherRow`. The BotSwitcherSheet component on the sheet should have all serialized fields wired (Inspector view).

Save the scene (Cmd-S / Ctrl-S).

- [ ] **Step 4: Smoke test the sheet open/close in isolation.**

Add a temporary `[MenuItem]` to open the sheet from the Editor:

```csharp
#if UNITY_EDITOR
[MenuItem("Tools/Bot Switcher/Open Sheet (test)")]
static void TestOpen()
{
    var sheet = GameObject.FindObjectOfType<BotSwitcherSheet>(includeInactive: true);
    if (sheet != null) sheet.Open();
}
#endif
```

You can put this in the same `BotSwitcherSheetBuilder.cs` file as a sibling method. Enter Play mode → run the menu item → sheet animates up, shows one row per bot (named "Bot0", "Bot1", etc.) → tap a row → sheet animates down → `ChatManager.CurrentBotId` is now the tapped bot. Confirm via temporary `Debug.Log` or by checking the cache directory created.

Remove the temporary `TestOpen` menu item once the wireup test passes (Task 15 binds it to the real header).

- [ ] **Step 5: Commit.**

```bash
git add Assets/Editor/BotSwitcherSheetBuilder.cs
git commit -m "feat(editor): BotSwitcherSheetBuilder constructs Sheet_BotSwitcher and row prefab"
```

(If you saved the scene with the new Sheet_BotSwitcher GameObject, also commit the scene change. Stage both files together.)

---

## Task 15: Bot-switcher header binder + Editor builder for `Screen_Whatsapp` header

**Files:**
- Create: `Assets/Scripts/UI/BotSwitcherTitleBinder.cs` (runtime)
- Create: `Assets/Editor/Screen_WhatsappHeaderRebuilder.cs` (editor)

The runtime binder owns the title's bot name, the avatar binding, and wires the Button's open-sheet listener at runtime. The Editor builder only constructs the GameObject hierarchy — no listener serialization, so we avoid the brittle `UnityEventTools` API.

- [ ] **Step 1: Inspect the existing header structure.**

In Unity, open the Main scene, locate `Screen_Whatsapp` in the Hierarchy, expand its children to find the existing header (likely a child named `Header` or `TopBar` or similar containing a `Title` text). Note the exact child path. The builder below assumes `Screen_Whatsapp/Header` — if your scene has a different path, change the `HeaderChildName` const in the builder.

- [ ] **Step 2: Create the runtime binder `Assets/Scripts/UI/BotSwitcherTitleBinder.cs`.**

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class BotSwitcherTitleBinder : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameLabel;

    private Button rowButton;

    private void Awake()
    {
        if (nameLabel == null)
        {
            Transform t = transform.Find("BotName");
            if (t != null) nameLabel = t.GetComponent<TextMeshProUGUI>();
        }

        rowButton = GetComponent<Button>();
        if (rowButton != null)
        {
            BotSwitcherSheet sheet = FindFirstObjectByType<BotSwitcherSheet>(FindObjectsInactive.Include);
            if (sheet != null)
            {
                rowButton.onClick.RemoveAllListeners();
                rowButton.onClick.AddListener(sheet.Open);
            }
        }
    }

    private void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnActiveBotChanged += UpdateTitle;
            UpdateTitle(ChatManager.Instance.CurrentBotId);
        }
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnActiveBotChanged -= UpdateTitle;
        }
    }

    private void UpdateTitle(string botId)
    {
        if (nameLabel == null) return;
        if (string.IsNullOrEmpty(botId)) { nameLabel.text = "Bot"; return; }
        nameLabel.text = PlayerPrefs.GetString(botId + "Name", botId);
    }
}
```

- [ ] **Step 3: Create the Editor builder `Assets/Editor/Screen_WhatsappHeaderRebuilder.cs`.**

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class Screen_WhatsappHeaderRebuilder
{
    private const string ScreenName = "Screen_Whatsapp";
    private const string HeaderChildName = "Header"; // adjust if scene differs
    private const string TitleName = "BotSwitcherTitle";

    [MenuItem("Tools/Bot Switcher/Rebuild Whatsapp Header")]
    public static void Rebuild()
    {
        GameObject screen = GameObject.Find(ScreenName);
        if (screen == null)
        {
            Debug.LogError($"[Screen_WhatsappHeaderRebuilder] Could not find {ScreenName} in the active scene. Open the Main scene.");
            return;
        }

        Transform header = screen.transform.Find(HeaderChildName);
        if (header == null)
        {
            Debug.LogError($"[Screen_WhatsappHeaderRebuilder] {ScreenName} has no child named '{HeaderChildName}'. " +
                $"Inspect the scene and update HeaderChildName in this builder.");
            return;
        }

        Transform existing = header.Find(TitleName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject root = new GameObject(TitleName,
            typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Button), typeof(Image));
        root.transform.SetParent(header, false);

        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(0.5f, 0.5f);
        rootRT.anchorMax = new Vector2(0.5f, 0.5f);
        rootRT.pivot = new Vector2(0.5f, 0.5f);
        rootRT.sizeDelta = new Vector2(240, 44); // 44dp touch target
        rootRT.anchoredPosition = Vector2.zero;

        // Invisible base — raycasts still hit because Image is present and the alpha is nonzero
        // (raycastTarget true on Image; alpha 0.001 keeps clicks alive without visual artifact).
        var rootImage = root.GetComponent<Image>();
        rootImage.color = new Color(1, 1, 1, 0.001f);
        rootImage.raycastTarget = true;

        var rootLayout = root.GetComponent<HorizontalLayoutGroup>();
        rootLayout.spacing = 8;
        rootLayout.childAlignment = TextAnchor.MiddleCenter;
        rootLayout.padding = new RectOffset(8, 8, 0, 0);
        rootLayout.childForceExpandWidth = false;
        rootLayout.childForceExpandHeight = false;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;

        // Avatar
        GameObject avatar = new GameObject("Avatar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        avatar.transform.SetParent(root.transform, false);
        avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(24, 24);
        var avLE = avatar.GetComponent<LayoutElement>();
        avLE.preferredWidth = 24;
        avLE.preferredHeight = 24;
        avatar.GetComponent<Image>().color = new Color(0.85f, 0.85f, 0.85f);

        // Name
        GameObject nameGO = new GameObject("BotName", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(root.transform, false);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = "Bot";
        nameText.fontSize = 18;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = new Color(0.1f, 0.1f, 0.1f);
        nameText.alignment = TextAlignmentOptions.Center;

        // Chevron (placeholder ▼ glyph; replace with a sprite when art arrives)
        GameObject chev = new GameObject("Chevron", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        chev.transform.SetParent(root.transform, false);
        var chevText = chev.GetComponent<TextMeshProUGUI>();
        chevText.text = "▼";
        chevText.fontSize = 12;
        chevText.color = new Color(0.45f, 0.45f, 0.45f);
        var chevLE = chev.GetComponent<LayoutElement>();
        chevLE.preferredWidth = 16;
        chevLE.preferredHeight = 16;

        // Add the runtime binder. It wires the Button.onClick at Awake by finding
        // BotSwitcherSheet in the scene — no Editor-time listener serialization.
        root.AddComponent<BotSwitcherTitleBinder>();

        // Sanity warning if the sheet isn't present yet.
        if (Object.FindFirstObjectByType<BotSwitcherSheet>(FindObjectsInactive.Include) == null)
        {
            Debug.LogWarning("[Screen_WhatsappHeaderRebuilder] No BotSwitcherSheet in scene — run 'Tools → Bot Switcher → Build Sheet' first, then re-run this builder.");
        }

        EditorUtility.SetDirty(root);
        Debug.Log("[Screen_WhatsappHeaderRebuilder] Whatsapp header rebuilt with bot-switcher title.");
        Selection.activeGameObject = root;
    }
}
#endif
```

- [ ] **Step 4: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 5: Run the builder.**

Menu `Tools → Bot Switcher → Rebuild Whatsapp Header`. Inspect the Hierarchy: `Screen_Whatsapp/Header/BotSwitcherTitle` should exist, with Avatar / BotName / Chevron children, a Button, and the `BotSwitcherTitleBinder` component.

Save the scene.

- [ ] **Step 6: Smoke test in Play mode.**

Enter Play mode → navigate to the WhatsApp tab → tap the title row in the header. The bot-switcher sheet slides up. Tap a different bot. The sheet animates down, the header title updates to the new bot's name, and the chats list refreshes (or shows the empty-state if the new bot has no WhatsApp connected).

- [ ] **Step 7: Commit.**

```bash
git add Assets/Scripts/UI/BotSwitcherTitleBinder.cs Assets/Editor/Screen_WhatsappHeaderRebuilder.cs
git commit -m "feat(ui): bot-switcher title in Whatsapp header (runtime binder + editor rebuilder)"
```

(Stage the saved scene file too, if its `.unity` shows up as modified.)

---

## Task 16: Wire `EmptyStateView` into `Screen_Whatsapp` and hide chat list when active

**Files:**
- Modify: `Assets/Scripts/UI/ChatListView.cs`

The `EmptyStateView` script exists (Task 11) but no GameObject in the scene uses it yet. We rely on a manual scene step to add the EmptyState GameObject (since this is a one-time placement; an Editor builder is overkill for a single placement). Then update `ChatListView` so it hides itself when the empty state is active.

- [ ] **Step 1: Manually add the EmptyState GameObject to `Screen_Whatsapp` in the scene.**

In Unity:
1. Open Main scene.
2. Right-click `Screen_Whatsapp` → `Create Empty` → name it `EmptyState`.
3. Add the `EmptyStateView` component to it (Add Component → Empty State View).
4. Build a child hierarchy by hand:
   - `Icon` (Image, 64x64).
   - `Title` (TextMeshProUGUI, 18sp Bold).
   - `Body` (TextMeshProUGUI, 14sp Regular).
   - `PrimaryButton` (Button + child TextMeshProUGUI for the label, 44dp tall).
5. Wire each child reference to the `EmptyStateView` component's serialized fields in the Inspector.
6. Drag in the two icon Sprites (`iconNoBots`, `iconNoWhatsApp`) — placeholder white squares are fine if no art exists yet; flag for art handoff in the PR.
7. Set the `EmptyState` GameObject inactive by default.
8. Save the scene.

(Yes — this manual step deviates from the builder pattern. Justification: the empty state is one GameObject in one location, with art that requires designer input. A builder would freeze in the wrong art.)

- [ ] **Step 2: Update `ChatListView.cs` to hide its content when an empty state is active.**

In `Assets/Scripts/UI/ChatListView.cs`, replace the entire file with:

```csharp
using System.Collections.Generic;
using UnityEngine;

public class ChatListView : MonoBehaviour
{
    [Header("Containers")]
    public Transform content;

    public ChatItemView prefab;

    private Dictionary<string, ChatItemView> itemsByChatId = new();

    void Start()
    {
        var manager = ChatManager.Instance;
        manager.OnChatAdded += AddChat;
        manager.OnChatListCleared += ClearChatList;
        manager.OnEmptyState += HandleEmptyState;
        manager.OnActiveBotChanged += HandleActiveBotChanged;

        foreach (var chat in manager.Chats)
            AddChat(chat);
    }

    void ClearChatList()
    {
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }
        itemsByChatId.Clear();
    }

    void AddChat(ChatViewModel vm)
    {
        // Real data came in — make sure our content panel is visible.
        if (content != null && !content.gameObject.activeSelf)
        {
            content.gameObject.SetActive(true);
        }

        var item = Instantiate(prefab, content);
        item.Bind(vm);
        itemsByChatId[vm.ChatId] = item;
        item.transform.SetAsLastSibling();
        item.transform.localScale = Vector3.one;

        // Row movement on update is handled inside ChatItemView.OnVmUpdated, which
        // unsubscribes itself in OnDestroy. Don't re-subscribe here — that leaks closures.
    }

    private void HandleEmptyState(EmptyStateReason _)
    {
        // The EmptyStateView surface activates itself; we just hide the list area.
        if (content != null)
        {
            content.gameObject.SetActive(false);
        }
    }

    private void HandleActiveBotChanged(string _)
    {
        if (content != null)
        {
            content.gameObject.SetActive(true);
        }
    }

    void OnDestroy()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatAdded -= AddChat;
            ChatManager.Instance.OnChatListCleared -= ClearChatList;
            ChatManager.Instance.OnEmptyState -= HandleEmptyState;
            ChatManager.Instance.OnActiveBotChanged -= HandleActiveBotChanged;
        }
    }
}
```

- [ ] **Step 3: Compile in Unity Editor.**

Console: zero errors.

- [ ] **Step 4: Smoke test empty states.**

1. **NoBotsExist:** Delete every bot. Open the WhatsApp tab. The chat list area is hidden; the EmptyState shows "No bots yet" with the "Create your first bot" button. Tap it → BotsPage appears (per `EmptyStateView.OpenCreateBotFlow`).
2. **BotHasNoWhatsApp:** Create a fresh bot but don't complete the WhatsApp QR auth. Open the WhatsApp tab. The chat list is hidden; the EmptyState shows "WhatsApp not connected" with the "Connect WhatsApp" button. Tap it → BotSettings opens for that bot (Auth tab is the first tab).
3. **Recovery:** With a connected bot active, the chat list area shows normal data and the EmptyState is hidden.

- [ ] **Step 5: Commit.**

```bash
git add Assets/Scripts/UI/ChatListView.cs
git commit -m "feat(ui): hide chat list when empty-state is active; restore on bot change"
```

(Also commit the scene file if it has the new EmptyState GameObject.)

---

## Task 17: End-to-end smoke test pass

**Files:** none — this is a verification task only.

Run through every test in the spec's Testing plan, making notes of any deviation. Fix issues inline; if a fix is large enough to warrant its own commit, commit it.

- [ ] **Step 1: Filter test.**

Two bots with separate WhatsApp accounts (Bot0, Bot1). Bot0 has chats from contacts A, B; Bot1 has chats from C, D. Open WhatsApp tab, confirm Bot0's chats (A, B) are visible. Open the bot-switcher sheet, tap Bot1, confirm only C, D are now visible.

- [ ] **Step 2: Persistence test.**

Select Bot1. Quit the app. Re-launch. Confirm Bot1 is still active (header shows Bot1's name; chats are C, D).

- [ ] **Step 3: First-launch migration test.**

This is harder to re-run on the same dev machine. Either:
- Test once in this iteration via the dummy-file recipe in Task 8 step 4, OR
- Skip if Task 8's smoke already verified this.

- [ ] **Step 4: Bot delete cleanup test.**

Create Bot0 → connect WhatsApp → sync chats → confirm `BotCache/Bot0/` exists with `chats.json`, `messages/`, `media/`. Delete Bot0. Confirm `BotCache/Bot0/` is gone. (Verified in Task 10's smoke; re-confirm here.)

- [ ] **Step 5: Delete current bot fallback test.**

Two bots. Bot1 is active. Delete Bot1. Confirm Bot0 becomes active and its chats render. Confirm `BotCache/Bot1/` is gone.

- [ ] **Step 6: Delete only bot test.**

Single-bot account. Delete that bot. Confirm `EmptyStateView` shows "No bots yet" and the CTA opens BotsPage.

- [ ] **Step 7: Empty WhatsApp test.**

Bot with no `whatsappProfileId` (skip the WA QR step on a fresh bot). Open WhatsApp tab. Confirm "WhatsApp not connected" empty state shows. Tap CTA, confirm BotSettings opens for that bot.

- [ ] **Step 8: Sheet UX test.**

Open sheet → confirm currently-selected bot has the highlight background and accent bar. Switch bots → highlight follows the new selection on next open. Tap backdrop → sheet closes.

- [ ] **Step 9: Final commit (notes / docs only — no source changes).**

If any verification revealed a missed edge case, that became its own commit. Otherwise, no commit needed for this task. Optionally update the spec's "Open questions" section if any of those got resolved during implementation:

```bash
# If you updated the spec:
git add docs/superpowers/specs/2026-05-04-per-bot-chat-filter-and-cleanup-design.md
git commit -m "docs(spec): close out open questions resolved during implementation"
```

---

## Final wrap-up

After Task 17, run:

```bash
git log --oneline main..HEAD
```

Confirm the commit chain reads sensibly start-to-finish. Then push the branch and open a PR linking the design doc.
