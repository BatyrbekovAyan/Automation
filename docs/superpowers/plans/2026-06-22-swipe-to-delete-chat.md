# Swipe-to-delete chat — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a WhatsApp-style swipe-left gesture on chat list rows that reveals a Delete button which, after a confirm dialog, permanently deletes the conversation on the WhatsApp server via Wappi and removes it locally.

**Architecture:** A new `SwipeToDelete` gesture (modeled on `SwipeToReply`) slides a `SwipeContent` layer left to reveal a Delete button behind it. Tapping Delete opens a `PopupUI` confirm dialog; on confirm, `ChatManager.DeleteChat` optimistically removes the row and caches, then calls `POST /api/sync/chat/delete`, rolling back on failure. A session-scoped `DeletedChatGuard` plus a `chats.json` rewrite stop the deleted chat from reappearing on sync or cold launch.

**Tech Stack:** Unity 6 (C#), UnityWebRequest + coroutines, Newtonsoft.Json (JsonConvert) for the API body, JsonUtility for cache models, DOTween for animation, NUnit EditMode tests.

---

## Spec reference

[docs/superpowers/specs/2026-06-22-swipe-to-delete-chat-design.md](docs/superpowers/specs/2026-06-22-swipe-to-delete-chat-design.md)

## Notes for the executor

- **Unity TDD wrinkle:** a test that references a not-yet-existing type causes a *compile error* that blocks the whole EditMode assembly (you can't run a single test). So for each testable unit: first create the type with a compiling stub that returns a wrong/default value, then write the test (it compiles and fails on the assertion), then implement.
- **Running EditMode tests** (`Assets/Tests/Editor/Chat/`, no asmdef):
  - Editor closed: `Tools/run-tests-headless.sh '<ClassNameRegex>'` → reads `Tools/test-output/`.
  - Editor open (focused): drop `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json` (see `Assets/Editor/ClaudeTestBridge.cs`). Never recompile mid-run.
- **Every new `.cs` commit must include its generated `.meta` file** (Unity creates it on import/recompile). Stage both.
- **Deviations from the spec, flagged for review:**
  1. The spec says a "brief error toast" on failure. The project has **no toast infrastructure** (only modal `PopupUI`). v1 uses the row reappearing (rollback) as the failure signal plus a `Debug.LogError`; a toast is **out of scope**. If you want a visible toast, say so and it becomes its own task.
  2. On rollback the row is re-added via `OnChatAdded`, which appends at the list bottom (it does not restore the exact prior position). This only affects the rare failure path; the next sync with a new message re-sorts it. Acceptable for v1.

---

## File structure

**New (testable pure logic):**
- `Assets/Scripts/Chat/WappiRecipient.cs` — chatId → Wappi `recipient` derivation.
- `Assets/Scripts/Chat/DeletedChatGuard.cs` — session-scoped resurrection guard.
- `Assets/Scripts/Chat/ChatListCacheEditor.cs` — remove one chat from a `chats.json` blob.

**New (Unity-runtime, editor-verified):**
- `Assets/Scripts/Main/ChatManager.DeleteChat.cs` — partial class: `DeleteChat`, the routine, DTOs, `OnChatRemoved`, guard field.
- `Assets/Scripts/Chat/SwipeToDelete.cs` — the row gesture.
- `Assets/Scripts/UI/ChatDeleteConfirm.cs` — confirm-dialog controller.
- `Assets/Editor/ChatDeleteConfirmBuilder.cs` — builds the confirm panel under the selected screen.

**Modified:**
- `Assets/Scripts/Chat/ChatHistoryCache.cs` — add `DeleteHistory`.
- `Assets/Scripts/Main/ChatManager.cs` — `ParseChatsJson` guard wiring.
- `Assets/Scripts/UI/ChatListView.cs` — `OnChatRemoved` handler + `RequestDelete`.
- `Assets/Scripts/UI/ChatItemView.cs` — delete-button hook + tap suppression.
- `Assets/Prefabs/ChatItem.prefab` — Delete layer + `SwipeContent`.

**New tests:**
- `Assets/Tests/Editor/Chat/WappiRecipientTests.cs`
- `Assets/Tests/Editor/Chat/DeletedChatGuardTests.cs`
- `Assets/Tests/Editor/Chat/ChatHistoryCacheDeleteTests.cs`
- `Assets/Tests/Editor/Chat/ChatListCacheEditorTests.cs`

---

## Task 1: `WappiRecipient` — chatId → recipient

**Files:**
- Create: `Assets/Scripts/Chat/WappiRecipient.cs`
- Test: `Assets/Tests/Editor/Chat/WappiRecipientTests.cs`

- [ ] **Step 1: Create a compiling stub**

```csharp
// Assets/Scripts/Chat/WappiRecipient.cs
public static class WappiRecipient
{
    public static string FromChatId(string chatId) => chatId; // stub
}
```

- [ ] **Step 2: Write the failing test**

```csharp
// Assets/Tests/Editor/Chat/WappiRecipientTests.cs
using NUnit.Framework;

public class WappiRecipientTests
{
    [Test] public void StripsCUsSuffixForOneToOne()
        => Assert.AreEqual("79995579399", WappiRecipient.FromChatId("79995579399@c.us"));

    [Test] public void PreservesGroupId()
        => Assert.AreEqual("120363012345@g.us", WappiRecipient.FromChatId("120363012345@g.us"));

    [Test] public void PassesThroughBareId()
        => Assert.AreEqual("79995579399", WappiRecipient.FromChatId("79995579399"));

    [Test] public void NullAndEmptyAreSafe()
    {
        Assert.IsNull(WappiRecipient.FromChatId(null));
        Assert.AreEqual("", WappiRecipient.FromChatId(""));
    }
}
```

- [ ] **Step 3: Run tests — expect FAIL**

Run: `Tools/run-tests-headless.sh 'WappiRecipientTests'`
Expected: `StripsCUsSuffixForOneToOne` FAILS (stub returns the full id).

- [ ] **Step 4: Implement**

```csharp
public static class WappiRecipient
{
    // Mirror PostTextMessageRoutine (ChatManager.cs): bare phone for 1:1 (@c.us),
    // full id for groups (@g.us) and anything else.
    public static string FromChatId(string chatId)
    {
        if (string.IsNullOrEmpty(chatId)) return chatId;
        return chatId.EndsWith("@c.us") ? chatId.Replace("@c.us", "") : chatId;
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

Run: `Tools/run-tests-headless.sh 'WappiRecipientTests'`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/WappiRecipient.cs Assets/Scripts/Chat/WappiRecipient.cs.meta \
        Assets/Tests/Editor/Chat/WappiRecipientTests.cs Assets/Tests/Editor/Chat/WappiRecipientTests.cs.meta
git commit -m "feat(chat): add WappiRecipient chatId->recipient helper"
```

---

## Task 2: `DeletedChatGuard` — resurrection guard

**Files:**
- Create: `Assets/Scripts/Chat/DeletedChatGuard.cs`
- Test: `Assets/Tests/Editor/Chat/DeletedChatGuardTests.cs`

- [ ] **Step 1: Create a compiling stub**

```csharp
// Assets/Scripts/Chat/DeletedChatGuard.cs
using System.Collections.Generic;

public class DeletedChatGuard
{
    public void MarkDeleted(string chatId) { }
    public bool ShouldSuppress(string chatId) => false; // stub
    public void Clear(string chatId) { }
    public void ReconcileWithServer(ICollection<string> serverChatIds) { }
}
```

- [ ] **Step 2: Write the failing test**

```csharp
// Assets/Tests/Editor/Chat/DeletedChatGuardTests.cs
using System.Collections.Generic;
using NUnit.Framework;

public class DeletedChatGuardTests
{
    [Test] public void SuppressesAfterMark()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted("a@c.us");
        Assert.IsTrue(g.ShouldSuppress("a@c.us"));
        Assert.IsFalse(g.ShouldSuppress("b@c.us"));
    }

    [Test] public void ClearStopsSuppression()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted("a@c.us");
        g.Clear("a@c.us");
        Assert.IsFalse(g.ShouldSuppress("a@c.us"));
    }

    [Test] public void ReconcileKeepsIdStillOnServer()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted("a@c.us");
        g.ReconcileWithServer(new HashSet<string> { "a@c.us", "b@c.us" }); // still present
        Assert.IsTrue(g.ShouldSuppress("a@c.us"));
    }

    [Test] public void ReconcileDropsIdAbsentFromServer()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted("a@c.us");
        g.ReconcileWithServer(new HashSet<string> { "b@c.us" }); // server confirmed gone
        Assert.IsFalse(g.ShouldSuppress("a@c.us"));
    }

    [Test] public void NullAndEmptyAreSafe()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted(null); g.MarkDeleted("");
        Assert.IsFalse(g.ShouldSuppress(null));
        Assert.IsFalse(g.ShouldSuppress(""));
        g.ReconcileWithServer(null); // no throw
    }
}
```

- [ ] **Step 3: Run tests — expect FAIL**

Run: `Tools/run-tests-headless.sh 'DeletedChatGuardTests'`
Expected: `SuppressesAfterMark` and others FAIL (stub always returns false).

- [ ] **Step 4: Implement**

```csharp
using System.Collections.Generic;

public class DeletedChatGuard
{
    private readonly HashSet<string> _deleted = new HashSet<string>();

    public void MarkDeleted(string chatId)
    {
        if (!string.IsNullOrEmpty(chatId)) _deleted.Add(chatId);
    }

    public bool ShouldSuppress(string chatId)
        => !string.IsNullOrEmpty(chatId) && _deleted.Contains(chatId);

    public void Clear(string chatId)
    {
        if (!string.IsNullOrEmpty(chatId)) _deleted.Remove(chatId);
    }

    // Drop any guarded id the server no longer lists — the delete is confirmed,
    // so suppression is no longer needed.
    public void ReconcileWithServer(ICollection<string> serverChatIds)
    {
        if (serverChatIds == null) return;
        var toClear = new List<string>();
        foreach (var id in _deleted)
            if (!serverChatIds.Contains(id)) toClear.Add(id);
        foreach (var id in toClear) _deleted.Remove(id);
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

Run: `Tools/run-tests-headless.sh 'DeletedChatGuardTests'`
Expected: 5 passed.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/DeletedChatGuard.cs Assets/Scripts/Chat/DeletedChatGuard.cs.meta \
        Assets/Tests/Editor/Chat/DeletedChatGuardTests.cs Assets/Tests/Editor/Chat/DeletedChatGuardTests.cs.meta
git commit -m "feat(chat): add DeletedChatGuard resurrection guard"
```

---

## Task 3: `ChatHistoryCache.DeleteHistory`

**Files:**
- Modify: `Assets/Scripts/Chat/ChatHistoryCache.cs`
- Test: `Assets/Tests/Editor/Chat/ChatHistoryCacheDeleteTests.cs`

- [ ] **Step 1: Add a compiling stub** to `ChatHistoryCache` (inside the class, after `LoadHistory`):

```csharp
    public static void DeleteHistory(string baseDir, string chatId) { } // stub
```

- [ ] **Step 2: Write the failing test**

```csharp
// Assets/Tests/Editor/Chat/ChatHistoryCacheDeleteTests.cs
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class ChatHistoryCacheDeleteTests
{
    private string _root;

    [SetUp] public void SetUp()
    {
        _root = Path.Combine(Application.temporaryCachePath, "delhist_" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(_root, "messages"));
    }

    [TearDown] public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test] public void DeletesTheChatFile()
    {
        string path = Path.Combine(_root, "messages", "a@c.us.json");
        File.WriteAllText(path, "{}");
        Assert.IsTrue(File.Exists(path));
        ChatHistoryCache.DeleteHistory(_root, "a@c.us");
        Assert.IsFalse(File.Exists(path));
    }

    [Test] public void NoThrowWhenFileAbsent()
        => Assert.DoesNotThrow(() => ChatHistoryCache.DeleteHistory(_root, "missing@c.us"));

    [Test] public void NullArgsAreSafe()
    {
        Assert.DoesNotThrow(() => ChatHistoryCache.DeleteHistory(null, "a@c.us"));
        Assert.DoesNotThrow(() => ChatHistoryCache.DeleteHistory(_root, null));
    }
}
```

- [ ] **Step 3: Run tests — expect FAIL**

Run: `Tools/run-tests-headless.sh 'ChatHistoryCacheDeleteTests'`
Expected: `DeletesTheChatFile` FAILS (stub does nothing; file still exists).

- [ ] **Step 4: Implement** (replace the stub):

```csharp
    /// <summary>
    /// Deletes {baseDir}/messages/{chatId}.json if present. Null/empty-safe; never throws.
    /// </summary>
    public static void DeleteHistory(string baseDir, string chatId)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(chatId)) return;
        string path = Path.Combine(baseDir, "messages", $"{chatId}.json");
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatHistoryCache] DeleteHistory failed for {chatId}: {ex.Message}");
        }
    }
```

- [ ] **Step 5: Run tests — expect PASS**

Run: `Tools/run-tests-headless.sh 'ChatHistoryCacheDeleteTests'`
Expected: 3 passed.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/ChatHistoryCache.cs \
        Assets/Tests/Editor/Chat/ChatHistoryCacheDeleteTests.cs Assets/Tests/Editor/Chat/ChatHistoryCacheDeleteTests.cs.meta
git commit -m "feat(chat): add ChatHistoryCache.DeleteHistory"
```

---

## Task 4: `ChatListCacheEditor` — drop a chat from chats.json

**Files:**
- Create: `Assets/Scripts/Chat/ChatListCacheEditor.cs`
- Test: `Assets/Tests/Editor/Chat/ChatListCacheEditorTests.cs`

- [ ] **Step 1: Create a compiling stub**

```csharp
// Assets/Scripts/Chat/ChatListCacheEditor.cs
public static class ChatListCacheEditor
{
    public static string RemoveChat(string json, string chatId) => json; // stub
}
```

- [ ] **Step 2: Write the failing test**

```csharp
// Assets/Tests/Editor/Chat/ChatListCacheEditorTests.cs
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class ChatListCacheEditorTests
{
    private static string TwoChatJson()
    {
        var resp = new ChatsResponse
        {
            status = "done",
            dialogs = new List<ChatDialog>
            {
                new ChatDialog { id = "a@c.us", name = "Alpha" },
                new ChatDialog { id = "b@c.us", name = "Bravo" },
            }
        };
        return JsonUtility.ToJson(resp);
    }

    [Test] public void RemovesTheNamedChat()
    {
        string outJson = ChatListCacheEditor.RemoveChat(TwoChatJson(), "a@c.us");
        var parsed = JsonUtility.FromJson<ChatsResponse>(outJson);
        Assert.AreEqual(1, parsed.dialogs.Count);
        Assert.AreEqual("b@c.us", parsed.dialogs[0].id);
    }

    [Test] public void UnknownChatLeavesJsonUnchanged()
    {
        string input = TwoChatJson();
        Assert.AreEqual(input, ChatListCacheEditor.RemoveChat(input, "zzz@c.us"));
    }

    [Test] public void NullOrEmptyInputsAreSafe()
    {
        Assert.IsNull(ChatListCacheEditor.RemoveChat(null, "a@c.us"));
        Assert.AreEqual("", ChatListCacheEditor.RemoveChat("", "a@c.us"));
        Assert.AreEqual("{}", ChatListCacheEditor.RemoveChat("{}", null));
    }

    [Test] public void GarbageJsonReturnedUnchanged()
        => Assert.AreEqual("not json", ChatListCacheEditor.RemoveChat("not json", "a@c.us"));
}
```

- [ ] **Step 3: Run tests — expect FAIL**

Run: `Tools/run-tests-headless.sh 'ChatListCacheEditorTests'`
Expected: `RemovesTheNamedChat` FAILS (stub returns input; still 2 dialogs).

- [ ] **Step 4: Implement**

```csharp
using UnityEngine;

public static class ChatListCacheEditor
{
    /// <summary>
    /// Returns <paramref name="json"/> with the dialog whose id == <paramref name="chatId"/>
    /// removed. Returns the input unchanged when it's null/empty/unparseable, has no dialogs,
    /// or the chat isn't present. Re-serialized via JsonUtility — only fields modeled on
    /// ChatsResponse/ChatDialog are preserved (sufficient: the cache is non-authoritative and
    /// the next /chats/filter sync overwrites it wholesale).
    /// </summary>
    public static string RemoveChat(string json, string chatId)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(chatId)) return json;

        ChatsResponse response;
        try { response = JsonUtility.FromJson<ChatsResponse>(json); }
        catch { return json; }

        if (response?.dialogs == null) return json;

        int removed = response.dialogs.RemoveAll(d => d != null && d.id == chatId);
        if (removed == 0) return json;

        return JsonUtility.ToJson(response);
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

Run: `Tools/run-tests-headless.sh 'ChatListCacheEditorTests'`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/ChatListCacheEditor.cs Assets/Scripts/Chat/ChatListCacheEditor.cs.meta \
        Assets/Tests/Editor/Chat/ChatListCacheEditorTests.cs Assets/Tests/Editor/Chat/ChatListCacheEditorTests.cs.meta
git commit -m "feat(chat): add ChatListCacheEditor.RemoveChat for chats.json"
```

---

## Task 5: `ChatManager.DeleteChat` partial — the delete pipeline

**Files:**
- Create: `Assets/Scripts/Main/ChatManager.DeleteChat.cs`

This wires Tasks 1–4 together. No unit test (it's a MonoBehaviour coroutine + network); verified end-to-end in Task 12.

- [ ] **Step 1: Create the partial**

```csharp
// Assets/Scripts/Main/ChatManager.DeleteChat.cs
using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    /// <summary>Fired after a chat is removed from the in-memory list (optimistic or confirmed).</summary>
    public event Action<string> OnChatRemoved;

    // Suppresses re-add of chats deleted this session until the server confirms (see ParseChatsJson).
    private readonly DeletedChatGuard _deletedChats = new DeletedChatGuard();

    /// <summary>
    /// Optimistically removes the chat + its caches, then deletes it on the Wappi server.
    /// Rolls back (re-adds the row) if the server call fails.
    /// </summary>
    public void DeleteChat(string chatId)
    {
        if (string.IsNullOrEmpty(chatId)) return;
        if (!chatLookup.TryGetValue(chatId, out var vm)) return;

        int index = Chats.IndexOf(vm);

        _deletedChats.MarkDeleted(chatId);
        RemoveChatLocally(chatId);
        EvictChatCaches(chatId);

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

    private void EvictChatCaches(string chatId)
    {
        string root = GetCacheRoot();
        ChatHistoryCache.DeleteHistory(root, chatId);

        string cachePath = Path.Combine(root, "chats.json");
        if (!File.Exists(cachePath)) return;
        try
        {
            string updated = ChatListCacheEditor.RemoveChat(File.ReadAllText(cachePath), chatId);
            File.WriteAllText(cachePath, updated);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ChatManager] chats.json rewrite failed for {chatId}: {ex.Message}");
        }
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

        // Success: keep the guard until a later /chats/filter no longer lists chatId
        // (cleared by ParseChatsJson's reconcile).
    }

    private void RollbackDelete(string chatId, ChatViewModel vm, int index)
    {
        _deletedChats.Clear(chatId);
        if (vm == null) return;
        if (chatLookup.ContainsKey(chatId)) return; // already restored / never gone

        Chats.Insert(Mathf.Clamp(index, 0, Chats.Count), vm);
        chatLookup[chatId] = vm;
        OnChatAdded?.Invoke(vm); // re-spawns the row (appends; see plan deviation #2)
    }
}

[Serializable]
public class WappiDeleteChatRequest
{
    public string recipient;
}

[Serializable]
public class WappiDeleteChatResponse
{
    public string status;
}
```

- [ ] **Step 2: Recompile and confirm no errors**

Editor open: `mcp__mcp-unity__recompile_scripts` then `mcp__mcp-unity__get_console_logs` (filter errors).
Editor closed: `Tools/run-tests-headless.sh 'WappiRecipientTests'` (a no-op filter that still forces a compile; expect compile success + that one class green).
Expected: no compile errors. (`ParseChatsJson` wiring comes in Task 6.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.DeleteChat.cs Assets/Scripts/Main/ChatManager.DeleteChat.cs.meta
git commit -m "feat(chat): add ChatManager.DeleteChat server-delete pipeline"
```

---

## Task 6: Wire the guard into `ParseChatsJson`

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` (`ParseChatsJson`, ~line 230)

- [ ] **Step 1: Add the reconcile block.** After `if (response?.dialogs == null) return;` and before the `if (isInitialLoad)` block, insert:

```csharp
        // Resurrection guard: drop guarded ids the server no longer lists (delete confirmed),
        // then below we skip re-adding ids still being suppressed this session.
        var serverIds = new System.Collections.Generic.HashSet<string>();
        foreach (var d in response.dialogs)
            if (d != null && d.id != null) serverIds.Add(d.id);
        _deletedChats.ReconcileWithServer(serverIds);
```

- [ ] **Step 2: Add the suppression check.** As the very first line inside `foreach (var chat in response.dialogs)`:

```csharp
            if (_deletedChats.ShouldSuppress(chat.id)) continue;
```

- [ ] **Step 3: Recompile and confirm no errors**

Editor open: `mcp__mcp-unity__recompile_scripts` → `get_console_logs`.
Expected: no compile errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): suppress deleted chats from re-adding on sync"
```

---

## Task 7: `ChatListView` — remove row on `OnChatRemoved` + `RequestDelete`

**Files:**
- Modify: `Assets/Scripts/UI/ChatListView.cs`

- [ ] **Step 1: Add a serialized confirm reference.** After `public ChatItemView prefab;`:

```csharp
    [SerializeField] private ChatDeleteConfirm deleteConfirm;
```

- [ ] **Step 2: Subscribe/unsubscribe.** In `Start()` after the other `manager.On...` lines add:

```csharp
        manager.OnChatRemoved += RemoveChat;
```

In `OnDestroy()` inside the `if (ChatManager.Instance != null)` block add:

```csharp
            ChatManager.Instance.OnChatRemoved -= RemoveChat;
```

- [ ] **Step 3: Add the removal handler + delete request.** Add these methods to the class (and `using DG.Tweening;` + `using UnityEngine.UI;` at the top if not present):

```csharp
    // Collapse the row out, then destroy it. Assumes the scroll content uses a
    // VerticalLayoutGroup (animating LayoutElement.preferredHeight drives reflow).
    private void RemoveChat(string chatId)
    {
        if (!itemsByChatId.TryGetValue(chatId, out var item))
            return;
        itemsByChatId.Remove(chatId);
        if (item == null) return;

        var rt = (RectTransform)item.transform;
        var le = item.GetComponent<LayoutElement>();
        if (le == null) le = item.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = rt.rect.height;

        var cg = item.GetComponent<CanvasGroup>();
        if (cg == null) cg = item.gameObject.AddComponent<CanvasGroup>();

        var go = item.gameObject;
        DG.Tweening.DOTween.To(() => le.preferredHeight, v => le.preferredHeight = v, 0f, 0.2f)
            .SetEase(DG.Tweening.Ease.InCubic)
            .OnComplete(() => { if (go != null) Destroy(go); });
        cg.DOFade(0f, 0.2f);
    }

    // Called by a row's Delete button (via ChatItemView) — raises the confirm dialog.
    public void RequestDelete(ChatViewModel vm)
    {
        if (vm == null) return;
        if (deleteConfirm != null) deleteConfirm.Ask(vm.ChatId, vm.Title);
        else ChatManager.Instance?.DeleteChat(vm.ChatId); // fallback: no dialog wired
    }
```

- [ ] **Step 4: Recompile.** This will error until `ChatDeleteConfirm` exists (Task 11). That's expected — do Task 8–11, then verify compile in Task 11. For now confirm the only errors reference `ChatDeleteConfirm`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/ChatListView.cs
git commit -m "feat(chat): ChatListView removes row on OnChatRemoved + RequestDelete"
```

---

## Task 8: `ChatItemView` — delete-button hook + tap suppression

**Files:**
- Modify: `Assets/Scripts/UI/ChatItemView.cs`

- [ ] **Step 1: Add serialized fields.** After `public TextMeshProUGUI unreadCountText;`:

```csharp
    [Header("Swipe-to-delete")]
    public Button deleteButton;        // the red button revealed behind the row
    public SwipeToDelete swipeToDelete; // on the SwipeContent child
```

- [ ] **Step 2: Reset + wire on Bind.** At the end of `Bind(...)`, after the existing `button.onClick` wiring, add:

```csharp
        if (swipeToDelete != null) swipeToDelete.ResetClosed();

        if (deleteButton != null)
        {
            deleteButton.onClick.RemoveAllListeners();
            deleteButton.onClick.AddListener(OnDeleteClicked);
        }
```

- [ ] **Step 3: Suppress the row tap while open.** Replace the existing `OnClick()` body:

```csharp
    void OnClick()
    {
        // An open row's tap closes the reveal instead of opening the chat (WhatsApp behavior).
        if (swipeToDelete != null && swipeToDelete.IsOpen)
        {
            swipeToDelete.Close();
            return;
        }
        ChatManager.Instance.SelectChat(chatId);
    }

    void OnDeleteClicked()
    {
        if (parentList != null) parentList.RequestDelete(vm);
    }
```

- [ ] **Step 4: Recompile.** Errors will reference `SwipeToDelete` until Task 9. Expected.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/ChatItemView.cs
git commit -m "feat(chat): ChatItemView delete-button hook + tap suppression"
```

---

## Task 9: `SwipeToDelete` gesture

**Files:**
- Create: `Assets/Scripts/Chat/SwipeToDelete.cs`

- [ ] **Step 1: Create the component**

```csharp
// Assets/Scripts/Chat/SwipeToDelete.cs
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// WhatsApp-style swipe-left-to-reveal-Delete on a chat list row. Lives on the row's
/// SwipeContent (the layer carrying avatar/text/bg/tap-button) and slides it on the X axis,
/// exposing the DeleteButton pinned behind it. Modeled on SwipeToReply: horizontal drags are
/// claimed here; vertical / blocked drags are forwarded to the list ScrollRect so scrolling is
/// unaffected. Only one row is open at a time.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SwipeToDelete : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private float revealWidth = 150f; // must match the DeleteButton width
    private const float SnapSeconds = 0.18f;

    private RectTransform _rt;
    private ScrollRect _scroll;
    private bool _routeToParent;
    private bool _dragging;
    private float _baseX;
    private Tween _snap;

    public bool IsOpen { get; private set; }

    private static SwipeToDelete _openInstance;

    private void Awake()
    {
        _rt = (RectTransform)transform;
        _scroll = GetComponentInParent<ScrollRect>();
    }

    private void OnDisable()
    {
        _snap?.Kill(); _snap = null;
        _routeToParent = false; _dragging = false;
        if (_openInstance == this) _openInstance = null;
    }

    /// <summary>Snap shut instantly (used by ChatItemView.Bind on prefab reuse).</summary>
    public void ResetClosed()
    {
        _snap?.Kill(); _snap = null;
        _rt.anchoredPosition = new Vector2(0f, _rt.anchoredPosition.y);
        IsOpen = false;
        if (_openInstance == this) _openInstance = null;
    }

    public void Close()
    {
        _snap?.Kill();
        _snap = _rt.DOAnchorPosX(0f, SnapSeconds).SetEase(Ease.OutCubic);
        IsOpen = false;
        if (_openInstance == this) _openInstance = null;
    }

    private void Open()
    {
        if (_openInstance != null && _openInstance != this) _openInstance.Close();
        _openInstance = this;
        _snap?.Kill();
        _snap = _rt.DOAnchorPosX(-revealWidth, SnapSeconds).SetEase(Ease.OutCubic);
        IsOpen = true;
    }

    public void OnInitializePotentialDrag(PointerEventData e) => _scroll?.OnInitializePotentialDrag(e);

    public void OnBeginDrag(PointerEventData e)
    {
        Vector2 traj = e.position - e.pressPosition;
        bool horizontal = Mathf.Abs(traj.x) > Mathf.Abs(traj.y);
        bool blocked = ScrollClickBlocker.IsBlocking || SwipeToBack.IsSliding;

        if (!horizontal || blocked)
        {
            _routeToParent = true;
            _scroll?.OnBeginDrag(e);
            return;
        }

        if (_openInstance != null && _openInstance != this) _openInstance.Close();

        _dragging = true;
        _snap?.Kill();
        _baseX = IsOpen ? -revealWidth : 0f;
    }

    public void OnDrag(PointerEventData e)
    {
        if (_routeToParent) { _scroll?.OnDrag(e); return; }
        if (!_dragging) return;
        float dx = e.position.x - e.pressPosition.x;
        float x = Mathf.Clamp(_baseX + dx, -revealWidth, 0f);
        _rt.anchoredPosition = new Vector2(x, _rt.anchoredPosition.y);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (_routeToParent) { _scroll?.OnEndDrag(e); _routeToParent = false; return; }
        if (!_dragging) return;
        _dragging = false;

        if (_rt.anchoredPosition.x <= -revealWidth * 0.5f) Open();
        else Close();
    }
}
```

- [ ] **Step 2: Recompile.** `ChatItemView` (Task 8) should now compile against this. `ChatListView` still needs `ChatDeleteConfirm` (Task 11). Confirm remaining errors only reference `ChatDeleteConfirm`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/SwipeToDelete.cs Assets/Scripts/Chat/SwipeToDelete.cs.meta
git commit -m "feat(chat): add SwipeToDelete row gesture"
```

---

## Task 10: Restructure `ChatItem.prefab` for the reveal layer

**Files:**
- Modify: `Assets/Prefabs/ChatItem.prefab`

Editor work — do it in the open Unity Editor (prefab mode) or via `mcp__mcp-unity__*`. Read the prefab first to confirm current child names. Per the project gotcha (`project_builder_rewire_consumers`), **reparent** rather than recreate so `ChatItemView`'s serialized refs survive, and re-verify every ref afterward. Apply `unity-ui-builder` metrics for the visual button.

Current: root `ChatItem` has `Image` (white bg) + `Button` (row tap) + `HorizontalLayoutGroup`, children `Avatar`, `TextBlock`.

- [ ] **Step 1:** Open `Assets/Prefabs/ChatItem.prefab` in prefab mode. Note the root size (the gesture math uses anchoredPosition in row-local units).

- [ ] **Step 2:** Create child `SwipeContent` under root, anchored stretch-fill (anchorMin 0,0 / anchorMax 1,1 / offsetMin 0,0 / offsetMax 0,0).

- [ ] **Step 3:** Move `HorizontalLayoutGroup` off the root onto `SwipeContent` (remove from root, add with the same padding/spacing/alignment values: padding L/R 40, spacing 32, child-align middle-right, child-force-expand height — copy from the original). Add an `Image` on `SwipeContent` set to the same white background the root had, and a `Button` whose `targetGraphic` is that Image.

- [ ] **Step 4:** Reparent `Avatar` and `TextBlock` under `SwipeContent` (preserve order). Remove the now-unused `Image`/`Button` from the root (root keeps only `ChatItemView` + RectTransform).

- [ ] **Step 5:** Create child `DeleteButton` under root **at sibling index 0** (behind `SwipeContent`). RectTransform: anchorMin 1,0 / anchorMax 1,1 / pivot 1,0.5, width 150, right offset 0 (pinned to the right edge, full height). Add `Image` (red — use the project danger red), a `Button`, and two children: a trash icon (`Image`, white) and a TMP label "Delete" (white, caption size). Keep width = 150 to match `SwipeToDelete.revealWidth`.

- [ ] **Step 6:** Add the `SwipeToDelete` component to `SwipeContent`. Set its `revealWidth` to the `DeleteButton` width (150).

- [ ] **Step 7:** On the `ChatItemView` component (root), wire the new serialized refs: `button` → the `Button` on `SwipeContent`; `deleteButton` → the `Button` on `DeleteButton`; `swipeToDelete` → the `SwipeToDelete` on `SwipeContent`. Verify `titleText`, `avatarImage`, `defaultAvatar`, `lastMessageText`, `timeText`, `unreadBadge`, `unreadCountText` all still resolve (they live inside the reparented `Avatar`/`TextBlock`).

- [ ] **Step 8: Maskable audit** (project gotcha `project_bubble_graphics_maskable` analog): ensure new graphics under the row don't break culling — but note chat list rows are not under a RectMask2D viewport the way message bubbles are; standard ScrollRect masking applies. Confirm the DeleteButton/SwipeContent graphics have `Maskable` on (default) so the scroll viewport clips them.

- [ ] **Step 9:** Save the prefab. Recompile if needed. Visually confirm in the Editor that the row looks unchanged when closed.

- [ ] **Step 10: Commit**

```bash
git add Assets/Prefabs/ChatItem.prefab
git commit -m "feat(chat): ChatItem prefab reveal layer (SwipeContent + DeleteButton)"
```

---

## Task 11: `ChatDeleteConfirm` controller + confirm popup panel

**Files:**
- Create: `Assets/Scripts/UI/ChatDeleteConfirm.cs`
- Create: `Assets/Editor/ChatDeleteConfirmBuilder.cs`
- Modify: the scene (`Assets/Scenes/Main.unity`) via the builder + manual wiring

- [ ] **Step 1: Create the controller**

```csharp
// Assets/Scripts/UI/ChatDeleteConfirm.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Modal confirm for deleting a chat. Reuses PopupUI's show/hide animation. Cancel hides;
/// Delete calls ChatManager.DeleteChat for the pending chat. Wired from ChatListView.RequestDelete.
/// </summary>
public class ChatDeleteConfirm : MonoBehaviour
{
    [SerializeField] private GameObject panel;       // backdrop Image + "Content" card child
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button deleteButton;

    private string _pendingChatId;

    private void Awake()
    {
        if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        if (deleteButton != null) deleteButton.onClick.AddListener(Confirm);
        if (panel != null) panel.SetActive(false);
    }

    public void Ask(string chatId, string chatTitle)
    {
        if (string.IsNullOrEmpty(chatId)) return;
        _pendingChatId = chatId;
        if (bodyText != null)
            bodyText.text = string.IsNullOrEmpty(chatTitle)
                ? "This chat will be permanently deleted."
                : $"\"{chatTitle}\" will be permanently deleted.";
        PopupUI.Show(panel);
    }

    private void Cancel()
    {
        _pendingChatId = null;
        PopupUI.Hide(panel);
    }

    private void Confirm()
    {
        string id = _pendingChatId;
        _pendingChatId = null;
        PopupUI.Hide(panel);
        if (!string.IsNullOrEmpty(id)) ChatManager.Instance?.DeleteChat(id);
    }
}
```

- [ ] **Step 2: Create the builder** (project `[MenuItem]` pattern; builds the panel under the selected screen GameObject — select the chat list screen panel first so the popup lives inside its screen, not the canvas root, per `feedback_ui_building`). Apply `unity-ui-builder` metrics; values below are a calibrated starting point.

```csharp
// Assets/Editor/ChatDeleteConfirmBuilder.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class ChatDeleteConfirmBuilder
{
    [MenuItem("Tools/Chat/Build Delete-Confirm Popup")]
    public static void Build()
    {
        var parent = Selection.activeGameObject;
        if (parent == null)
        {
            Debug.LogError("[ChatDeleteConfirmBuilder] Select the chat list screen panel first.");
            return;
        }

        var panel = NewRect("DeleteChatConfirmPanel", parent.transform);
        Stretch(panel);
        var backdrop = panel.gameObject.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.5f);

        var card = NewRect("Content", panel);          // PopupUI animates the "Content" child
        card.sizeDelta = new Vector2(820, 460);
        Center(card);
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = Color.white;

        var title = NewText("Title", card, "Удалить чат?", 44, FontStyles.Bold, TextAlignmentOptions.Center);
        Anchor(title, new Vector2(0.5f, 1f), new Vector2(760, 70), new Vector2(0, -60));

        var body = NewText("Body", card, "", 32, FontStyles.Normal, TextAlignmentOptions.Center);
        body.color = new Color(0.4f, 0.4f, 0.4f, 1f);
        Anchor(body, new Vector2(0.5f, 1f), new Vector2(760, 140), new Vector2(0, -150));

        var cancel = NewButton("CancelButton", card, "Отмена", new Color(0.93f, 0.93f, 0.93f), Color.black);
        Anchor((RectTransform)cancel.transform, new Vector2(0.5f, 0f), new Vector2(360, 110), new Vector2(-195, 70));

        var del = NewButton("DeleteButton", card, "Удалить", new Color(0.89f, 0.29f, 0.29f), Color.white);
        Anchor((RectTransform)del.transform, new Vector2(0.5f, 0f), new Vector2(360, 110), new Vector2(195, 70));

        var controller = panel.gameObject.AddComponent<ChatDeleteConfirm>();
        var so = new SerializedObject(controller);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("bodyText").objectReferenceValue = body;
        so.FindProperty("cancelButton").objectReferenceValue = cancel;
        so.FindProperty("deleteButton").objectReferenceValue = del;
        so.ApplyModifiedProperties();

        panel.gameObject.SetActive(false);
        Selection.activeGameObject = panel.gameObject;
        Debug.Log("[ChatDeleteConfirmBuilder] Built DeleteChatConfirmPanel. Now wire ChatListView.deleteConfirm to its ChatDeleteConfirm.");
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void Center(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }

    private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size,
                                           FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = style; t.alignment = align;
        t.color = Color.black; t.enableWordWrapping = true;
        return t;
    }

    private static Button NewButton(string name, Transform parent, string label, Color bg, Color fg)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = bg;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var t = NewText("Label", rt, label, 32, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch((RectTransform)t.transform);
        t.color = fg;
        return btn;
    }
}
```

- [ ] **Step 3:** In the Editor, select the chat list screen panel (the one containing the chat `ScrollRect` / where `ChatListView` lives), run `Tools/Chat/Build Delete-Confirm Popup`. Apply rounded corners to the `Content` card and `Cancel`/`Delete` buttons via the project's RoundedCorners convention (see `project_roundedcorners_assembly` — AppDomain-scan the type, radius ≈ half min-dim, Validate/Refresh after a layout pass).

- [ ] **Step 4:** Select the GameObject with `ChatListView` and drag the new `ChatDeleteConfirm` (on `DeleteChatConfirmPanel`) into its `deleteConfirm` field.

- [ ] **Step 5: Recompile.** Now `ChatListView` (Task 7) compiles. Run `mcp__mcp-unity__recompile_scripts` → `get_console_logs`. Expected: zero compile errors across the project.

- [ ] **Step 6: Run all new EditMode tests** to confirm nothing regressed:

Run: `Tools/run-tests-headless.sh 'WappiRecipientTests|DeletedChatGuardTests|ChatHistoryCacheDeleteTests|ChatListCacheEditorTests'`
Expected: all green (16 tests).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/UI/ChatDeleteConfirm.cs Assets/Scripts/UI/ChatDeleteConfirm.cs.meta \
        Assets/Editor/ChatDeleteConfirmBuilder.cs Assets/Editor/ChatDeleteConfirmBuilder.cs.meta \
        Assets/Scenes/Main.unity
git commit -m "feat(chat): add ChatDeleteConfirm popup + builder, wire to ChatListView"
```

