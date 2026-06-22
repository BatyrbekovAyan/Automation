# Persistent soft-delete watermark — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a deleted chat stay deleted across bot switches/restarts and reappear when newer activity arrives, by replacing the session-only `DeletedChatGuard` with a persistent per-bot last-message-timestamp watermark.

**Architecture:** A pure decision function (`DeletedChatRule`) + a per-bot JSON store (`DeletedChatStore`, `deleted_chats.json` in `GetCacheRoot()`). `ParseChatsJson` consults the watermark to hide/show/adopt each chat; `ChatManager.DeleteChat` records the watermark on delete and removes it on rollback. `isDeleted` is read from the chat list to adopt externally-deleted chats.

**Tech Stack:** Unity 6 C#, JsonUtility (cache models), Newtonsoft (API body), NUnit EditMode tests, the in-Editor test bridge.

---

## Spec
[docs/superpowers/specs/2026-06-22-soft-delete-watermark-design.md](docs/superpowers/specs/2026-06-22-soft-delete-watermark-design.md)

## Notes for the executor
- **Unity TDD wrinkle:** a test referencing a not-yet-existing type is a *compile error* that blocks the whole EditMode assembly. So: create a compiling stub returning a wrong value → write the test (fails on assertion) → implement → passes.
- **Editor is OPEN** → the headless script is blocked; use the in-Editor bridge to run tests / gate compilation:
  1. `mkdir -p Temp/claude && rm -f Temp/claude/test-summary.json Temp/claude/run-tests.trigger`
  2. `printf '<ClassNameRegex>' > Temp/claude/run-tests.trigger`
  3. Poll (Bash timeout 600000): `for i in $(seq 1 180); do s=$(cat Temp/claude/test-summary.json 2>/dev/null); echo "$s" | grep -qE '"status": "(completed|error)"' && { echo "$s"; break; }; sleep 3; done`
  4. `overall:"Passed"` = green (and project compiled). `overall:"CompilationFailed"` = fix + re-trigger. Running an existing test class (e.g. `WappiRecipientTests`) is the compile gate for non-test tasks.
- Every new `.cs` commit includes its generated `.cs.meta` (stage both). Work on `main`. The tree has unrelated unstaged changes — stage ONLY the files each task lists.

## File structure
- **New:** `Assets/Scripts/Chat/DeletedChatRule.cs`, `Assets/Scripts/Chat/DeletedChatStore.cs`, tests `Assets/Tests/Editor/Chat/DeletedChatRuleTests.cs`, `Assets/Tests/Editor/Chat/DeletedChatStoreTests.cs`.
- **Modify:** `Assets/Scripts/Chat/ChatDialog.cs` (+`isDeleted`), `Assets/Scripts/Main/ChatManager.DeleteChat.cs` (rewrite), `Assets/Scripts/Main/ChatManager.cs` (`ParseChatsJson`), `Assets/Scripts/Main/ChatManager.BotState.cs` (load watermarks, drop `ClearAll`).
- **Delete:** `Assets/Scripts/Chat/DeletedChatGuard.cs`(+meta+tests), `Assets/Scripts/Chat/ChatListCacheEditor.cs`(+meta+tests).

---

## Task 1: `DeletedChatRule` — pure hide/adopt decision

**Files:** Create `Assets/Scripts/Chat/DeletedChatRule.cs`; Test `Assets/Tests/Editor/Chat/DeletedChatRuleTests.cs`

- [ ] **Step 1: Compiling stub**
```csharp
// Assets/Scripts/Chat/DeletedChatRule.cs
public static class DeletedChatRule
{
    public static bool ShouldHide(bool hasWatermark, long watermark, long ts, bool isDeleted, out long adoptWatermark)
    {
        adoptWatermark = -1; return false; // stub
    }
}
```

