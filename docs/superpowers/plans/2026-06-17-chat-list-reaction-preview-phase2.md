# Chat-list reaction preview — Phase 2 (target-text backfill) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Backfill the reacted-to message text for bulk-fetched reaction rows so the chat list shows `You reacted ❤️ to "msg"` even for reactions that arrived while the app was closed — lazily, serially, and cached.

**Architecture:** Evidence (saved `messages/get` responses) showed a reaction carries no `reply_message`, but its target reliably sits in the same recent-messages window. So a single `messages/get?chat_id=X&limit=50` per reaction-row yields both the reaction and its target → resolve text locally. A pure resolver + a persistent per-reaction-id cache feed the Phase-1 seam `ChatViewModel.UpdateReactionContext(text, type)`. A lazy trigger (ChatItemView, on-screen rows only) drives a serial, bot-switch-guarded fetch queue in ChatManager. No `messages/id/get`; reuses the proven endpoint and warms the history cache.

**Tech Stack:** Unity 6 / C#, Newtonsoft for parsing, NUnit EditMode tests (no asmdef), in-Editor test bridge.

**Decisions (from brainstorming):** Trigger = lazy/on-screen. Cache = persisted to disk (each reaction fetched once ever; "not found" is cached too, so no refetch).

**Per-task consent:** Do NOT auto-commit. "Commit" steps = stage only the listed files (+ generated `.meta`), then ask before committing.

---

## File structure

- **Create** `Assets/Scripts/Chat/ReactionTargetResolver.cs` — pure: find reaction by id → its target by `stanzaId` → `(text, type)`. No side effects.
- **Create** `Assets/Scripts/Chat/ReactionTargetCache.cs` — persistent `{cacheRoot}/reaction_targets.json`, keyed by reaction id, in-memory layer over disk.
- **Create** `Assets/Scripts/Main/ChatManager.ReactionResolve.cs` — partial ChatManager: `ResolveReactionTarget(vm)` + serial fetch queue.
- **Modify** `Assets/Scripts/UI/ChatItemView.cs` — lazy trigger on bind/update for reaction rows missing text.
- **Create** `Assets/Tests/Editor/Chat/ReactionTargetResolverTests.cs`
- **Create** `Assets/Tests/Editor/Chat/ReactionTargetCacheTests.cs`

The Phase-1 bulk-merge clear (ParseChatsJson) stays as-is: after it nulls the context, the lazy trigger re-fills instantly from cache (no refetch).

---

## Task 1: ReactionTargetResolver (pure, TDD)