---

## Task 12: End-to-end verification (Editor / device)

No new code — exercise the full flow. The gesture, prefab reveal, scroll arbitration, and live `chat/delete` round-trip can't be asserted headlessly.

- [ ] **Step 1:** Enter Play mode (or device build). Open the chats list for a bot with WhatsApp connected.
- [ ] **Step 2:** Swipe a row left → Delete reveals; release short → snaps closed; release past halfway → stays open. Swipe right / tap the open row → closes. Vertical drag still scrolls the list.
- [ ] **Step 3:** Open a row, then swipe another → the first closes (one-open-at-a-time).
- [ ] **Step 4:** Tap Delete → confirm dialog appears with the chat name. Cancel → nothing changes.
- [ ] **Step 5:** Tap Delete → confirm → row collapses out immediately. Watch the console for `chat/delete` success (`status:"done"`). Pull-to-refresh / wait for a sync → the chat does **not** reappear.
- [ ] **Step 6:** Cold-restart the app → the deleted chat is gone (chats.json rewrite worked).
- [ ] **Step 7: Failure path** — temporarily point the URL at a bad host or disable network, delete a chat → the row reappears and `[Wappi] chat/delete failed` is logged. Restore the URL.
- [ ] **Step 8:** Group chat (`@g.us`) delete works (recipient = full id).
- [ ] **Step 9:** Confirm there's no cross-talk: deleting chat A never removes/affects chat B; the open chat (if any) is unaffected.