- [ ] **Step 2: Failing test**
```csharp
// Assets/Tests/Editor/Chat/DeletedChatRuleTests.cs
using NUnit.Framework;

public class DeletedChatRuleTests
{
    [Test] public void FreshDelete_SameTimestamp_Hides()
    {
        bool hide = DeletedChatRule.ShouldHide(true, 100, 100, false, out long adopt);
        Assert.IsTrue(hide); Assert.AreEqual(-1, adopt);
    }

    [Test] public void NoNewActivity_OlderTimestamp_Hides()
        => Assert.IsTrue(DeletedChatRule.ShouldHide(true, 100, 90, true, out _));

    [Test] public void Revived_NewerTimestamp_Shows()
    {
        bool hide = DeletedChatRule.ShouldHide(true, 100, 150, true, out long adopt);
        Assert.IsFalse(hide); Assert.AreEqual(-1, adopt);
    }

    [Test] public void ExternalDelete_NoWatermark_HidesAndAdopts()
    {
        bool hide = DeletedChatRule.ShouldHide(false, 0, 100, true, out long adopt);
        Assert.IsTrue(hide); Assert.AreEqual(100, adopt);
    }

    [Test] public void NeverDeleted_Shows()
    {
        bool hide = DeletedChatRule.ShouldHide(false, 0, 100, false, out long adopt);
        Assert.IsFalse(hide); Assert.AreEqual(-1, adopt);
    }

    [Test] public void ZeroTimestamp_WithZeroWatermark_Hides()
        => Assert.IsTrue(DeletedChatRule.ShouldHide(true, 0, 0, false, out _));
}
```

- [ ] **Step 3: Run — expect FAIL** — `printf 'DeletedChatRuleTests' > Temp/claude/run-tests.trigger` (+poll). Expected: the "Hides"/"Adopts" cases fail (stub returns false/-1).

- [ ] **Step 4: Implement**
```csharp
public static class DeletedChatRule
{
    /// <summary>
    /// Decide whether a chat should be hidden given its per-bot deletion watermark and current
    /// last-message timestamp. A watermarked chat hides while ts is not newer than the watermark.
    /// A chat with no watermark that Wappi reports isDeleted is hidden and adopted (adoptWatermark
    /// = ts) so future newer activity revives it. Everything else shows.
    /// </summary>
    public static bool ShouldHide(bool hasWatermark, long watermark, long ts, bool isDeleted, out long adoptWatermark)
    {
        if (hasWatermark) { adoptWatermark = -1; return ts <= watermark; }
        if (isDeleted)    { adoptWatermark = ts; return true; }
        adoptWatermark = -1; return false;
    }
}
```

- [ ] **Step 5: Run — expect PASS** (6 passed).

- [ ] **Step 6: Commit**
```bash
git add Assets/Scripts/Chat/DeletedChatRule.cs Assets/Scripts/Chat/DeletedChatRule.cs.meta \
        Assets/Tests/Editor/Chat/DeletedChatRuleTests.cs Assets/Tests/Editor/Chat/DeletedChatRuleTests.cs.meta
git commit -m "feat(chat): add DeletedChatRule soft-delete visibility decision"
```

---

## Task 2: `DeletedChatStore` — per-bot watermark persistence

**Files:** Create `Assets/Scripts/Chat/DeletedChatStore.cs`; Test `Assets/Tests/Editor/Chat/DeletedChatStoreTests.cs`

- [ ] **Step 1: Compiling stub**
```csharp
// Assets/Scripts/Chat/DeletedChatStore.cs
using System.Collections.Generic;

public static class DeletedChatStore
{
    public static Dictionary<string, long> Load(string cacheRoot) => new Dictionary<string, long>(); // stub
    public static void Save(string cacheRoot, Dictionary<string, long> map) { }
}
```

- [ ] **Step 2: Failing test**
```csharp
// Assets/Tests/Editor/Chat/DeletedChatStoreTests.cs
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class DeletedChatStoreTests
{
    private string _root;

    [SetUp] public void SetUp()
    {
        _root = Path.Combine(Application.temporaryCachePath, "delstore_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    [TearDown] public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test] public void RoundTrips()
    {
        var map = new Dictionary<string, long> { { "a@c.us", 100 }, { "b@c.us", 200 } };
        DeletedChatStore.Save(_root, map);
        var loaded = DeletedChatStore.Load(_root);
        Assert.AreEqual(2, loaded.Count);
        Assert.AreEqual(100, loaded["a@c.us"]);
        Assert.AreEqual(200, loaded["b@c.us"]);
    }

    [Test] public void MissingFileIsEmpty()
        => Assert.AreEqual(0, DeletedChatStore.Load(_root).Count);

    [Test] public void CorruptFileIsEmpty()
    {
        File.WriteAllText(Path.Combine(_root, "deleted_chats.json"), "not json");
        Assert.AreEqual(0, DeletedChatStore.Load(_root).Count);
    }

    [Test] public void NullRootIsSafe()
    {
        Assert.AreEqual(0, DeletedChatStore.Load(null).Count);
        Assert.DoesNotThrow(() => DeletedChatStore.Save(null, new Dictionary<string, long> { { "a", 1 } }));
    }
}
```