**Files:**
- Test: `Assets/Tests/Editor/Chat/ReactionTargetResolverTests.cs`
- Create: `Assets/Scripts/Chat/ReactionTargetResolver.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/ReactionTargetResolverTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

public class ReactionTargetResolverTests
{
    private static RawMessage Msg(string id, string type, object body = null,
                                  string stanzaId = null, string caption = null) =>
        new RawMessage
        {
            id = id,
            type = type,
            body = body == null ? null : JToken.FromObject(body),
            stanzaId = stanzaId,
            caption = caption
        };

    [Test]
    public void ResolvesTextTarget()
    {
        var msgs = new List<RawMessage>
        {
            Msg("R", "reaction", "❤️", stanzaId: "T"),
            Msg("T", "chat", "See you tomorrow"),
        };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("See you tomorrow", r.text);
        Assert.AreEqual("chat", r.type);
    }

    [Test]
    public void PrefersCaptionOverBody()
    {
        var msgs = new List<RawMessage>
        {
            Msg("R", "reaction", "❤️", stanzaId: "T"),
            Msg("T", "image", new { url = "x" }, caption: "A caption"),
        };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("A caption", r.text);
        Assert.AreEqual("image", r.type);
    }

    [Test]
    public void MediaTargetWithoutCaption_EmptyTextKeepsType()
    {
        var msgs = new List<RawMessage>
        {
            Msg("R", "reaction", "❤️", stanzaId: "T"),
            Msg("T", "image", new { url = "x" }),
        };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("", r.text);
        Assert.AreEqual("image", r.type);
    }

    [Test]
    public void TargetNotInWindow_ReturnsEmpty()
    {
        var msgs = new List<RawMessage> { Msg("R", "reaction", "❤️", stanzaId: "GONE") };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("", r.text);
        Assert.AreEqual("", r.type);
    }

    [Test]
    public void ReactionNotFound_ReturnsEmpty()
    {
        var msgs = new List<RawMessage> { Msg("T", "chat", "hi") };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("", r.text);
        Assert.AreEqual("", r.type);
    }

    [Test]
    public void NullArgs_ReturnEmpty()
    {
        var r = ReactionTargetResolver.Resolve(null, "R");
        Assert.AreEqual("", r.text);
        Assert.AreEqual("", r.type);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Arm bridge filtered to `ReactionTargetResolverTests` (drop `Temp/claude/run-tests.trigger` with that one line), focus Unity, read `Temp/claude/test-summary.json`.
Expected: **compile failure** — `ReactionTargetResolver` undefined.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Chat/ReactionTargetResolver.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Pure resolver for a reaction's target text. Given a fetched message window, finds the
/// reaction (id == reactionId) and its target (id == reaction.stanzaId) and returns the
/// target's display text (caption preferred, else body if it's a string) plus the Wappi
/// type keyword. Empty text+type means "reaction or target not in this window" — the
/// caller treats that as a definitive "show who + emoji only" outcome.
/// </summary>
public static class ReactionTargetResolver
{
    public struct Result { public string text; public string type; }

    public static Result Resolve(IReadOnlyList<RawMessage> messages, string reactionId)
    {
        var empty = new Result { text = "", type = "" };
        if (messages == null || string.IsNullOrEmpty(reactionId)) return empty;

        RawMessage reaction = FindById(messages, reactionId);
        if (reaction == null || string.IsNullOrEmpty(reaction.stanzaId)) return empty;

        RawMessage target = FindById(messages, reaction.stanzaId);
        if (target == null) return empty;

        string text = !string.IsNullOrEmpty(target.caption)
            ? target.caption
            : (target.body != null && target.body.Type == JTokenType.String ? target.body.ToString() : "");
        string type = string.IsNullOrEmpty(target.type) ? "chat" : target.type;
        return new Result { text = text, type = type };
    }

    private static RawMessage FindById(IReadOnlyList<RawMessage> messages, string id)
    {
        for (int i = 0; i < messages.Count; i++)
            if (messages[i] != null && messages[i].id == id) return messages[i];
        return null;
    }
}
```

- [ ] **Step 4: Run to verify it passes** — same command. Expected: **PASS** (6 tests).

- [ ] **Step 5: Commit** (consent)

```bash
git add Assets/Tests/Editor/Chat/ReactionTargetResolverTests.cs Assets/Tests/Editor/Chat/ReactionTargetResolverTests.cs.meta Assets/Scripts/Chat/ReactionTargetResolver.cs Assets/Scripts/Chat/ReactionTargetResolver.cs.meta
git commit -m "feat(chat): pure resolver for a reaction's target text"
```

---

## Task 2: ReactionTargetCache (persistent, TDD)

**Files:**
- Test: `Assets/Tests/Editor/Chat/ReactionTargetCacheTests.cs`
- Create: `Assets/Scripts/Chat/ReactionTargetCache.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/ReactionTargetCacheTests.cs`:

```csharp
using System;
using System.IO;
using NUnit.Framework;

public class ReactionTargetCacheTests
{
    private static string FreshDir(string tag)
    {
        string dir = Path.Combine(Path.GetTempPath(), "rtc_" + tag + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public void PutThenGet_ReturnsValues()
    {
        string dir = FreshDir("putget");
        ReactionTargetCache.Put(dir, "r1", "Hello", "chat");

        bool ok = ReactionTargetCache.TryGet(dir, "r1", out string text, out string type);

        Assert.IsTrue(ok);
        Assert.AreEqual("Hello", text);
        Assert.AreEqual("chat", type);
    }

    [Test]
    public void Put_WritesFileToDisk()
    {
        string dir = FreshDir("disk");
        ReactionTargetCache.Put(dir, "r1", "Hello", "chat");

        string path = Path.Combine(dir, "reaction_targets.json");
        Assert.IsTrue(File.Exists(path), "cache file should be written");
        StringAssert.Contains("r1", File.ReadAllText(path));
        StringAssert.Contains("Hello", File.ReadAllText(path));
    }

    [Test]
    public void LoadsFromExistingFile_OnFirstAccess()
    {
        // A never-seen dir with a pre-existing file exercises the disk-load path
        // (in-memory map is keyed per dir, so this dir starts cold).
        string dir = FreshDir("load");
        File.WriteAllText(Path.Combine(dir, "reaction_targets.json"),
            "{\"entries\":[{\"reactionId\":\"r9\",\"text\":\"Persisted\",\"type\":\"image\"}]}");

        bool ok = ReactionTargetCache.TryGet(dir, "r9", out string text, out string type);

        Assert.IsTrue(ok);
        Assert.AreEqual("Persisted", text);
        Assert.AreEqual("image", type);
    }

    [Test]
    public void UnknownId_ReturnsFalse()
    {
        string dir = FreshDir("unknown");
        Assert.IsFalse(ReactionTargetCache.TryGet(dir, "nope", out _, out _));
    }

    [Test]
    public void EmptyOutcome_IsCachedAndDistinctFromMiss()
    {
        string dir = FreshDir("emptyok");
        ReactionTargetCache.Put(dir, "r2", "", ""); // "resolved: nothing to show"

        bool ok = ReactionTargetCache.TryGet(dir, "r2", out string text, out string type);

        Assert.IsTrue(ok, "an empty outcome is still a cached hit (no refetch)");
        Assert.AreEqual("", text);
        Assert.AreEqual("", type);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — bridge filtered to `ReactionTargetCacheTests`. Expected: **compile failure** — `ReactionTargetCache` undefined.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Chat/ReactionTargetCache.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists the resolved reacted-to text/type per reaction message id so a chat-list
/// reaction row's "… to “msg”" survives restarts and is fetched at most once ever.
/// Keyed by the reaction's own id (a new reaction = new id = new entry). An empty
/// text+type entry records a definitive "nothing to show" so the row is never refetched.
/// File: {baseDir}/reaction_targets.json (baseDir is the bot-scoped cache root).
/// </summary>
public static class ReactionTargetCache
{
    [Serializable] public class Entry { public string reactionId; public string text; public string type; }
    [Serializable] private class FileShape { public List<Entry> entries = new List<Entry>(); }

    // In-memory layer keyed by baseDir → (reactionId → Entry); avoids disk IO per row bind.
    private static readonly Dictionary<string, Dictionary<string, Entry>> _mem =
        new Dictionary<string, Dictionary<string, Entry>>();

    public static bool TryGet(string baseDir, string reactionId, out string text, out string type)
    {
        text = null; type = null;
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(reactionId)) return false;
        var map = LoadMap(baseDir);
        if (map.TryGetValue(reactionId, out var e)) { text = e.text; type = e.type; return true; }
        return false;
    }

    public static void Put(string baseDir, string reactionId, string text, string type)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(reactionId)) return;
        var map = LoadMap(baseDir);
        map[reactionId] = new Entry { reactionId = reactionId, text = text ?? "", type = type ?? "" };
        Save(baseDir, map);
    }

    private static Dictionary<string, Entry> LoadMap(string baseDir)
    {
        if (_mem.TryGetValue(baseDir, out var cached)) return cached;

        var map = new Dictionary<string, Entry>();
        try
        {
            string path = PathFor(baseDir);
            if (File.Exists(path))
            {
                var shape = JsonUtility.FromJson<FileShape>(File.ReadAllText(path));
                if (shape?.entries != null)
                    foreach (var e in shape.entries)
                        if (e != null && !string.IsNullOrEmpty(e.reactionId)) map[e.reactionId] = e;
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[ReactionTargetCache] load failed: {ex.Message}"); }

        _mem[baseDir] = map;
        return map;
    }

    private static void Save(string baseDir, Dictionary<string, Entry> map)
    {
        try
        {
            var shape = new FileShape { entries = new List<Entry>(map.Values) };
            string path = PathFor(baseDir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(shape));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch (Exception ex) { Debug.LogWarning($"[ReactionTargetCache] save failed: {ex.Message}"); }
    }

    private static string PathFor(string baseDir) => Path.Combine(baseDir, "reaction_targets.json");
}
```

- [ ] **Step 4: Run to verify it passes** — Expected: **PASS** (5 tests).

- [ ] **Step 5: Commit** (consent)