- [ ] **Step 10:** If everything passes, the feature is complete. Final spec/CLAUDE.md note: add `chat/delete` to the Wappi endpoint list in `CLAUDE.md` (External APIs → Wappi WhatsApp) if you want it documented.

---

## Self-review (completed during authoring)

- **Spec coverage:** gesture (T9/T10), confirm dialog (T11), optimistic delete + rollback (T5), server API exact shape (T5), `OnChatRemoved`/`RemoveChatLocally` (T5/T7), resurrection guard (T2/T6), `DeleteHistory` (T3), `chats.json` rewrite (T4/T5), prefab reveal (T10), EditMode tests (T1–T4), Telegram excluded / media untouched (honored — no tasks add them). ✓
- **Deviations flagged:** no toast (rollback is the failure signal); rollback re-adds at list bottom. Both documented above for reviewer sign-off.
- **Type consistency:** `WappiDeleteChatRequest.recipient`, `WappiDeleteChatResponse.status`, `DeletedChatGuard.{MarkDeleted,ShouldSuppress,Clear,ReconcileWithServer}`, `SwipeToDelete.{IsOpen,Close,ResetClosed,revealWidth}`, `ChatDeleteConfirm.Ask`, `ChatListView.RequestDelete`, `ChatItemView.{deleteButton,swipeToDelete}` — all referenced consistently across tasks. ✓