- [ ] **Step 3: Run — expect FAIL** — `printf 'DeletedChatStoreTests' > …trigger`. Expected: `RoundTrips` fails (stub Save no-ops; Load returns empty).

- [ ] **Step 4: Implement**
```csharp
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists the per-bot soft-delete watermark map (chatId -> last-message unix at deletion) to
/// {cacheRoot}/deleted_chats.json. Null/empty/corrupt-safe; never throws. Mirrors ChatHistoryCache.
/// </summary>
public static class DeletedChatStore
{
    private const string FileName = "deleted_chats.json";

    [System.Serializable] private class Entry { public string id; public long ts; }
    [System.Serializable] private class Wrapper { public List<Entry> entries = new List<Entry>(); }

    public static Dictionary<string, long> Load(string cacheRoot)
    {
        var map = new Dictionary<string, long>();
        if (string.IsNullOrEmpty(cacheRoot)) return map;

        string path = Path.Combine(cacheRoot, FileName);
        if (!File.Exists(path)) return map;

        try
        {
            var wrapper = JsonUtility.FromJson<Wrapper>(File.ReadAllText(path));
            if (wrapper?.entries != null)
                foreach (var e in wrapper.entries)
                    if (e != null && !string.IsNullOrEmpty(e.id)) map[e.id] = e.ts;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DeletedChatStore] Corrupt {path}: {ex.Message}. Treating as empty.");
        }
        return map;
    }

    public static void Save(string cacheRoot, Dictionary<string, long> map)
    {
        if (string.IsNullOrEmpty(cacheRoot) || map == null) return;

        var wrapper = new Wrapper();
        foreach (var kvp in map) wrapper.entries.Add(new Entry { id = kvp.Key, ts = kvp.Value });

        try
        {
            if (!Directory.Exists(cacheRoot)) Directory.CreateDirectory(cacheRoot);
            string path = Path.Combine(cacheRoot, FileName);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(wrapper));
            if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
            else File.Move(tmp, path);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DeletedChatStore] Failed to save to {cacheRoot}: {ex.Message}");
        }
    }
}
```

- [ ] **Step 5: Run — expect PASS** (4 passed).

- [ ] **Step 6: Commit**
```bash
git add Assets/Scripts/Chat/DeletedChatStore.cs Assets/Scripts/Chat/DeletedChatStore.cs.meta \
        Assets/Tests/Editor/Chat/DeletedChatStoreTests.cs Assets/Tests/Editor/Chat/DeletedChatStoreTests.cs.meta
git commit -m "feat(chat): add DeletedChatStore per-bot watermark persistence"
```

---

## Task 3: Add `isDeleted` to `ChatDialog`

**Files:** Modify `Assets/Scripts/Chat/ChatDialog.cs`

- [ ] **Step 1: Add the field.** Find `public bool isArchived;` and add after it:
```csharp
    public bool isDeleted;
```

- [ ] **Step 2: Compile-verify** — `printf 'WappiRecipientTests' > …trigger` (+poll). Expected `overall:"Passed"`.

- [ ] **Step 3: Commit**
```bash
git add Assets/Scripts/Chat/ChatDialog.cs
git commit -m "feat(chat): add isDeleted to ChatDialog model"
```

---

## Task 4: Swap the guard for the watermark (coordinated, must compile together)

This replaces `DeletedChatGuard` usage across three files in one atomic change. `DeletedChatGuard.cs` and `ChatListCacheEditor.cs` still exist (unused) after this task — they're deleted in Task 5.

**Files:** Rewrite `Assets/Scripts/Main/ChatManager.DeleteChat.cs`; Modify `Assets/Scripts/Main/ChatManager.cs`; Modify `Assets/Scripts/Main/ChatManager.BotState.cs`

