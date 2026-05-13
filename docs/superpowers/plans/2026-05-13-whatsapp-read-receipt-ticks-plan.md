# WhatsApp Read-Receipt Ticks — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render WhatsApp-style delivery ticks (clock / single / double / blue / red `!`) inside outgoing message bubbles, with a tap-to-retry outbox for failed sends that persists across chat reopens.

**Architecture:** A `DeliveryStatus` enum threads through the existing `Raw → Normalized → ViewModel → View` pipeline. `MessageItemView.Bind()` appends a `<sprite name="...">` tag inside the bubble's existing `timeText`. `OutboxStore` (per-bot JSON files) owns optimistic-send state; `ChatManager.OnMessageStatusChanged` lets already-rendered bubbles update in place. The existing send POST is extracted into `PostTextMessageRoutine` so retry reuses it.

**Tech Stack:** Unity 6 (URP), C#, TextMeshPro sprite tags, Newtonsoft.Json, UnityWebRequest + coroutines, NUnit / Unity Test Framework (EditMode).

**Spec:** [`docs/superpowers/specs/2026-05-13-whatsapp-read-receipt-ticks-design.md`](../specs/2026-05-13-whatsapp-read-receipt-ticks-design.md)

---

## File Structure

**New files (8):**
| Path | Purpose |
|---|---|
| `Assets/Scripts/Chat/DeliveryStatus.cs` | Enum of all six tick states |
| `Assets/Scripts/Chat/DeliveryTickFormatter.cs` | Pure functions: enum → sprite tag string, Wappi raw → enum |
| `Assets/Scripts/Chat/OutboxStore.cs` | Per-bot, per-chat JSON outbox of unresolved sends |
| `Assets/Scripts/Main/ChatManager.Outbox.cs` | Partial class: outbox field, splice helper, retry method |
| `Assets/Tests/EditMode/Automation.EditModeTests.asmdef` | Test assembly definition |
| `Assets/Tests/EditMode/Chat/DeliveryTickFormatterTests.cs` | Unit tests for the formatter |
| `Assets/Tests/EditMode/Chat/OutboxStoreTests.cs` | Unit tests for the outbox persistence |
| `Assets/Scripts/Chat.asmdef` | **Conditional** — only if Chat scripts aren't already in a referenced assembly (probe in Task 0) |

**Modified files (4):**
| Path | Change |
|---|---|
| `Assets/Scripts/Chat/RawMessage.cs` | Add `deliveryStatusRaw` field with `[JsonProperty("delivery_status")]` |
| `Assets/Scripts/Chat/NormalizedMessage.cs` | Add `deliveryStatus` enum field |
| `Assets/Scripts/UI/MessageViewModel.cs` | Add `deliveryStatus` enum field |
| `Assets/Scripts/Main/ChatManager.cs` | Parse status in `Normalize()`, pass in `CreateViewModel()`, declare event, refactor send routine, hook outbox calls |
| `Assets/Scripts/UI/MessageItemView.cs` | Render tick in `Bind()` time block, subscribe to status event, add `SetDeliveryStatus` + retry-button logic |

---

## Task 0: Set up EditMode test infrastructure

**Files:**
- Create: `Assets/Tests/EditMode/Automation.EditModeTests.asmdef`

The project currently has no `Assets/Tests/` directory. Without an EditMode assembly definition, NUnit tests in `Assets/Tests/EditMode/` won't be discovered.

- [ ] **Step 1: Create the test directory**

```bash
mkdir -p /Users/ayan/Projects/Automation/Assets/Tests/EditMode/Chat
```

- [ ] **Step 2: Probe whether Chat scripts live in the default assembly or a custom one**

```bash
ls /Users/ayan/Projects/Automation/Assets/Scripts/*.asmdef 2>/dev/null
ls /Users/ayan/Projects/Automation/Assets/Scripts/Chat/*.asmdef 2>/dev/null
```

Expected output: empty (no asmdef in `Assets/Scripts/` or `Assets/Scripts/Chat/`). If empty, the production scripts live in the default `Assembly-CSharp` and the test asmdef must reference that. If non-empty, reference the specific asmdef instead.

- [ ] **Step 3: Create the EditMode assembly definition**

Path: `Assets/Tests/EditMode/Automation.EditModeTests.asmdef`

```json
{
    "name": "Automation.EditModeTests",
    "rootNamespace": "Automation.Tests.EditMode",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

The empty `"references"` array list intentionally only contains the TestRunner refs. Since production code is in `Assembly-CSharp` (default), and our asmdef sets `"overrideReferences": true` + has no asmdef reference for `Assembly-CSharp`, **Unity automatically lets EditMode tests access the default assembly**. No `assemblyReferences` entry is needed.

- [ ] **Step 4: Open Unity Editor → Window → General → Test Runner**

Expected: The Test Runner window opens. Click "EditMode" tab. The `Automation.EditModeTests` assembly should appear in the list (initially with zero tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/Tests/EditMode/Automation.EditModeTests.asmdef
git commit -m "test: scaffold EditMode test assembly

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 1: `DeliveryStatus` enum + `DeliveryTickFormatter` (TDD)

**Files:**
- Create: `Assets/Scripts/Chat/DeliveryStatus.cs`
- Create: `Assets/Scripts/Chat/DeliveryTickFormatter.cs`
- Create: `Assets/Tests/EditMode/Chat/DeliveryTickFormatterTests.cs`

Pure value types and a static formatter — perfect for TDD.

- [ ] **Step 1: Write the failing test file**

Path: `Assets/Tests/EditMode/Chat/DeliveryTickFormatterTests.cs`

```csharp
using NUnit.Framework;

public class DeliveryTickFormatterTests
{
    [Test]
    public void GetSprite_None_ReturnsNull()
    {
        Assert.IsNull(DeliveryTickFormatter.GetSprite(DeliveryStatus.None));
    }