```bash
git add Assets/Tests/Editor/Chat/ReactionTargetCacheTests.cs Assets/Tests/Editor/Chat/ReactionTargetCacheTests.cs.meta Assets/Scripts/Chat/ReactionTargetCache.cs Assets/Scripts/Chat/ReactionTargetCache.cs.meta
git commit -m "feat(chat): persistent per-reaction target-text cache"
```

---

## Task 3: ChatManager serial resolve queue

**Files:**
- Create: `Assets/Scripts/Main/ChatManager.ReactionResolve.cs`

- [ ] **Step 1: Implement the partial**

Create `Assets/Scripts/Main/ChatManager.ReactionResolve.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    // Phase 2: lazily backfill the reacted-to text for chat-list reaction rows.
    private readonly Queue<string> _reactionResolveQueue = new Queue<string>();   // chatIds pending
    private readonly HashSet<string> _reactionResolveInFlight = new HashSet<string>(); // reactionIds
    private bool _reactionResolveDraining;

    /// <summary>
    /// Entry point called by ChatItemView when a reaction row missing its target text comes
    /// on screen. Cache hit → fills instantly; miss → enqueues one serial messages/get fetch.
    /// </summary>
    public void ResolveReactionTarget(ChatViewModel chatVm)
    {
        if (chatVm == null) return;
        if (chatVm.LastMessageType != "reaction") return;
        if (chatVm.ReactionTargetText != null) return;          // already resolved (incl. "")
        string reactionId = chatVm.LastMessageId;
        if (string.IsNullOrEmpty(reactionId)) return;

        if (ReactionTargetCache.TryGet(GetCacheRoot(), reactionId, out string cachedText, out string cachedType))
        {
            chatVm.UpdateReactionContext(cachedText, cachedType);
            return;
        }

        if (_reactionResolveInFlight.Contains(reactionId)) return;
        _reactionResolveInFlight.Add(reactionId);
        _reactionResolveQueue.Enqueue(chatVm.ChatId);
        if (!_reactionResolveDraining) StartCoroutine(DrainReactionResolveQueue());
    }

    private IEnumerator DrainReactionResolveQueue()
    {
        _reactionResolveDraining = true;
        string profileId = GetActiveProfileId();
        string cacheRoot = GetCacheRoot();

        while (_reactionResolveQueue.Count > 0)
        {
            // Abandon the queue if the active bot changed mid-drain.
            if (GetActiveProfileId() != profileId) break;

            string chatId = _reactionResolveQueue.Dequeue();
            ChatViewModel chatVm = GetChat(chatId);
            if (chatVm == null || chatVm.LastMessageType != "reaction"
                || chatVm.ReactionTargetText != null || string.IsNullOrEmpty(chatVm.LastMessageId))
                continue;

            string reactionId = chatVm.LastMessageId;
            if (string.IsNullOrEmpty(profileId)) { _reactionResolveInFlight.Remove(reactionId); continue; }

            string escapedId = UnityWebRequest.EscapeURL(chatId);
            string url = $"https://wappi.pro/api/sync/messages/get?profile_id={profileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset=0";

            bool definitive = false;
            ReactionTargetResolver.Result res = new ReactionTargetResolver.Result { text = "", type = "" };

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
                www.timeout = 30;
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var resp = JsonConvert.DeserializeObject<MessagesResponseRaw>(www.downloadHandler.text);
                        if (resp?.messages != null) res = ReactionTargetResolver.Resolve(resp.messages, reactionId);
                        definitive = true;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[ChatManager] reaction-target parse failed: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[Wappi] reaction-target messages/get failed [{www.responseCode}] {url}: {www.error}");
                }
            }

            _reactionResolveInFlight.Remove(reactionId);

            // Only cache/apply on a definitive answer; a network failure leaves the row
            // unresolved so it retries on a later on-screen bind.
            if (definitive)
            {
                ReactionTargetCache.Put(cacheRoot, reactionId, res.text, res.type);
                if (GetActiveProfileId() == profileId)
                {
                    ChatViewModel current = GetChat(chatId);
                    if (current != null && current.LastMessageType == "reaction"
                        && current.LastMessageId == reactionId)
                        current.UpdateReactionContext(res.text, res.type);
                }
            }
        }

        // Drained or aborted: drop stragglers so a new bot/list starts clean.
        _reactionResolveQueue.Clear();
        _reactionResolveInFlight.Clear();
        _reactionResolveDraining = false;
    }
}
```