- [ ] **Step 1: Rewrite `ChatManager.DeleteChat.cs`** to exactly:
```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    /// <summary>Fired after a chat is removed from the in-memory list (optimistic delete, server sync, or rollback re-add via OnChatAdded).</summary>
    public event Action<string> OnChatRemoved;

    // Per-bot soft-delete watermarks: chatId -> the chat's last-message unix timestamp at deletion.
    // Loaded in LoadChatsForActiveBot, consulted in ParseChatsJson (DeletedChatRule), persisted via
    // DeletedChatStore. A deleted chat stays hidden until a newer message arrives (then it revives).
    private Dictionary<string, long> _deletedWatermarks = new Dictionary<string, long>();

    /// <summary>
    /// Optimistically removes the chat + its message cache, records a deletion watermark, then
    /// soft-deletes it on the Wappi server. Rolls back (re-adds the row, drops the watermark) on failure.
    /// </summary>
    public void DeleteChat(string chatId)
    {
        if (string.IsNullOrEmpty(chatId)) return;
        if (!chatLookup.TryGetValue(chatId, out var vm)) return;

        int index = Chats.IndexOf(vm);

        // Watermark at the current last message: the chat hides until something newer arrives.
        _deletedWatermarks[chatId] = vm.LastMessageTime;
        DeletedChatStore.Save(GetCacheRoot(), _deletedWatermarks);

        RemoveChatLocally(chatId);
        ChatHistoryCache.DeleteHistory(GetCacheRoot(), chatId);

        StartCoroutine(DeleteChatRoutine(chatId, vm, index));
    }

    private void RemoveChatLocally(string chatId)
    {
        if (chatLookup.TryGetValue(chatId, out var vm))
        {
            Chats.Remove(vm);
            chatLookup.Remove(chatId);
        }
        OnChatRemoved?.Invoke(chatId);
    }

    private IEnumerator DeleteChatRoutine(string chatId, ChatViewModel vm, int index)
    {
        string profileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(profileId))
        {
            Debug.LogWarning($"[ChatManager] DeleteChat: no active profile_id; rolling back {chatId}.");
            RollbackDelete(chatId, vm, index);
            yield break;
        }

        string recipient = WappiRecipient.FromChatId(chatId);
        string url = $"https://wappi.pro/api/sync/chat/delete?profile_id={profileId}";
        string body = JsonConvert.SerializeObject(new WappiDeleteChatRequest { recipient = recipient });

        using UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] raw = System.Text.Encoding.UTF8.GetBytes(body);
        www.uploadHandler = new UploadHandlerRaw(raw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Wappi] chat/delete failed [{www.responseCode}] {url}: {www.error}\n{www.downloadHandler?.text}");
            RollbackDelete(chatId, vm, index);
            yield break;
        }

        WappiDeleteChatResponse resp = null;
        try { resp = JsonConvert.DeserializeObject<WappiDeleteChatResponse>(www.downloadHandler.text); }
        catch (Exception ex)
        {
            Debug.LogError($"[Wappi] chat/delete parse failed: {ex.Message}\n{www.downloadHandler.text}");
        }

        if (resp == null || resp.status != "done")
        {
            Debug.LogWarning($"[Wappi] chat/delete returned non-done status: {www.downloadHandler?.text}");
            RollbackDelete(chatId, vm, index);
            yield break;
        }

        // Success: the watermark keeps the chat hidden until newer activity revives it. Nothing else to do.
    }

    private void RollbackDelete(string chatId, ChatViewModel vm, int index)
    {
        _deletedWatermarks.Remove(chatId);
        DeletedChatStore.Save(GetCacheRoot(), _deletedWatermarks);
        if (vm == null) return;
        if (chatLookup.ContainsKey(chatId)) return; // already restored / never gone

        Chats.Insert(Mathf.Clamp(index, 0, Chats.Count), vm);
        chatLookup[chatId] = vm;
        OnChatAdded?.Invoke(vm); // re-spawns the row (appends)
    }
}

[Serializable]
public class WappiDeleteChatRequest { public string recipient; }

[Serializable]
public class WappiDeleteChatResponse { public string status; }
```

- [ ] **Step 2: `ChatManager.cs` — replace the reconcile block.** Find:
```csharp
        // Resurrection guard: drop guarded ids the server no longer lists (delete confirmed),
        // then below we skip re-adding ids still being suppressed this session.
        var serverIds = new System.Collections.Generic.HashSet<string>();
        foreach (var d in response.dialogs)
            if (d != null && d.id != null) serverIds.Add(d.id);
        _deletedChats.ReconcileWithServer(serverIds);
```
Replace with:
```csharp
        // Ids the server currently lists (used by the stale-chat sweep below).
        var serverIds = new System.Collections.Generic.HashSet<string>();
        foreach (var d in response.dialogs)
            if (d != null && d.id != null) serverIds.Add(d.id);

        bool watermarksDirty = false;
```