    [Test]
    public void GetSprite_Pending_ReturnsClockTag()
    {
        Assert.AreEqual("<sprite name=\"tick_pending\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Pending));
    }

    [Test]
    public void GetSprite_Sent_ReturnsSingleTickTag()
    {
        Assert.AreEqual("<sprite name=\"tick_sent\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Sent));
    }

    [Test]
    public void GetSprite_Delivered_ReturnsDoubleTickTag()
    {
        Assert.AreEqual("<sprite name=\"tick_double\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Delivered));
    }

    [Test]
    public void GetSprite_Read_ReturnsBlueDoubleTickTag()
    {
        Assert.AreEqual("<sprite name=\"tick_double_blue\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Read));
    }

    [Test]
    public void GetSprite_Failed_ReturnsFailedTag()
    {
        Assert.AreEqual("<sprite name=\"tick_failed\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Failed));
    }

    [TestCase("sent",      DeliveryStatus.Sent)]
    [TestCase("SENT",      DeliveryStatus.Sent)]
    [TestCase("Sent",      DeliveryStatus.Sent)]
    [TestCase("delivered", DeliveryStatus.Delivered)]
    [TestCase("read",      DeliveryStatus.Read)]
    public void ParseWappiString_KnownValue_ReturnsMatchingEnum(string raw, DeliveryStatus expected)
    {
        Assert.AreEqual(expected, DeliveryTickFormatter.ParseWappiString(raw));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("unknown_status_value")]
    public void ParseWappiString_UnknownOrEmpty_ReturnsNone(string raw)
    {
        Assert.AreEqual(DeliveryStatus.None, DeliveryTickFormatter.ParseWappiString(raw));
    }
}
```

- [ ] **Step 2: Verify the test fails to compile**

In Unity → Test Runner → EditMode tab → Run All.

Expected: Compile errors — `DeliveryStatus` and `DeliveryTickFormatter` undefined. This is the failing-state for a TDD cycle on type-introduction.

- [ ] **Step 3: Create the enum**

Path: `Assets/Scripts/Chat/DeliveryStatus.cs`

```csharp
public enum DeliveryStatus
{
    None,
    Pending,
    Sent,
    Delivered,
    Read,
    Failed
}
```

- [ ] **Step 4: Create the formatter**

Path: `Assets/Scripts/Chat/DeliveryTickFormatter.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps DeliveryStatus values to TMP sprite tags from ChatTicks.asset, and
/// parses the Wappi string-typed delivery_status field into the same enum.
/// Sprite names must match the glyph character names in ChatTicks.asset,
/// which ChatTicksFallbackRegistrar registers as a TMP fallback at startup.
/// </summary>
public static class DeliveryTickFormatter
{
    private const string TagPending = "<sprite name=\"tick_pending\">";
    private const string TagSent    = "<sprite name=\"tick_sent\">";
    private const string TagDouble  = "<sprite name=\"tick_double\">";
    private const string TagBlue    = "<sprite name=\"tick_double_blue\">";
    private const string TagFailed  = "<sprite name=\"tick_failed\">";

    private static readonly HashSet<string> LoggedUnknown = new();

    public static string GetSprite(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Pending   => TagPending,
        DeliveryStatus.Sent      => TagSent,
        DeliveryStatus.Delivered => TagDouble,
        DeliveryStatus.Read      => TagBlue,
        DeliveryStatus.Failed    => TagFailed,
        _                        => null,
    };

    public static DeliveryStatus ParseWappiString(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return DeliveryStatus.None;
        switch (raw.ToLowerInvariant())
        {
            case "sent":      return DeliveryStatus.Sent;
            case "delivered": return DeliveryStatus.Delivered;
            case "read":      return DeliveryStatus.Read;
            default:
                if (LoggedUnknown.Add(raw))
                    Debug.LogWarning($"[DeliveryTickFormatter] Unknown Wappi delivery_status: '{raw}'");
                return DeliveryStatus.None;
        }
    }
}
```

- [ ] **Step 5: Run the tests, verify all 14 pass**

Unity → Test Runner → EditMode → Run All.

Expected: 14 tests pass (6 GetSprite, 5 ParseWappiString known, 3 ParseWappiString unknown/empty/null).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/DeliveryStatus.cs \
        Assets/Scripts/Chat/DeliveryStatus.cs.meta \
        Assets/Scripts/Chat/DeliveryTickFormatter.cs \
        Assets/Scripts/Chat/DeliveryTickFormatter.cs.meta \
        Assets/Tests/EditMode/Chat/DeliveryTickFormatterTests.cs \
        Assets/Tests/EditMode/Chat/DeliveryTickFormatterTests.cs.meta
git commit -m "feat(chat): DeliveryStatus enum + DeliveryTickFormatter

Adds the type model for read-receipt ticks and pure functions mapping
DeliveryStatus to TMP sprite tags and parsing Wappi's delivery_status
string into the enum. Glyphs live in the existing ChatTicks.asset,
registered as a TMP fallback at startup.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Plumb `deliveryStatus` through the data pipeline

**Files:**
- Modify: `Assets/Scripts/Chat/RawMessage.cs`
- Modify: `Assets/Scripts/Chat/NormalizedMessage.cs`
- Modify: `Assets/Scripts/UI/MessageViewModel.cs`
- Modify: `Assets/Scripts/Main/ChatManager.cs` (`Normalize` + `CreateViewModel`)

After this task, the field exists end-to-end but isn't rendered yet. Tests are smoke-only since this is pure plumbing.

- [ ] **Step 1: Add `deliveryStatusRaw` to `RawMessage`**

File: `Assets/Scripts/Chat/RawMessage.cs`

Find this block (around line 8):
```csharp
public class RawMessage
{
    public string id;
    public string type;
    public string chatId;
    public string senderName;
    public bool fromMe;
    public long time;
    public string caption;
```

Add a new field directly after `caption`:
```csharp
    public string caption;

    [JsonProperty("delivery_status")]
    public string deliveryStatusRaw;
```

- [ ] **Step 2: Add `deliveryStatus` to `NormalizedMessage`**

File: `Assets/Scripts/Chat/NormalizedMessage.cs`

After the `pageCount` field at the end of the class, add:
```csharp
    public int pageCount;

    public DeliveryStatus deliveryStatus;
}
```

- [ ] **Step 3: Add `deliveryStatus` to `MessageViewModel`**

File: `Assets/Scripts/UI/MessageViewModel.cs`

After the `pageCount` field at the end of the class, add:
```csharp
    public int pageCount;

    public DeliveryStatus deliveryStatus;
}
```

- [ ] **Step 4: Parse `deliveryStatusRaw` inside `ChatManager.Normalize()`**

File: `Assets/Scripts/Main/ChatManager.cs`

Locate the `Normalize` method (around line 433). Find this block at the top of the method:

```csharp
NormalizedMessage msg = new NormalizedMessage
{
    id = raw.id,
    chatId = raw.chatId,
    senderName = raw.senderName,
    messageType = ParseMessageType(raw.type),
    fromMe = raw.fromMe,
    time = raw.time
};
```

Add the delivery status parse immediately after the object-initializer block (so we can branch on `raw.fromMe`):

```csharp
NormalizedMessage msg = new NormalizedMessage
{
    id = raw.id,
    chatId = raw.chatId,
    senderName = raw.senderName,
    messageType = ParseMessageType(raw.type),
    fromMe = raw.fromMe,
    time = raw.time
};

// Outgoing messages carry a Wappi delivery_status string. Incoming messages
// never render a tick — leave at DeliveryStatus.None.
if (raw.fromMe)
    msg.deliveryStatus = DeliveryTickFormatter.ParseWappiString(raw.deliveryStatusRaw);
```

- [ ] **Step 5: Pass `deliveryStatus` through `CreateViewModel()`**

File: `Assets/Scripts/Main/ChatManager.cs` (around line 408)

Find this block:

```csharp
MessageViewModel CreateViewModel(NormalizedMessage msg)
{
    return new MessageViewModel
    {
        messageId = msg.id,
        chatId = msg.chatId,
        senderName = msg.senderName,
        type = msg.messageType,
        text = msg.text,
        fileName = msg.fileName,
        mediaUrl = msg.mediaUrl,
        thumbnailUrl = msg.thumbnailUrl,
        aspectRatio = msg.aspectRatio,
        expireTime = msg.expireTime,
        mimeType = msg.mimeType,
        videoUrl = msg.videoUrl,
        duration = msg.duration,
        isSticker = msg.isSticker,
        isIncoming = !msg.fromMe,
        timestamp = msg.time,
        fileSize = msg.fileSize,
        pageCount = msg.pageCount
    };
}
```

Add `deliveryStatus` as the last property in the initializer:

```csharp
        fileSize = msg.fileSize,
        pageCount = msg.pageCount,
        deliveryStatus = msg.deliveryStatus
    };
}
```

- [ ] **Step 6: Verify the project compiles**

In Unity Editor, wait for the auto-recompile or run:

```bash
ls /Users/ayan/Projects/Automation/Library/ScriptAssemblies/Assembly-CSharp.dll
```

Expected: file exists with a recent mtime. If Unity logs compile errors, fix and retry before moving on.

- [ ] **Step 7: Run the existing EditMode tests to confirm nothing broke**

Unity → Test Runner → EditMode → Run All.

Expected: 14 tests still pass.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Chat/RawMessage.cs \
        Assets/Scripts/Chat/NormalizedMessage.cs \
        Assets/Scripts/UI/MessageViewModel.cs \
        Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): plumb deliveryStatus from Wappi to ViewModel

Adds delivery_status JSON parsing in RawMessage, threads the enum
through NormalizedMessage and MessageViewModel, parses for outgoing
messages only inside ChatManager.Normalize.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Render the tick inside `timeText` (static-at-load path)

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs`

After this task, outgoing bubbles render their server-supplied tick on first appearance. No live mutation yet.

- [ ] **Step 1: Update the time-formatter block to append the tick sprite**

File: `Assets/Scripts/UI/MessageItemView.cs`

Locate the `timeText` block around line 268:

```csharp
        if (timeText != null)
        {
            DateTime localTime = DateTimeOffset.FromUnixTimeSeconds(vm.timestamp).LocalDateTime;
            timeText.text = localTime.ToString("HH:mm");
        }
```

Replace with:

```csharp
        if (timeText != null)
        {
            DateTime localTime = DateTimeOffset.FromUnixTimeSeconds(vm.timestamp).LocalDateTime;
            string formattedTime = localTime.ToString("HH:mm");
            string tickTag = vm.isIncoming ? null : DeliveryTickFormatter.GetSprite(vm.deliveryStatus);
            timeText.text = tickTag != null ? $"{formattedTime} {tickTag}" : formattedTime;
        }
```

- [ ] **Step 2: Manual smoke test — open Unity Editor**

1. Open `Assets/Scenes/Main.unity` in Unity.
2. Enter Play mode.
3. Navigate to the WhatsApp page (BottomNavPanel) and open any chat with outgoing messages.
4. Visually verify that **outgoing bubbles now show a tick next to the time** (state depends on Wappi's `delivery_status` for those messages — likely `read` for older outgoing messages, so expect double blue tick).
5. Verify **incoming bubbles render no tick**.

Expected: outgoing bubbles show ticks, incoming bubbles unchanged.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "feat(chat): render delivery tick inside outgoing bubble timeText

Appends a TMP sprite tag (tick_sent / tick_double / tick_double_blue)
to the existing time label for outgoing messages. Incoming messages
render no tick. Static-at-load only; live mutation comes next.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: `OutboxStore` class (TDD)

**Files:**
- Create: `Assets/Scripts/Chat/OutboxStore.cs`
- Create: `Assets/Tests/EditMode/Chat/OutboxStoreTests.cs`

Plain C# class, instance-based, per-bot scoped via a `getCacheRoot` Func injected at construction. Storage: `{cacheRoot}/outbox_{sanitizedChatId}.json`.

- [ ] **Step 1: Write the failing test file**

Path: `Assets/Tests/EditMode/Chat/OutboxStoreTests.cs`

```csharp
using System.IO;
using NUnit.Framework;

public class OutboxStoreTests
{
    private string _tempRoot;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "OutboxStoreTests_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private OutboxStore MakeStore() => new OutboxStore(() => _tempRoot);

    private OutboxStore.OutboxEntry MakeEntry(string tempId = "t1", string chatId = "+15551@c.us")
        => new OutboxStore.OutboxEntry
        {
            tempId = tempId,
            chatId = chatId,
            text = "hi",
            timestamp = 12345,
            attemptCount = 1,
            profileId = "profileX"
        };

    [Test]
    public void GetFor_EmptyChat_ReturnsEmptyList()
    {
        var store = MakeStore();
        var entries = store.GetFor("+15551@c.us");
        Assert.IsNotNull(entries);
        Assert.AreEqual(0, entries.Count);
    }

    [Test]
    public void Add_ThenGetFor_ReturnsOneEntry()
    {
        var store = MakeStore();
        var entry = MakeEntry();
        store.Add(entry);

        var entries = store.GetFor(entry.chatId);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("t1", entries[0].tempId);
        Assert.AreEqual("hi", entries[0].text);
    }

    [Test]
    public void Add_PersistsToDisk()
    {
        var store = MakeStore();
        var entry = MakeEntry();
        store.Add(entry);

        // A second store instance against the same root must see the entry.
        var store2 = MakeStore();
        var entries = store2.GetFor(entry.chatId);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("t1", entries[0].tempId);
    }

    [Test]
    public void Remove_DeletesTheEntry()
    {
        var store = MakeStore();
        var entry = MakeEntry();
        store.Add(entry);
        store.Remove("t1");

        Assert.AreEqual(0, store.GetFor(entry.chatId).Count);
    }

    [Test]
    public void Find_ReturnsMatchingEntry()
    {
        var store = MakeStore();
        store.Add(MakeEntry(tempId: "t1"));
        store.Add(MakeEntry(tempId: "t2"));

        var found = store.Find("t2");
        Assert.IsNotNull(found);
        Assert.AreEqual("t2", found.tempId);
    }

    [Test]
    public void Find_MissingTempId_ReturnsNull()
    {
        var store = MakeStore();
        store.Add(MakeEntry(tempId: "t1"));
        Assert.IsNull(store.Find("missing"));
    }

    [Test]
    public void Update_BumpsAttemptCount_AndPersists()
    {
        var store = MakeStore();
        var entry = MakeEntry();
        store.Add(entry);

        entry.attemptCount = 5;
        store.Update(entry);

        var store2 = MakeStore();
        var reloaded = store2.GetFor(entry.chatId)[0];
        Assert.AreEqual(5, reloaded.attemptCount);
    }

    [Test]
    public void CorruptedJsonFile_GetForReturnsEmpty()
    {
        var chatId = "+15551@c.us";
        var path = Path.Combine(_tempRoot, "outbox_" + SanitizeForTest(chatId) + ".json");
        File.WriteAllText(path, "{ this is not valid json ");

        var store = MakeStore();
        Assert.AreEqual(0, store.GetFor(chatId).Count);
    }

    [Test]
    public void ChatIdWithSpecialChars_IsSanitizedForFilename()
    {
        // Group chat ids contain '@g.us' and a hyphen — the file system must accept them.
        var store = MakeStore();
        var entry = MakeEntry(chatId: "12345-67890@g.us");
        store.Add(entry);

        var entries = store.GetFor("12345-67890@g.us");
        Assert.AreEqual(1, entries.Count);
    }

    // Mirrors the SanitizeChatId used inside the SUT — keep in sync.
    private static string SanitizeForTest(string chatId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(chatId.Length);
        foreach (var c in chatId)
            sb.Append(System.Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
```

- [ ] **Step 2: Verify the test fails to compile**

Unity → Test Runner → EditMode → Run All.

Expected: Compile errors — `OutboxStore` undefined.

- [ ] **Step 3: Create `OutboxStore`**

Path: `Assets/Scripts/Chat/OutboxStore.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Per-bot, per-chat persisted queue of unresolved outgoing sends.
/// Entries are added when SendTextMessage fires its optimistic UI update,
/// removed when Wappi acknowledges with a real message id, and left in
/// place (so the bubble can render as Failed and offer tap-to-retry) when
/// the POST fails.
///
/// Storage layout: {cacheRoot}/outbox_{sanitizedChatId}.json — mirrors the
/// per-bot pattern of ChatHistoryCache. Writes are atomic via .tmp +
/// File.Replace.
/// </summary>
public class OutboxStore
{
    [Serializable]
    public class OutboxEntry
    {
        public string tempId;
        public string chatId;
        public string text;
        public long timestamp;
        public int attemptCount;
        public string profileId;
    }

    [Serializable]
    private class OutboxFile
    {
        public List<OutboxEntry> entries = new();
    }

    private readonly Func<string> _getCacheRoot;
    private readonly Dictionary<string, List<OutboxEntry>> _byChatId = new();

    public OutboxStore(Func<string> getCacheRoot)
    {
        _getCacheRoot = getCacheRoot ?? throw new ArgumentNullException(nameof(getCacheRoot));
    }

    public IReadOnlyList<OutboxEntry> GetFor(string chatId)
    {
        return LoadOrCache(chatId);
    }

    public OutboxEntry Find(string tempId)
    {
        foreach (var list in _byChatId.Values)
            foreach (var entry in list)
                if (entry.tempId == tempId) return entry;
        return null;
    }

    public void Add(OutboxEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.chatId) || string.IsNullOrEmpty(entry.tempId))
            return;
        var list = LoadOrCache(entry.chatId);
        list.Add(entry);
        Persist(entry.chatId, list);
    }

    public void Remove(string tempId)
    {
        foreach (var kvp in _byChatId)
        {
            var list = kvp.Value;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].tempId == tempId)
                {
                    list.RemoveAt(i);
                    Persist(kvp.Key, list);
                    return;
                }
            }
        }
    }

    public void Update(OutboxEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.chatId) || string.IsNullOrEmpty(entry.tempId))
            return;
        var list = LoadOrCache(entry.chatId);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].tempId == entry.tempId)
            {
                list[i] = entry;
                Persist(entry.chatId, list);
                return;
            }
        }
    }

    private List<OutboxEntry> LoadOrCache(string chatId)
    {
        if (_byChatId.TryGetValue(chatId, out var cached)) return cached;

        var list = LoadFromDisk(chatId);
        _byChatId[chatId] = list;
        return list;
    }

    private List<OutboxEntry> LoadFromDisk(string chatId)
    {
        string path = FilePath(chatId);
        if (!File.Exists(path)) return new List<OutboxEntry>();

        try
        {
            string json = File.ReadAllText(path);
            var parsed = JsonUtility.FromJson<OutboxFile>(json);
            return parsed?.entries ?? new List<OutboxEntry>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[OutboxStore] Corrupted outbox at {path}: {ex.Message}. Treating as empty.");
            return new List<OutboxEntry>();
        }
    }

    private void Persist(string chatId, List<OutboxEntry> list)
    {
        string path = FilePath(chatId);
        string tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = JsonUtility.ToJson(new OutboxFile { entries = list }, prettyPrint: false);
            File.WriteAllText(tmp, json);

            if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
            else File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OutboxStore] Failed to persist outbox for {chatId}: {ex.Message}");
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private string FilePath(string chatId)
    {
        string root = _getCacheRoot?.Invoke();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("OutboxStore: getCacheRoot returned null or empty.");
        return Path.Combine(root, $"outbox_{SanitizeChatId(chatId)}.json");
    }

    private static string SanitizeChatId(string chatId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(chatId.Length);
        foreach (var c in chatId)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run the tests, verify all 9 pass**

Unity → Test Runner → EditMode → Run All.

Expected: 9 OutboxStore tests pass. Combined total now 23 tests passing.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/OutboxStore.cs \
        Assets/Scripts/Chat/OutboxStore.cs.meta \
        Assets/Tests/EditMode/Chat/OutboxStoreTests.cs \
        Assets/Tests/EditMode/Chat/OutboxStoreTests.cs.meta
git commit -m "feat(chat): OutboxStore — persisted per-chat retry queue

Plain C# class. Per-bot scoped via injected getCacheRoot Func, with
per-chat JSON files at {root}/outbox_{chatId}.json. Atomic writes via
.tmp + File.Replace; corrupted files degrade to empty without crashing.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: `OnMessageStatusChanged` event + `SetDeliveryStatus` on the view

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` (declare event)
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (subscribe, handle, extract `RefreshTimeAndTick`)

After this task, the event exists and the view re-renders its tick when the event fires — but nothing fires it yet. The tap-to-retry button is a no-op stub until Task 8.

- [ ] **Step 1: Declare the event on `ChatManager`**

File: `Assets/Scripts/Main/ChatManager.cs`

Find the existing event declarations (around line 25-35). Add:

```csharp
    public event Action<List<MessageViewModel>> OnLiveMessagesReceived;

    /// <summary>
    /// Fires when an outgoing message's delivery status changes.
    /// oldMessageId matches the bubble's current MessageViewModel.messageId.
    /// newMessageId is the post-change id — for the optimistic-send → server-ack
    /// transition it's the real Wappi id; for in-place status updates it's the
    /// same as oldMessageId.
    /// </summary>
    public event Action<string, string, DeliveryStatus> OnMessageStatusChanged;
```

(Adapt the surrounding code if the existing event isn't on the exact line shown — just add the new event in the same region.)

- [ ] **Step 2: Extract the time-render block into a method on `MessageItemView`**

File: `Assets/Scripts/UI/MessageItemView.cs`

In `Bind()` (around line 268 after Task 3), the timeText block currently reads:

```csharp
        if (timeText != null)
        {
            DateTime localTime = DateTimeOffset.FromUnixTimeSeconds(vm.timestamp).LocalDateTime;
            string formattedTime = localTime.ToString("HH:mm");
            string tickTag = vm.isIncoming ? null : DeliveryTickFormatter.GetSprite(vm.deliveryStatus);
            timeText.text = tickTag != null ? $"{formattedTime} {tickTag}" : formattedTime;
        }
```

Replace the body with a call to a new method:

```csharp
        RefreshTimeAndTick();
```

Then add the method as a new private member of `MessageItemView`. Place it near the end of the class, just before any closing brace:

```csharp
    private void RefreshTimeAndTick()
    {
        if (timeText == null || currentVm == null) return;
        DateTime localTime = DateTimeOffset.FromUnixTimeSeconds(currentVm.timestamp).LocalDateTime;
        string formattedTime = localTime.ToString("HH:mm");
        string tickTag = currentVm.isIncoming ? null : DeliveryTickFormatter.GetSprite(currentVm.deliveryStatus);
        timeText.text = tickTag != null ? $"{formattedTime} {tickTag}" : formattedTime;
    }
```

Note: `currentVm` is already a field on `MessageItemView` (set inside `Bind` at line 120).

- [ ] **Step 3: Add `SetDeliveryStatus`, `HandleStatusChanged`, and `UpdateRetryButton` stub**

File: `Assets/Scripts/UI/MessageItemView.cs`

Add to the same end-of-class region:

```csharp
    public void SetDeliveryStatus(DeliveryStatus newStatus)
    {
        if (currentVm == null || currentVm.isIncoming) return;
        currentVm.deliveryStatus = newStatus;
        RefreshTimeAndTick();
        UpdateRetryButton(newStatus == DeliveryStatus.Failed);
    }

    private void HandleStatusChanged(string oldMessageId, string newMessageId, DeliveryStatus status)
    {
        if (currentVm == null || currentVm.isIncoming) return;
        if (currentVm.messageId != oldMessageId) return;
        if (newMessageId != oldMessageId) currentVm.messageId = newMessageId;
        SetDeliveryStatus(status);
    }

    // Stub — full implementation in Task 8 (tap-to-retry).
    private void UpdateRetryButton(bool enableRetry)
    {
        // TODO Task 8: lazily AddComponent<Button> on timeText, wire onClick to
        // ChatManager.Instance.RetryOutboxMessage(currentVm.messageId).
    }
```

(The `TODO Task 8` comment is acceptable here because it points at the **very next task** in this plan. The full implementation lands in Task 8.)

- [ ] **Step 4: Subscribe and unsubscribe in `OnEnable` / `OnDisable`**

File: `Assets/Scripts/UI/MessageItemView.cs`

Locate the existing `OnEnable` method (around line 98). Currently:

```csharp
    void OnEnable()
    {
        if (AudioController.Instance != null)
        {
            AudioController.Instance.OnAudioStarted += HandleAudioStarted;
            AudioController.Instance.OnAudioStopped += HandleAudioStopped;
            AudioController.Instance.OnAudioProgress += HandleAudioProgress;
        }
    }
```

Add the new subscription at the end of the method:

```csharp
    void OnEnable()
    {
        if (AudioController.Instance != null)
        {
            AudioController.Instance.OnAudioStarted += HandleAudioStarted;
            AudioController.Instance.OnAudioStopped += HandleAudioStopped;
            AudioController.Instance.OnAudioProgress += HandleAudioProgress;
        }

        if (ChatManager.Instance != null)
            ChatManager.Instance.OnMessageStatusChanged += HandleStatusChanged;
    }
```

Similarly in `OnDisable` (around line 108):

```csharp
    void OnDisable()
    {
        if (AudioController.Instance != null)
        {
            AudioController.Instance.OnAudioStarted -= HandleAudioStarted;
            AudioController.Instance.OnAudioStopped -= HandleAudioStopped;
            AudioController.Instance.OnAudioProgress -= HandleAudioProgress;
        }

        if (ChatManager.Instance != null)
            ChatManager.Instance.OnMessageStatusChanged -= HandleStatusChanged;
    }
```

- [ ] **Step 5: Verify compilation in Unity**

Wait for the auto-recompile in the Editor. If compile errors appear, fix and rerun.

Expected: clean compile, no console errors. The Test Runner still shows 23 tests passing.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs Assets/Scripts/UI/MessageItemView.cs
git commit -m "feat(chat): OnMessageStatusChanged event + view re-render hook

Declares ChatManager.OnMessageStatusChanged; subscribes MessageItemView
to it and extracts the time/tick rendering into RefreshTimeAndTick so
status changes can re-render without rebuilding the bubble. UpdateRetryButton
is a stub — Task 8 wires the click handler.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Refactor `SendTextMessageRoutine` into a reusable POST coroutine

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

Pure refactor. After this task the send flow is byte-for-byte equivalent to before, but the POST body is callable from `RetryOutboxMessage` in Task 8.

- [ ] **Step 1: Read the existing routine to mark the extraction boundary**

The current `SendTextMessageRoutine` (line 675) does three things:

1. **Optimistic UI** (lines 692–716): tempId, optimistic VM, `OnLiveMessagesReceived`, cache write.
2. **POST** (lines 718–736): build URL, POST, check result.
3. **Success handling** (lines 738+): parse response, swap tempId/messageId.

Task 6 extracts steps 2 and 3 into `PostTextMessageRoutine(chatId, text, tempId, profileId, sendCacheRoot)`. Step 1 stays inside `SendTextMessageRoutine` so the existing behavior on the entry side is preserved.

- [ ] **Step 2: Add the new private coroutine**

File: `Assets/Scripts/Main/ChatManager.cs`

Find `SendTextMessageRoutine` (line 675). At the end of the method body (after the existing `try/catch` block that closes the routine), add a new method directly underneath. Use this template, copying the existing POST and response-handling code verbatim into it — adjusting field references to use the parameters rather than locals:

```csharp
    /// <summary>
    /// Network half of an outgoing text send. Shared between the initial
    /// optimistic send (SendTextMessageRoutine) and tap-to-retry
    /// (RetryOutboxMessage). Fires OnMessageStatusChanged on both success
    /// and failure paths; does NOT touch the outbox itself — callers own
    /// outbox lifecycle.
    /// </summary>
    private IEnumerator PostTextMessageRoutine(
        string chatId,
        string text,
        string tempId,
        string profileId,
        string sendCacheRoot)
    {
        string recipient = chatId.EndsWith("@c.us") ? chatId.Replace("@c.us", "") : chatId;
        string url = $"https://wappi.pro/api/sync/message/send?profile_id={profileId}";

        var requestData = new WappiSendTextRequest { body = text, recipient = recipient };
        string jsonPayload = JsonConvert.SerializeObject(requestData);

        using UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Wappi] message/send failed: {www.error}\n{www.downloadHandler?.text}");
            OnMessageStatusChanged?.Invoke(tempId, tempId, DeliveryStatus.Failed);
            yield break;
        }

        try
        {
            var response = JsonConvert.DeserializeObject<WappiSendTextResponse>(www.downloadHandler.text);
            if (response != null && response.status == "done" && !string.IsNullOrEmpty(response.message_id))
            {
                seenMessageIds.Remove(tempId);
                seenMessageIds.Add(response.message_id);

                // Update cached optimistic message so a chat reopen picks up the
                // real id and Sent status instead of a stranded tempId / Pending.
                List<MessageViewModel> cachedList = ChatHistoryCache.LoadHistory(sendCacheRoot, chatId);
                for (int i = 0; i < cachedList.Count; i++)
                {
                    if (cachedList[i].messageId == tempId)
                    {
                        cachedList[i].messageId = response.message_id;
                        cachedList[i].deliveryStatus = DeliveryStatus.Sent;
                        break;
                    }
                }
                ChatHistoryCache.SaveHistory(sendCacheRoot, chatId, cachedList);

                OnMessageStatusChanged?.Invoke(tempId, response.message_id, DeliveryStatus.Sent);
            }
            else
            {
                Debug.LogWarning($"[Wappi] message/send returned non-done status: {www.downloadHandler.text}");
                OnMessageStatusChanged?.Invoke(tempId, tempId, DeliveryStatus.Failed);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Wappi] message/send response parse failed: {ex.Message}\n{www.downloadHandler.text}");
            OnMessageStatusChanged?.Invoke(tempId, tempId, DeliveryStatus.Failed);
        }
    }
```

- [ ] **Step 3: Replace the inline POST + response block in `SendTextMessageRoutine`**

File: `Assets/Scripts/Main/ChatManager.cs`

Locate the POST block inside `SendTextMessageRoutine` (lines 718–end of method). Replace everything from `// --- BACKGROUND: Send to server silently ---` (line 718) to the closing `}` of the method body with:

```csharp
    // --- BACKGROUND: Send to server silently ---
    yield return PostTextMessageRoutine(chatId, text, tempId, activeProfileId, sendCacheRoot);
}
```

- [ ] **Step 4: Verify compilation and run all tests**

Unity → wait for compile → Test Runner → EditMode → Run All.

Expected: clean compile, 23 EditMode tests still pass.

- [ ] **Step 5: Manual smoke test — send a message**

1. Enter Play mode.
2. Open any chat.
3. Type "hello world", tap send.
4. Verify the bubble appears immediately (optimistic path unchanged).
5. Verify the message actually arrives on the recipient's device (POST path unchanged).
6. Watch the bubble — after the success path of `PostTextMessageRoutine` runs, the tick should flip to **single grey tick** (Sent) due to the now-firing `OnMessageStatusChanged` event.

Expected: send works identically to before, with the new bonus that the optimistic bubble flips from no-tick to single tick when the server acknowledges. (If the bubble still shows no tick, it means the optimistic VM started at `DeliveryStatus.None`. That's expected — Task 7 will set it to `Pending` at creation, then the response flips to `Sent`.)

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "refactor(chat): extract PostTextMessageRoutine from send flow

Splits the network half of SendTextMessageRoutine into a reusable
private coroutine so tap-to-retry can call it directly. Behavior is
unchanged for the existing send path, plus the success/failure paths
now fire OnMessageStatusChanged so already-rendered bubbles flip their
tick in place. Failure handling stops silently dropping the message —
it now signals Failed instead.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Wire outbox + Pending state into `SendTextMessageRoutine`

**Files:**
- Create: `Assets/Scripts/Main/ChatManager.Outbox.cs`
- Modify: `Assets/Scripts/Main/ChatManager.cs` (call sites only)

After this task, optimistic bubbles render with the clock from the start, and failed sends persist in the outbox JSON file.

- [ ] **Step 1: Create the `ChatManager.Outbox` partial class**

Path: `Assets/Scripts/Main/ChatManager.Outbox.cs`

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Outbox concerns split out of ChatManager — keeps the god-object trimmer
/// and groups related behavior. Mirrors the existing ChatManager.BotState.cs
/// partial split.
/// </summary>
public partial class ChatManager
{
    private OutboxStore _outbox;
    private readonly HashSet<string> _retriesInFlight = new();

    private OutboxStore Outbox => _outbox ??= new OutboxStore(GetCacheRoot);

    /// <summary>
    /// Re-fires the network half of a previously-failed send. No-op if the
    /// entry was never queued or a retry for the same tempId is already in
    /// flight — guards against rapid double-taps spawning duplicate POSTs.
    /// </summary>
    public void RetryOutboxMessage(string tempId)
    {
        if (string.IsNullOrEmpty(tempId)) return;
        if (!_retriesInFlight.Add(tempId)) return; // already retrying this id

        OutboxStore.OutboxEntry entry = Outbox.Find(tempId);
        if (entry == null)
        {
            _retriesInFlight.Remove(tempId);
            return;
        }

        entry.attemptCount++;
        Outbox.Update(entry);

        OnMessageStatusChanged?.Invoke(tempId, tempId, DeliveryStatus.Pending);

        MonoBehaviour runner = Manager.Instance != null ? (MonoBehaviour)Manager.Instance : this;
        runner.StartCoroutine(RetryRoutine(tempId, entry));
    }

    private IEnumerator RetryRoutine(string tempId, OutboxStore.OutboxEntry entry)
    {
        try
        {
            yield return PostTextMessageRoutine(entry.chatId, entry.text, tempId, entry.profileId, GetCacheRoot());
        }
        finally
        {
            _retriesInFlight.Remove(tempId);
        }
    }
}
```

**Why the `try/finally` around `yield return`:** C# disallows yielding inside a `try/catch` block that has a `catch`, but **does** allow yielding inside a `try/finally`. Either the success path or the failure path of `PostTextMessageRoutine` will run, both will fire `OnMessageStatusChanged` themselves, and either way control returns to the `finally` to clear the in-flight tracking set.

- [ ] **Step 2: Hook the outbox `Add` into `SendTextMessageRoutine`**

File: `Assets/Scripts/Main/ChatManager.cs`

Locate the optimistic-UI block (lines 692–716). Find:

```csharp
    seenMessageIds.Add(tempId);

    var instantMessage = new MessageViewModel
    {
        messageId = tempId,
        chatId = chatId,
        senderName = "Me",
        type = MessageType.Chat,
        text = text,
        isIncoming = false,
        timestamp = now
    };
```

Add `deliveryStatus = DeliveryStatus.Pending` to the initializer and an outbox `Add` call afterward:

```csharp
    seenMessageIds.Add(tempId);

    var instantMessage = new MessageViewModel
    {
        messageId = tempId,
        chatId = chatId,
        senderName = "Me",
        type = MessageType.Chat,
        text = text,
        isIncoming = false,
        timestamp = now,
        deliveryStatus = DeliveryStatus.Pending
    };

    Outbox.Add(new OutboxStore.OutboxEntry
    {
        tempId       = tempId,
        chatId       = chatId,
        text         = text,
        timestamp    = now,
        attemptCount = 1,
        profileId    = activeProfileId
    });
```

- [ ] **Step 3: Hook the outbox `Remove` into `PostTextMessageRoutine` success path**

File: `Assets/Scripts/Main/ChatManager.cs`

Inside the success branch of `PostTextMessageRoutine` (the block created in Task 6), find the line:

```csharp
                OnMessageStatusChanged?.Invoke(tempId, response.message_id, DeliveryStatus.Sent);
```

Add the outbox removal immediately before it:

```csharp
                Outbox.Remove(tempId);
                OnMessageStatusChanged?.Invoke(tempId, response.message_id, DeliveryStatus.Sent);
```

Failure paths leave the entry in place — no code change needed there.

- [ ] **Step 4: Manual smoke test — happy path**

1. Enter Play mode with a working internet connection.
2. Send a text message.
3. Verify the bubble shows **clock** initially, then within ~1 second flips to **single grey tick**.
4. Open Finder and navigate to the persistent data path (Application.persistentDataPath). On macOS it's typically `~/Library/Application Support/<companyName>/<productName>/BotCache/<botId>/`. Verify there's no `outbox_*.json` for this chat after the success.

Expected: clock → single tick transition is visible; outbox file does not accumulate successful sends.

- [ ] **Step 5: Manual smoke test — failure path**

1. Enable airplane mode on the host machine (or stop the device's network).
2. Send another text message.
3. Verify the bubble shows **clock** for ~30 seconds (UnityWebRequest timeout), then flips to **red `!`**.
4. Verify `outbox_<chatId>.json` exists in the bot's cache root and contains the entry.

Expected: clock → red `!`; outbox file persists the entry.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.Outbox.cs \
        Assets/Scripts/Main/ChatManager.Outbox.cs.meta \
        Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): wire outbox + Pending state into send flow

Optimistic sends now start at DeliveryStatus.Pending and are added to
OutboxStore. On Wappi success, the entry is removed and the bubble
flips to Sent. On failure, the entry persists and the bubble flips to
Failed. RetryOutboxMessage re-fires PostTextMessageRoutine using the
captured profileId so bot-switching during a pending send can't
cross-wire.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Tap-to-retry button on failed bubbles

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (fill in `UpdateRetryButton` stub from Task 5)

After this task, tapping a red-! bubble re-fires the send and the bubble cycles back to clock → single tick (or red ! on repeat failure).

- [ ] **Step 1: Add a private retry-button field**

File: `Assets/Scripts/UI/MessageItemView.cs`

In the private-fields region near `currentVm` (around line 70), add:

```csharp
    private Button retryButton;
```

(Note: `UnityEngine.UI` is already imported at the top of the file.)

- [ ] **Step 2: Replace the `UpdateRetryButton` stub with the working implementation**

File: `Assets/Scripts/UI/MessageItemView.cs`

Locate the stub added in Task 5:

```csharp
    // Stub — full implementation in Task 8 (tap-to-retry).
    private void UpdateRetryButton(bool enableRetry)
    {
        // TODO Task 8: lazily AddComponent<Button> on timeText, wire onClick to
        // ChatManager.Instance.RetryOutboxMessage(currentVm.messageId).
    }
```

Replace with:

```csharp
    private void UpdateRetryButton(bool enableRetry)
    {
        if (timeText == null) return;

        if (enableRetry)
        {
            // Lazily create the Button on first failure for this bubble.
            if (retryButton == null)
            {
                timeText.raycastTarget = true;
                retryButton = timeText.GetComponent<Button>();
                if (retryButton == null) retryButton = timeText.gameObject.AddComponent<Button>();
                retryButton.transition = Selectable.Transition.None;
            }

            retryButton.onClick.RemoveAllListeners();
            string capturedMessageId = currentVm.messageId;
            retryButton.onClick.AddListener(() =>
            {
                if (ScrollClickBlocker.IsBlocking) return;
                if (ChatManager.Instance != null) ChatManager.Instance.RetryOutboxMessage(capturedMessageId);
            });
            retryButton.interactable = true;
        }
        else if (retryButton != null)
        {
            retryButton.onClick.RemoveAllListeners();
            retryButton.interactable = false;
        }
    }
```

The `ScrollClickBlocker.IsBlocking` guard mirrors the pattern at [MessageItemView.cs:182](../../../Assets/Scripts/UI/MessageItemView.cs#L182) — prevents accidental taps while the user is scrolling.

- [ ] **Step 3: Clean up the button listener on disable**

File: `Assets/Scripts/UI/MessageItemView.cs`

Inside `OnDisable` (the version edited in Task 5), add after the existing unsubscribes:

```csharp
        if (retryButton != null)
            retryButton.onClick.RemoveAllListeners();
```

- [ ] **Step 4: Manual smoke test — full retry cycle**

1. Enter Play mode.
2. Enable airplane mode and send a message → wait for red `!`.
3. Disable airplane mode.
4. Tap on the time/tick area of the failed bubble.
5. Verify the bubble flips back to **clock**, then to **single grey tick** within ~1 second.
6. Verify the recipient actually receives the message.
7. Verify `outbox_<chatId>.json` no longer contains the entry.

Expected: tap-to-retry succeeds and clears the outbox.

- [ ] **Step 5: Manual smoke test — retry-while-still-offline**

1. Enable airplane mode.
2. Send a message → wait for red `!`.
3. While still in airplane mode, tap the red `!`.
4. Verify the bubble flips to clock for ~30 seconds, then back to red `!`.
5. Verify the outbox entry's `attemptCount` has incremented to 3 (initial send + 2 retries — read the JSON file).

Expected: retries cycle correctly and `attemptCount` grows on each failure.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "feat(chat): tap-to-retry on failed outgoing bubbles

UpdateRetryButton lazily AddComponent<Button> on the timeText TMP rect
when a bubble enters DeliveryStatus.Failed. Click fires
ChatManager.RetryOutboxMessage with the captured messageId. Listener
clears when the bubble leaves the Failed state or is disabled.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Chat-open promotion of stale `Pending` to `Failed`

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` (`OnChatSelected` or cache-load path)

After this task, an optimistic send interrupted by an app crash or quit will render as **Failed** (red `!`) on next chat open instead of as a stranded clock.

The mechanism: any cached message whose `messageId` still matches an entry in the outbox is, by definition, still unresolved. Promote its `deliveryStatus` to `Failed` before it reaches the UI.

**Model note — promotion vs. splice.** The spec §5.6 describes this step as "splicing" outbox entries into the message list. The plan implements it as in-place promotion of *already-cached* messages because the existing send code at [`ChatManager.cs:716`](../../../Assets/Scripts/Main/ChatManager.cs#L716) already persists optimistic messages to `ChatHistoryCache` via `SaveHistory`. So a failed message survives a chat reopen as a normal cache entry; the outbox tells us only *which* of those entries should render as Failed instead of Pending. This is functionally identical to the spec's splice semantics — the bubble appears at its original timestamp position rendering as Failed — but avoids materializing a second `MessageViewModel` for an entry that already has one in the cache.

- [ ] **Step 1: Find the cache-load site**

File: `Assets/Scripts/Main/ChatManager.cs`

Locate the place inside `OnChatSelected` (or its called helper) where the cached messages are loaded but **before** they're handed to `OnBatchMessagesLoaded`. Search for `ChatHistoryCache.LoadHistory(`:

```bash
grep -n "ChatHistoryCache.LoadHistory" /Users/ayan/Projects/Automation/Assets/Scripts/Main/ChatManager.cs
```

You'll find ~2 call sites. The relevant one is inside the chat-open flow (not the send flow). It currently produces a `List<MessageViewModel> cachedMessages` that's about to be fired through `OnBatchMessagesLoaded`.

- [ ] **Step 2: Add the promotion pass**

Immediately after `cachedMessages` is materialized and before it's handed to subscribers, insert:

```csharp
        // Promote stale-Pending cached messages to Failed for any tempId still
        // sitting in the outbox. An unresolved entry means the in-flight POST
        // from a previous session never completed — without this pass the user
        // would see a phantom clock that never resolves.
        var unresolved = Outbox.GetFor(chatId);
        if (unresolved.Count > 0)
        {
            var unresolvedIds = new HashSet<string>();
            foreach (var entry in unresolved) unresolvedIds.Add(entry.tempId);

            foreach (var msg in cachedMessages)
            {
                if (!msg.isIncoming && unresolvedIds.Contains(msg.messageId))
                    msg.deliveryStatus = DeliveryStatus.Failed;
            }
        }
```

(Adjust the variable name `cachedMessages` if the local variable in the actual code uses a different identifier — the comment is what matters.)

- [ ] **Step 3: Manual smoke test — app-kill mid-send**

This is the spec's PlayMode check #3:

1. Enter Play mode with internet connectivity.
2. Open a chat.
3. **Disable the network**, then immediately send a message.
4. Without waiting for the 30 s timeout to flip the bubble to red `!`, **stop Play mode**.
5. Re-enter Play mode and reopen the same chat.
6. Verify the message renders with the **red `!` tick** (not a stranded clock).
7. Tap the red `!`, re-enable network, verify the retry succeeds.

Expected: stale Pending entries reliably promote to Failed on chat reopen.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): promote stale Pending to Failed on chat open

Walks the cache-loaded message list at chat-open time and flips any
non-incoming message whose tempId still lives in the outbox to
DeliveryStatus.Failed. Resolves the 'phantom clock' case after a
mid-send app kill, and feeds tap-to-retry the right starting state.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Final verification

Run the full PlayMode checklist from spec §8.2. Each check should already have been covered by an earlier task's manual smoke test, but it's worth a single end-to-end pass to catch interactions.

- [ ] **Check 1:** Normal text send → clock → single tick → double tick → blue (when recipient reads). (Tasks 6+7)
- [ ] **Check 2:** Airplane-mode mid-send → red `!`; toggle off + tap `!` → clock → single tick. Outbox empty after success. (Task 8)
- [ ] **Check 3:** Force-quit mid-send → reopen chat → Failed renders; tap retries. (Task 9)
- [ ] **Check 4:** Switch bots, switch back, tap retry → sends from the original bot. (Task 7 — `entry.profileId`)
- [ ] **Check 5:** Group chat with mixed states → all senders' colors + all tick states correct.
- [ ] **Check 6:** Long chat, scroll to old outgoing messages → ticks render correctly in history. (Task 3)
- [ ] **Check 7:** Chat with zero outgoing messages → no tick rendered anywhere. (Task 3 — incoming guard)

- [ ] **Final commit (only if any tweaks needed during verification):**

```bash
# Only if you fixed anything during the manual checklist.
git add -p
git commit -m "fix(chat): <describe specific tweak>

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```