- [ ] **Step 2: Verify compile + suite green**

Arm bridge with no filter (full suite), focus Unity, read summary.
Expected: **PASS** — project compiles (new partial references resolver + cache from Tasks 1–2), no regressions.

- [ ] **Step 3: Commit** (consent)

```bash
git add Assets/Scripts/Main/ChatManager.ReactionResolve.cs Assets/Scripts/Main/ChatManager.ReactionResolve.cs.meta
git commit -m "feat(chat): serial lazy queue to backfill reaction target text"
```

---

## Task 4: ChatItemView lazy trigger

**Files:**
- Modify: `Assets/Scripts/UI/ChatItemView.cs` (Bind ~line 109, OnVmUpdated ~line 159, + new method)

- [ ] **Step 1: Trigger after the preview is set in Bind**

Replace:

```csharp
        UpdatePreviewText(vm.LastMessage ?? "");

        vm.OnUpdated += OnVmUpdated;
```

with:

```csharp
        UpdatePreviewText(vm.LastMessage ?? "");
        MaybeResolveReactionTarget();

        vm.OnUpdated += OnVmUpdated;
```

- [ ] **Step 2: Trigger after the preview is refreshed in OnVmUpdated**

Replace:

```csharp
        // --- THE FIX: Format the preview text on updates too ---
        UpdatePreviewText(vm.LastMessage ?? "");

        ApplyUnreadBadge(vm.UnreadCount);
```

with:

```csharp
        // --- THE FIX: Format the preview text on updates too ---
        UpdatePreviewText(vm.LastMessage ?? "");
        MaybeResolveReactionTarget();

        ApplyUnreadBadge(vm.UnreadCount);
```

- [ ] **Step 3: Add the trigger method**

Add directly after `OnVmUpdated` (before `OnLastMessageChanged`):

```csharp
    // Phase 2: when an on-screen reaction row has no resolved target text yet, ask the
    // manager to backfill it. The manager caches/dedupes/queues, so this is a cheap ping.
    // Reference-null (not IsNullOrEmpty): a resolved media row keeps "" + a type and must
    // not re-ping, while a cleared/unresolved row (null) should.
    private void MaybeResolveReactionTarget()
    {
        if (vm == null) return;
        if (vm.LastMessageType != "reaction") return;
        if (vm.ReactionTargetText != null) return;
        if (string.IsNullOrEmpty(vm.LastMessageId)) return;
        if (ChatManager.Instance != null) ChatManager.Instance.ResolveReactionTarget(vm);
    }
```

- [ ] **Step 4: Verify compile + suite green**

Full-suite bridge run. Expected: **PASS**, project compiles.

- [ ] **Step 5: Commit** (consent)

```bash
git add Assets/Scripts/UI/ChatItemView.cs
git commit -m "feat(chat): lazily backfill reaction target text for on-screen rows"
```

---

## Task 5: Full verification

- [ ] **Step 1: Run the entire EditMode suite** (no filter). Expected: **all PASS** — Phase 2's 11 new tests (6 resolver + 5 cache) plus the existing suite, no regressions.

- [ ] **Step 2: Manual check in the running app**
- Open the chat list with a chat whose last activity is a reaction that happened while the app was closed. Initially it reads `You reacted ❤️` / `Reacted ❤️`; within a moment (after the lazy fetch) it upgrades in place to `… to "their message"` (or `… to 📷 Photo` for media), without reordering.
- Background-sync the list again: the row must NOT flicker back to who+emoji (cache re-fill is instant).
- Force-close and reopen the app: the resolved text appears immediately for previously-seen reaction rows (persisted cache, no refetch).
- A reaction whose target is older than the 50-message window stays `You reacted ❤️` (definitive not-found, cached, no repeated fetching).

- [ ] **Step 3: Final commit if needed** (consent)

---

## Spec coverage check
- One-hop messages/get resolve (no messages/id/get) → Task 1 + Task 3.
- Persistent cache, "not found" cached, no refetch → Task 2 + Task 3 (definitive-only Put).
- Lazy on-screen trigger → Task 4.
- Serial queue + bot-switch guard → Task 3.
- Phase-1 flicker eliminated via instant cache re-fill → Task 3 (ResolveReactionTarget cache hit) + existing Phase-1 clear.
- Verification → Task 5.