- [ ] **Step 3: `ChatManager.cs` — replace the loop top (suppress → watermark).** Find:
```csharp
        foreach (var chat in response.dialogs)
        {
            if (_deletedChats.ShouldSuppress(chat.id)) continue;

            long unixTime = 0;
            if (DateTimeOffset.TryParse(chat.last_timestamp, out var dto)) unixTime = dto.ToUnixTimeSeconds();

            string displayName = string.IsNullOrEmpty(chat.name) ? chat.id[..^5] : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.name);
```
Replace with:
```csharp
        foreach (var chat in response.dialogs)
        {
            long unixTime = 0;
            if (DateTimeOffset.TryParse(chat.last_timestamp, out var dto)) unixTime = dto.ToUnixTimeSeconds();

            // Soft-delete watermark: hide a deleted chat until newer activity revives it, and adopt
            // an externally-isDeleted chat we have no record for. See DeletedChatRule.
            bool hasWatermark = _deletedWatermarks.TryGetValue(chat.id, out long watermark);
            if (DeletedChatRule.ShouldHide(hasWatermark, watermark, unixTime, chat.isDeleted, out long adopt))
            {
                if (adopt >= 0) { _deletedWatermarks[chat.id] = adopt; watermarksDirty = true; }
                if (chatLookup.TryGetValue(chat.id, out var hiddenVm))
                {
                    Chats.Remove(hiddenVm);
                    chatLookup.Remove(chat.id);
                    OnChatRemoved?.Invoke(chat.id);
                }
                continue;
            }

            string displayName = string.IsNullOrEmpty(chat.name) ? chat.id[..^5] : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.name);
```

- [ ] **Step 4: `ChatManager.cs` — persist adopted watermarks at the end of `ParseChatsJson`.** Find:
```csharp
                    ChatHistoryCache.DeleteHistory(cacheRoot, goneId);
                    OnChatRemoved?.Invoke(goneId);
                }
            }
        }
    }
```
Replace with:
```csharp
                    ChatHistoryCache.DeleteHistory(cacheRoot, goneId);
                    OnChatRemoved?.Invoke(goneId);
                }
            }
        }

        if (watermarksDirty) DeletedChatStore.Save(GetCacheRoot(), _deletedWatermarks);
    }
```

- [ ] **Step 5: `ChatManager.BotState.cs` — load watermarks when the bot's list loads.** Find:
```csharp
    private void LoadChatsForActiveBot()
    {
        string cachePath = Path.Combine(GetCacheRoot(), "chats.json");
```
Replace with:
```csharp
    private void LoadChatsForActiveBot()
    {
        _deletedWatermarks = DeletedChatStore.Load(GetCacheRoot());
        string cachePath = Path.Combine(GetCacheRoot(), "chats.json");
```

- [ ] **Step 6: `ChatManager.BotState.cs` — drop the `ClearAll` in `SetActiveBot`.** Find:
```csharp
        Chats.Clear();
        chatLookup.Clear();
        _deletedChats.ClearAll(); // per-bot: a delete guard left set would suppress the same chatId on the next bot
        OnChatListCleared?.Invoke();
        OnActiveBotChanged?.Invoke(botId);
```
Replace with:
```csharp
        Chats.Clear();
        chatLookup.Clear();
        OnChatListCleared?.Invoke();
        OnActiveBotChanged?.Invoke(botId);
```

- [ ] **Step 7: `ChatManager.BotState.cs` — drop the `ClearAll` in the no-bots path.** Find:
```csharp
            Chats.Clear();
            chatLookup.Clear();
            _deletedChats.ClearAll();
            OnChatListCleared?.Invoke();
            OnEmptyState?.Invoke(EmptyStateReason.NoBotsExist);
```
Replace with:
```csharp
            Chats.Clear();
            chatLookup.Clear();
            OnChatListCleared?.Invoke();
            OnEmptyState?.Invoke(EmptyStateReason.NoBotsExist);
```

- [ ] **Step 8: Compile-verify** — `printf 'WappiRecipientTests' > …trigger` (+poll). Expected `overall:"Passed"`. If `CompilationFailed`, the likely cause is a leftover `_deletedChats` reference — grep `grep -rn "_deletedChats" Assets/Scripts` should return nothing.

- [ ] **Step 9: Commit**
```bash
git add Assets/Scripts/Main/ChatManager.DeleteChat.cs Assets/Scripts/Main/ChatManager.cs Assets/Scripts/Main/ChatManager.BotState.cs
git commit -m "feat(chat): persistent soft-delete watermark replaces session guard"
```

---

## Task 5: Remove the now-dead guard + cache editor

After Task 4, `DeletedChatGuard` and `ChatListCacheEditor` have no remaining references.

**Files:** Delete `DeletedChatGuard.cs`(+test), `ChatListCacheEditor.cs`(+test)

- [ ] **Step 1: Confirm they're unreferenced**
```bash
cd /Users/ayan/Projects/Automation
grep -rn "DeletedChatGuard\|ChatListCacheEditor" Assets/Scripts
```
Expected: only the two class definition files themselves (no usages elsewhere). If anything else appears, STOP — Task 4 missed a reference.

- [ ] **Step 2: Remove the files (production + tests, together so the assembly always compiles)**
```bash
git rm Assets/Scripts/Chat/DeletedChatGuard.cs Assets/Scripts/Chat/DeletedChatGuard.cs.meta \
       Assets/Tests/Editor/Chat/DeletedChatGuardTests.cs Assets/Tests/Editor/Chat/DeletedChatGuardTests.cs.meta \
       Assets/Scripts/Chat/ChatListCacheEditor.cs Assets/Scripts/Chat/ChatListCacheEditor.cs.meta \
       Assets/Tests/Editor/Chat/ChatListCacheEditorTests.cs Assets/Tests/Editor/Chat/ChatListCacheEditorTests.cs.meta
```

- [ ] **Step 3: Compile-verify + confirm the remaining suite is green** — `printf 'DeletedChatRuleTests|DeletedChatStoreTests|WappiRecipientTests|ChatHistoryCacheDeleteTests' > …trigger` (+poll). Expected `overall:"Passed"`, no `CompilationFailed`, and the DeletedChatGuardTests/ChatListCacheEditorTests are gone.

- [ ] **Step 4: Commit**
```bash
git commit -m "chore(chat): remove dead DeletedChatGuard + ChatListCacheEditor"
```

---

## Task 6: Manual verification (Editor / device)

- [ ] **Step 1:** Delete a chat in the app → it disappears. Pull-to-refresh / re-sync → it stays gone. Cold-restart the app → still gone.
- [ ] **Step 2:** Two bots sharing a contact: delete the chat on bot A → bot B is unaffected; return to bot A → still gone (no reappearance).
- [ ] **Step 3: Revival** — after deleting, send/receive a new message in that chat from WhatsApp, then refresh the list in the app → the chat reappears, and stays on subsequent syncs.
- [ ] **Step 4: External delete** — delete a chat directly in WhatsApp (not via the app) → on the next sync it disappears in the app too (adopted); a later new message revives it.
- [ ] **Step 5: Failure rollback** — break networking, delete a chat → the row returns and `[Wappi] chat/delete failed` is logged; the watermark was removed (it won't stay hidden).
- [ ] **Step 6:** Inspect `{persistentDataPath}/BotCache/<bot>/deleted_chats.json` — it lists the deleted chatId(s) with timestamps.

---

## Self-review (completed during authoring)
- **Spec coverage:** persistence (T2), decision rule (T1), `isDeleted` model (T3), ParseChatsJson hide/show/adopt + save + on-screen removal (T4 steps 2-4), delete flow watermark + rollback (T4 step 1), bot-switch load replacing ClearAll (T4 steps 5-7), removal of guard/cache-editor + chats.json rewrite (T4 step 1 drops the rewrite; T5 deletes the classes), tests (T1/T2), absent-chat safety retained (untouched in T4). ✓
- **Type consistency:** `DeletedChatRule.ShouldHide(bool,long,long,bool,out long)`, `DeletedChatStore.Load/Save`, `_deletedWatermarks` (Dictionary<string,long>), `ChatDialog.isDeleted`, `ChatViewModel.LastMessageTime` — used consistently across tasks. ✓
- **Placeholder scan:** none. ✓
