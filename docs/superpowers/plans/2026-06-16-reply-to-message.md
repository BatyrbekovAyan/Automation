# Reply to message (WhatsApp-style) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add WhatsApp-style reply-to-message to the WhatsApp/Wappi chat — quoted card in the bubble, a compose preview bar, swipe-right + long-press triggers, and tap-to-scroll-to-original.

**Architecture:** Replies travel the existing four-layer message pipeline (`RawMessage` → `Normalize` → `CreateViewModel` → `MessageItemView`). A new pure `ReplyParser` resolves the quoted preview once, in `Normalize` (cache-by-id → embedded `reply_message` snapshot → placeholder), and stores it as flat primitive fields on the VM (a `JToken` on `MessageViewModel` would corrupt the JsonUtility cache). Compose/reply state lives on `ChatManager`; the bubble card is pure-render. The reply trigger is disabled on not-yet-Sent bubbles (decision D1), so the wire `quoted_message_id` is always a real server id.

**Tech Stack:** Unity 6 (6000.3.9f1), C#, Newtonsoft.Json (messages), Unity JsonUtility (cache), DOTween, TMPro, NUnit EditMode tests.

**Reference spec:** [docs/superpowers/specs/2026-06-16-reply-to-message-design.md](../specs/2026-06-16-reply-to-message-design.md)

---

## Conventions for every task

- **Run EditMode tests** (Editor must be closed): `Tools/run-tests-headless.sh "<regex>"` — results land in `Tools/test-output/`. If the Unity Editor is open instead, use the `Temp/claude/run-tests.trigger` bridge.
- **New `.cs` files** get a `.meta` from Unity on import; the first headless test run imports them. **Stage `.cs` + `.meta` together** on commit.
- **Commit message trailer:** end every commit body with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- The `validate-cs.sh` PostToolUse hook checks C# quality after each edit — heed its output.

---

## Phase A — Verify the Wappi wire shapes (prerequisite)

### Task 1: Capture a real reply payload and pin field names

**Files:** none (runtime/manual verification).

- [ ] **Step 1: Capture a live reply payload**

In a test WhatsApp chat, send yourself (or have someone send) a message that **replies to** an earlier text message and an earlier **image**. Open the project in the Unity Editor and open that chat so `GetMessagesRoutine` runs. The `#if UNITY_EDITOR` block in `Assets/Scripts/Main/ChatManager.cs` (~lines 1004–1009) writes the raw `messages/get` response to `Application.persistentDataPath/response.txt`.

- [ ] **Step 2: Read the dump and record the exact keys**

Open `response.txt` and confirm/record:
- the reply flag key (expected `isReply`),
- the quoted object key (expected `reply_message`),
- its sub-fields (expected `id`, `body`, `type`, `caption`, `file_name`, optional `JPEGThumbnail`, `senderName`, `fromMe`),
- whether `reply_message.id` is the **same id space** as `RawMessage.id` (so exact-match cache lookup works) or a `stanzaId`-style id.

- [ ] **Step 3: Confirm the send-side param name**

From the Wappi Postman collection / dashboard / support, confirm the `/message/send` field that quotes a message (expected `quoted_message_id`).

- [ ] **Step 4: Record findings inline in the spec**

If any name differs from the assumptions, note the real names at the top of the spec file under a new "Phase 0 — confirmed wire shapes" heading. `ReplyParser` (Task 3) is written tolerant of missing keys, so a mismatch degrades to placeholder rather than crashing — but pin the names here before relying on media thumbnails in quotes.

- [ ] **Step 5: Commit the spec note**

```bash
git add docs/superpowers/specs/2026-06-16-reply-to-message-design.md
git commit -m "docs(chat): pin confirmed Wappi reply wire field names

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Phase B — Data model + ReplyParser

### Task 2: Add reply fields to `RawMessage`

**Files:**
- Modify: `Assets/Scripts/Chat/RawMessage.cs`

- [ ] **Step 1: Add the two fields**

In `RawMessage`, after `from` (line ~16), add:

```csharp
    [JsonProperty("isReply")]
    public bool isReply;        // True when this message replies to another.

    [JsonProperty("reply_message")]
    public JToken replyMessage; // Snapshot of the quoted message (id/type/body/caption/...).
```

(`JToken` is already imported via `Newtonsoft.Json.Linq` at the top and is safe here — `RawMessage` is only ever deserialized via `JsonConvert`.)

- [ ] **Step 2: Build-check via the existing suite**

Run: `Tools/run-tests-headless.sh "ReactionParser"`
Expected: existing tests still PASS (confirms the project compiles with the new fields).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/RawMessage.cs
git commit -m "feat(chat): add isReply/reply_message to RawMessage

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 3: Create `QuotedPreview` + `ReplyParser` (TDD)

**Files:**
- Create: `Assets/Scripts/Chat/QuotedPreview.cs`
- Create: `Assets/Scripts/Chat/ReplyParser.cs`
- Test: `Assets/Tests/Editor/Chat/ReplyParserTests.cs`

- [ ] **Step 1: Write the value object**

`Assets/Scripts/Chat/QuotedPreview.cs`:

```csharp
/// <summary>
/// In-memory resolved preview of a quoted (replied-to) message. Flattened onto
/// NormalizedMessage/MessageViewModel by ChatManager. Never holds a JToken.
/// </summary>
public struct QuotedPreview
{
    public string      messageId;
    public string      senderName;   // "You" for own messages, else the real sender.
    public string      text;         // Snippet or a type label ("Photo", "Voice message", ...).
    public MessageType type;
    public string      thumbnailUrl; // Null for text / when no cached thumb is available.

    public bool IsEmpty => string.IsNullOrEmpty(messageId);
    public static QuotedPreview None => new QuotedPreview();
}
```

- [ ] **Step 2: Write the failing tests**

`Assets/Tests/Editor/Chat/ReplyParserTests.cs`:

```csharp
using NUnit.Framework;
using Newtonsoft.Json.Linq;

public class ReplyParserTests
{
    private static MessageType StubParse(string t)
    {
        switch (t)
        {
            case "image": return MessageType.Image;
            case "video": return MessageType.Video;
            case "ptt":   return MessageType.Voice;
            case "audio": return MessageType.Audio;
            case "document": return MessageType.Document;
            case "chat":  return MessageType.Chat;
            default:       return MessageType.Unknown;
        }
    }

    private static RawMessage Reply(string quotedId, string type = "chat", string body = "orig", string caption = null)
    {
        var snap = new JObject { ["id"] = quotedId, ["type"] = type, ["body"] = body };
        if (caption != null) snap["caption"] = caption;
        return new RawMessage { type = "chat", isReply = true, replyMessage = snap };
    }

    [Test]
    public void NullRaw_ReturnsNone()
    {
        Assert.IsTrue(ReplyParser.Resolve(null, _ => null, StubParse).IsEmpty);
    }

    [Test]
    public void ReactionRaw_ReturnsNone()
    {
        var raw = Reply("Q1");
        raw.type = "reaction";
        Assert.IsTrue(ReplyParser.Resolve(raw, _ => null, StubParse).IsEmpty);
    }

    [Test]
    public void NoReplyIndicator_ReturnsNone()
    {
        var raw = new RawMessage { type = "chat", isReply = false };
        Assert.IsTrue(ReplyParser.Resolve(raw, _ => null, StubParse).IsEmpty);
    }

    [Test]
    public void CacheHit_UsesCachedContent()
    {
        var cached = new MessageViewModel { messageId = "Q1", senderName = "Aisha", text = "cached body", type = MessageType.Chat, isIncoming = true };
        var preview = ReplyParser.Resolve(Reply("Q1", body: "stale snapshot"), id => id == "Q1" ? cached : null, StubParse);
        Assert.AreEqual("Q1", preview.messageId);
        Assert.AreEqual("Aisha", preview.senderName);
        Assert.AreEqual("cached body", preview.text);
    }

    [Test]
    public void CacheMiss_UsesSnapshot()
    {
        var preview = ReplyParser.Resolve(Reply("Q2", type: "image", body: "", caption: "Beach"), _ => null, StubParse);
        Assert.AreEqual("Q2", preview.messageId);
        Assert.AreEqual(MessageType.Image, preview.type);
        Assert.AreEqual("Beach", preview.text);
    }

    [Test]
    public void CacheMiss_NoSnapshotContent_ReturnsPlaceholderWithId()
    {
        var raw = new RawMessage { type = "chat", isReply = true }; // no replyMessage object
        raw.stanzaId = "Q3";
        var preview = ReplyParser.Resolve(raw, _ => null, StubParse);
        Assert.AreEqual("Q3", preview.messageId);
        Assert.AreEqual(MessageType.Unknown, preview.type);
    }

    [Test]
    public void NullResolver_DoesNotThrow_FallsToSnapshot()
    {
        var preview = ReplyParser.Resolve(Reply("Q4", body: "snap"), null, StubParse);
        Assert.AreEqual("snap", preview.text);
    }

    [Test]
    public void OwnMessage_SenderLabelIsYou()
    {
        var cached = new MessageViewModel { messageId = "Q5", senderName = "Me", text = "hi", type = MessageType.Chat, isIncoming = false };
        var preview = ReplyParser.Resolve(Reply("Q5"), id => cached, StubParse);
        Assert.AreEqual("You", preview.senderName);
    }

    [Test]
    public void MediaNoCaption_SnippetIsTypeLabel()
    {
        Assert.AreEqual("Photo", ReplyParser.SnippetFor(MessageType.Image, null));
        Assert.AreEqual("Voice message", ReplyParser.SnippetFor(MessageType.Voice, ""));
        Assert.AreEqual("hello", ReplyParser.SnippetFor(MessageType.Image, "hello"));
    }

    [Test]
    public void CleanSnippet_TrimsLeadingZeroWidthSpace()
    {
        Assert.AreEqual("hi", ReplyParser.CleanSnippet("​  hi"));
    }
}
```

- [ ] **Step 3: Run the tests to verify they FAIL**

Run: `Tools/run-tests-headless.sh "ReplyParser"`
Expected: FAIL/compile error — `ReplyParser` does not exist yet.

- [ ] **Step 4: Implement `ReplyParser`**

`Assets/Scripts/Chat/ReplyParser.cs`:

```csharp
using System;
using Newtonsoft.Json.Linq;

/// <summary>
/// Resolves the quoted-message preview for a reply bubble. Pure/static so it is
/// unit-testable and callable from ChatManager.Normalize without a MonoBehaviour.
/// Resolution order: cache-by-id (freshest, tappable) -> API-embedded reply_message
/// snapshot -> id-only placeholder. Never throws.
/// </summary>
public static class ReplyParser
{
    public static QuotedPreview Resolve(
        RawMessage raw,
        Func<string, MessageViewModel> resolveById,
        Func<string, MessageType> parseType)
    {
        if (raw == null || raw.type == "reaction") return QuotedPreview.None;

        bool isReply = raw.isReply || raw.replyMessage is JObject;
        if (!isReply) return QuotedPreview.None;

        string quotedId = ExtractQuotedId(raw);
        if (string.IsNullOrEmpty(quotedId)) return QuotedPreview.None;

        MessageViewModel cached = resolveById?.Invoke(quotedId);
        if (cached != null) return FromCached(quotedId, cached);

        if (raw.replyMessage is JObject snap) return FromSnapshot(quotedId, snap, parseType);

        return new QuotedPreview { messageId = quotedId, type = MessageType.Unknown };
    }

    private static string ExtractQuotedId(RawMessage raw)
    {
        if (raw.replyMessage is JObject obj)
        {
            string id = obj["id"]?.ToString();
            if (!string.IsNullOrEmpty(id)) return id;
        }
        return raw.stanzaId;
    }

    private static QuotedPreview FromCached(string id, MessageViewModel vm) => new QuotedPreview
    {
        messageId    = id,
        senderName   = SenderLabel(vm.isIncoming, vm.senderName),
        text         = SnippetFor(vm.type, vm.text),
        type         = vm.type,
        thumbnailUrl = string.IsNullOrEmpty(vm.thumbnailUrl) ? vm.mediaUrl : vm.thumbnailUrl
    };

    private static QuotedPreview FromSnapshot(string id, JObject snap, Func<string, MessageType> parseType)
    {
        string typeStr = snap["type"]?.ToString() ?? "chat";
        MessageType t  = parseType != null ? parseType(typeStr) : MessageType.Unknown;
        string caption = snap["caption"]?.ToString();
        string body    = snap["body"]?.ToString();
        bool fromMe    = snap["fromMe"]?.ToObject<bool>() ?? false;
        string sender  = snap["senderName"]?.ToString();
        return new QuotedPreview
        {
            messageId    = id,
            senderName   = SenderLabel(!fromMe, sender),
            text         = SnippetFor(t, string.IsNullOrEmpty(caption) ? body : caption),
            type         = t,
            thumbnailUrl = null   // snapshot rarely carries a usable thumb -> icon+label
        };
    }

    /// "You" for own messages, else the real sender name (never null).
    public static string SenderLabel(bool isIncoming, string senderName)
        => isIncoming ? (senderName ?? string.Empty) : "You";

    /// Caption/body when present, else a human label for the media type.
    public static string SnippetFor(MessageType type, string text)
    {
        if (!string.IsNullOrEmpty(text)) return text;
        switch (type)
        {
            case MessageType.Image:    return "Photo";
            case MessageType.Video:    return "Video";
            case MessageType.Voice:    return "Voice message";
            case MessageType.Audio:    return "Audio";
            case MessageType.Sticker:  return "Sticker";
            case MessageType.Document: return "Document";
            default:                   return string.Empty;
        }
    }

    /// Strips the leading zero-width space + whitespace UnicodeEmojiConverter prepends,
    /// so an emoji-only snippet doesn't render as visually empty.
    public static string CleanSnippet(string s)
        => string.IsNullOrEmpty(s) ? s : s.TrimStart('​', ' ', '\t', '\n', '\r');
}
```

- [ ] **Step 5: Run the tests to verify they PASS**

Run: `Tools/run-tests-headless.sh "ReplyParser"`
Expected: PASS (all ReplyParserTests green).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/QuotedPreview.cs Assets/Scripts/Chat/QuotedPreview.cs.meta \
        Assets/Scripts/Chat/ReplyParser.cs Assets/Scripts/Chat/ReplyParser.cs.meta \
        Assets/Tests/Editor/Chat/ReplyParserTests.cs Assets/Tests/Editor/Chat/ReplyParserTests.cs.meta
git commit -m "feat(chat): add ReplyParser + QuotedPreview with full resolution tests

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 4: Add quoted fields to `NormalizedMessage` and `MessageViewModel`

**Files:**
- Modify: `Assets/Scripts/Chat/NormalizedMessage.cs`
- Modify: `Assets/Scripts/UI/MessageViewModel.cs`
- Test: `Assets/Tests/Editor/Chat/QuotedFieldsCacheTests.cs`

- [ ] **Step 1: Write the failing JsonUtility round-trip test**

`Assets/Tests/Editor/Chat/QuotedFieldsCacheTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class QuotedFieldsCacheTests
{
    [Test]
    public void MessageViewModel_QuotedFields_SurviveJsonUtility()
    {
        var vm = new MessageViewModel
        {
            messageId = "m1",
            quotedMessageId = "q1",
            quotedSenderName = "You",
            quotedText = "hello",
            quotedType = MessageType.Image,
            quotedThumbnailUrl = "thumb://q1"
        };

        var back = JsonUtility.FromJson<MessageViewModel>(JsonUtility.ToJson(vm));

        Assert.AreEqual("q1", back.quotedMessageId);
        Assert.AreEqual("You", back.quotedSenderName);
        Assert.AreEqual("hello", back.quotedText);
        Assert.AreEqual(MessageType.Image, back.quotedType);
        Assert.AreEqual("thumb://q1", back.quotedThumbnailUrl);
    }
}
```

- [ ] **Step 2: Run to verify it FAILS**

Run: `Tools/run-tests-headless.sh "QuotedFieldsCache"`
Expected: FAIL — `quotedMessageId` etc. do not exist on `MessageViewModel`.

- [ ] **Step 3: Add fields to `NormalizedMessage`**

In `Assets/Scripts/Chat/NormalizedMessage.cs`, before `public DeliveryStatus deliveryStatus;`:

```csharp
    // Reply quote (resolved in ChatManager.Normalize via ReplyParser).
    public string      quotedMessageId;
    public string      quotedSenderName;
    public string      quotedText;
    public MessageType quotedType;
    public string      quotedThumbnailUrl;
```

- [ ] **Step 4: Add fields to `MessageViewModel`**

In `Assets/Scripts/UI/MessageViewModel.cs`, after the `reactions` field:

```csharp
    // Reply quote — flat primitives only (JsonUtility-persisted via ChatHistoryCache).
    public string      quotedMessageId;
    public string      quotedSenderName;
    public string      quotedText;
    public MessageType quotedType;
    public string      quotedThumbnailUrl;
```

- [ ] **Step 5: Run to verify it PASSES**

Run: `Tools/run-tests-headless.sh "QuotedFieldsCache"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/NormalizedMessage.cs Assets/Scripts/UI/MessageViewModel.cs \
        Assets/Tests/Editor/Chat/QuotedFieldsCacheTests.cs Assets/Tests/Editor/Chat/QuotedFieldsCacheTests.cs.meta
git commit -m "feat(chat): carry quoted* fields on NormalizedMessage + MessageViewModel

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 5: Resolve quotes in `Normalize` + copy in `CreateViewModel` + `FindActiveById`

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

- [ ] **Step 1: Add the cache-by-id helper**

Near the other private helpers in `ChatManager` (e.g. just below the `_activeChatCache` field region), add:

```csharp
    /// Linear scan of the loaded chat for a message by id. Null-safe during cold load
    /// (when _activeChatCache is not yet assigned). Used by ReplyParser to resolve quotes.
    private MessageViewModel FindActiveById(string id)
    {
        if (string.IsNullOrEmpty(id) || _activeChatCache == null) return null;
        for (int i = 0; i < _activeChatCache.Count; i++)
            if (_activeChatCache[i] != null && _activeChatCache[i].messageId == id)
                return _activeChatCache[i];
        return null;
    }
```

- [ ] **Step 2: Populate quoted fields at the end of `Normalize`**

In `Normalize(RawMessage raw)`, immediately before `return msg;` (~line 1292):

```csharp
        if (raw.type != "reaction")
        {
            QuotedPreview quote = ReplyParser.Resolve(raw, FindActiveById, ParseMessageType);
            if (!quote.IsEmpty)
            {
                msg.quotedMessageId    = quote.messageId;
                msg.quotedSenderName   = quote.senderName;
                msg.quotedText         = quote.text;
                msg.quotedType         = quote.type;
                msg.quotedThumbnailUrl = quote.thumbnailUrl;
            }
        }
```

- [ ] **Step 3: Copy quoted fields in `CreateViewModel`**

In the `MessageViewModel` initializer inside `CreateViewModel(NormalizedMessage msg)` (~lines 1100–1121), add:

```csharp
            quotedMessageId    = msg.quotedMessageId,
            quotedSenderName   = msg.quotedSenderName,
            quotedText         = msg.quotedText,
            quotedType         = msg.quotedType,
            quotedThumbnailUrl = msg.quotedThumbnailUrl,
```

- [ ] **Step 4: Confirm `ParseMessageType` is reachable as a `Func<string,MessageType>`**

`ParseMessageType` is an instance method on `ChatManager`; passing it as `ParseMessageType` (method group) to `ReplyParser.Resolve` works. If it is `static`, that is also fine. No change needed unless the compiler complains about accessibility — it is in the same class.

- [ ] **Step 5: Build-check**

Run: `Tools/run-tests-headless.sh "ReplyParser|QuotedFieldsCache"`
Expected: PASS (project compiles; pure tests still green).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): resolve reply quotes in Normalize + carry through CreateViewModel

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Phase C — Send + compose state

### Task 6: `quoted_message_id` on `WappiSendTextRequest` (TDD)

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`
- Test: `Assets/Tests/Editor/Chat/WappiSendTextRequestTests.cs`

- [ ] **Step 1: Write the failing serialization test**

`Assets/Tests/Editor/Chat/WappiSendTextRequestTests.cs`:

```csharp
using NUnit.Framework;
using Newtonsoft.Json;

public class WappiSendTextRequestTests
{
    [Test]
    public void QuotedMessageId_Null_KeyOmitted()
    {
        var req = new WappiSendTextRequest { body = "hi", recipient = "123" };
        string json = JsonConvert.SerializeObject(req);
        Assert.IsFalse(json.Contains("quoted_message_id"), json);
    }

    [Test]
    public void QuotedMessageId_Set_KeyPresent()
    {
        var req = new WappiSendTextRequest { body = "hi", recipient = "123", quotedMessageId = "ABC123" };
        string json = JsonConvert.SerializeObject(req);
        StringAssert.Contains("quoted_message_id", json);
        StringAssert.Contains("ABC123", json);
    }
}
```

- [ ] **Step 2: Run to verify it FAILS**

Run: `Tools/run-tests-headless.sh "WappiSendTextRequest"`
Expected: FAIL — `quotedMessageId` does not exist.

- [ ] **Step 3: Add the field**

In `WappiSendTextRequest` (~line 1715):

```csharp
    [JsonProperty("quoted_message_id", NullValueHandling = NullValueHandling.Ignore)]
    public string quotedMessageId;
```

- [ ] **Step 4: Run to verify it PASSES**

Run: `Tools/run-tests-headless.sh "WappiSendTextRequest"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs \
        Assets/Tests/Editor/Chat/WappiSendTextRequestTests.cs Assets/Tests/Editor/Chat/WappiSendTextRequestTests.cs.meta
git commit -m "feat(chat): add quoted_message_id to WappiSendTextRequest (omitted when null)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 7: `quotedMessageId` on `OutboxStore.OutboxEntry` (TDD)

**Files:**
- Modify: `Assets/Scripts/Chat/OutboxStore.cs`
- Test: `Assets/Tests/Editor/Chat/OutboxEntryReplyTests.cs`

- [ ] **Step 1: Write the failing back-compat test**

`Assets/Tests/Editor/Chat/OutboxEntryReplyTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class OutboxEntryReplyTests
{
    [Test]
    public void LegacyEntry_MissingQuotedId_DeserializesNull()
    {
        var entry = JsonUtility.FromJson<OutboxStore.OutboxEntry>(
            "{\"tempId\":\"t1\",\"chatId\":\"c1\",\"text\":\"hi\"}");
        Assert.IsNull(entry.quotedMessageId);
    }

    [Test]
    public void Entry_QuotedId_RoundTrips()
    {
        var entry = new OutboxStore.OutboxEntry { tempId = "t1", chatId = "c1", text = "hi", quotedMessageId = "q1" };
        var back = JsonUtility.FromJson<OutboxStore.OutboxEntry>(JsonUtility.ToJson(entry));
        Assert.AreEqual("q1", back.quotedMessageId);
    }
}
```

- [ ] **Step 2: Run to verify it FAILS**

Run: `Tools/run-tests-headless.sh "OutboxEntryReply"`
Expected: FAIL — `quotedMessageId` does not exist on `OutboxEntry`.

- [ ] **Step 3: Add the field**

In `OutboxStore.OutboxEntry` (append-only, after the last existing field, ~line 49):

```csharp
        public string quotedMessageId;   // Non-null => this outbox message is a reply.
```

- [ ] **Step 4: Run to verify it PASSES**

Run: `Tools/run-tests-headless.sh "OutboxEntryReply"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/OutboxStore.cs \
        Assets/Tests/Editor/Chat/OutboxEntryReplyTests.cs Assets/Tests/Editor/Chat/OutboxEntryReplyTests.cs.meta
git commit -m "feat(chat): persist quotedMessageId on OutboxEntry (back-compat null)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 8: Reply compose state + event on `ChatManager`

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.Outbox.cs`

- [ ] **Step 1: Add reply state, event, and methods**

In the `ChatManager` partial in `ChatManager.Outbox.cs`, add:

```csharp
    private MessageViewModel _replyTarget;

    /// Fires whenever the active reply target changes. Null payload == reply cancelled.
    public event System.Action<MessageViewModel> OnReplyTargetChanged;

    /// The message the next sent text will quote, or null. Read by SendTextMessageRoutine.
    public MessageViewModel ReplyTarget => _replyTarget;

    /// Begin replying to a message. Ignored for not-yet-Sent messages (decision D1):
    /// their id is still a temp id and cannot be quoted on the wire.
    public void BeginReply(MessageViewModel target)
    {
        if (target == null) return;
        if (target.deliveryStatus == DeliveryStatus.Pending || target.deliveryStatus == DeliveryStatus.Failed) return;
        _replyTarget = target;
        OnReplyTargetChanged?.Invoke(target);
    }

    public void CancelReply()
    {
        if (_replyTarget == null) return;
        _replyTarget = null;
        OnReplyTargetChanged?.Invoke(null);
    }
```

> Note: `DeliveryStatus` enum member names — confirm `Pending`/`Failed` exist in `Assets/Scripts/Chat/DeliveryStatus.cs` (they are referenced in `MessageItemView`/`ChatManager`). Adjust to the exact member names if different.

- [ ] **Step 2: Build-check**

Run: `Tools/run-tests-headless.sh "ReplyParser"`
Expected: PASS (compiles).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.Outbox.cs
git commit -m "feat(chat): add BeginReply/CancelReply + OnReplyTargetChanged state

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 9: Thread the quote through the send + retry routines

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`
- Modify: `Assets/Scripts/Main/ChatManager.Outbox.cs`

- [ ] **Step 1: Snapshot + stamp in `SendTextMessageRoutine`**

In `SendTextMessageRoutine` (~1530), right after the existing `sendCacheRoot`/`tempId`/`now` snapshot and before the optimistic VM is built, capture and clear the reply target:

```csharp
        // Snapshot the reply target before any yield; clear it so the NEXT message
        // isn't a reply. D1: BeginReply only accepts Sent messages, so this id is real.
        MessageViewModel replyTarget = _replyTarget;
        string quotedId = replyTarget != null ? replyTarget.messageId : null;
        if (_replyTarget != null) CancelReply();
```

Then in the `instantMessage` initializer (~1550–1561), add the quoted snapshot so the optimistic bubble shows the quote instantly:

```csharp
            quotedMessageId    = replyTarget != null ? replyTarget.messageId : null,
            quotedSenderName   = replyTarget != null ? ReplyParser.SenderLabel(replyTarget.isIncoming, replyTarget.senderName) : null,
            quotedText         = replyTarget != null ? ReplyParser.SnippetFor(replyTarget.type, replyTarget.text) : null,
            quotedType         = replyTarget != null ? replyTarget.type : MessageType.Unknown,
            quotedThumbnailUrl = replyTarget != null ? (string.IsNullOrEmpty(replyTarget.thumbnailUrl) ? replyTarget.mediaUrl : replyTarget.thumbnailUrl) : null,
```

And set the outbox entry's quote (in the `Outbox.Add(new OutboxStore.OutboxEntry { ... })` initializer, ~1572):

```csharp
        quotedMessageId = quotedId,
```

Finally, pass it into the network half:

```csharp
    yield return PostTextMessageRoutine(chatId, text, tempId, activeProfileId, sendCacheRoot, quotedId);
```

- [ ] **Step 2: Add the parameter to `PostTextMessageRoutine`**

Change the signature (~1593) to add `string quotedMessageId` and set it on the request (~1603):

```csharp
private IEnumerator PostTextMessageRoutine(
    string chatId, string text, string tempId, string profileId, string sendCacheRoot, string quotedMessageId)
{
    ...
    var requestData = new WappiSendTextRequest { body = text, recipient = recipient, quotedMessageId = quotedMessageId };
    ...
}
```

- [ ] **Step 3: Thread the quote on the retry path**

In `ChatManager.Outbox.cs`, `RetryRoutine` (~51–54), pass the persisted quote when dispatching a text retry:

```csharp
        yield return PostTextMessageRoutine(entry.chatId, entry.text, tempId, entry.profileId, retryCacheRoot, entry.quotedMessageId);
```

(Adjust to the actual local variable names in `RetryRoutine`.)

- [ ] **Step 4: Build-check**

Run: `Tools/run-tests-headless.sh "WappiSendTextRequest|ReplyParser"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs Assets/Scripts/Main/ChatManager.Outbox.cs
git commit -m "feat(chat): attach quote to optimistic send, wire payload, and retry

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 10: Clear the reply target on chat-switch

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

- [ ] **Step 1: Call `CancelReply()` in `OpenChat`**

In `OpenChat` (~367–380), alongside the existing `seenMessageIds.Clear()` / reaction clear and **before** `currentChatId` is reassigned, add:

```csharp
        CancelReply();   // A pending reply must not leak into the newly-opened chat.
```

- [ ] **Step 2: Build-check**

Run: `Tools/run-tests-headless.sh "ReplyParser"`
Expected: PASS (compiles).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "fix(chat): cancel pending reply when switching chats

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 11: Reply preview bar in `MessagesBottomPanel`

**Files:**
- Modify: `Assets/Scripts/Chat/MessagesBottomPanel.cs`
- Modify (Editor): the messages bottom panel hierarchy in `Assets/Scenes/Main.unity`

- [ ] **Step 1: Add serialized refs + subscription**

In `MessagesBottomPanel`, add fields and wire show/hide:

```csharp
    [Header("Reply Preview")]
    [SerializeField] private GameObject replyPreviewBar;     // sibling ABOVE the input rect
    [SerializeField] private Image replyPreviewAccent;
    [SerializeField] private TextMeshProUGUI replyPreviewSender;
    [SerializeField] private TextMeshProUGUI replyPreviewSnippet;
    [SerializeField] private Button replyPreviewCancel;
```

In `OnEnable`, after the existing wiring:

```csharp
        if (ChatManager.Instance != null)
            ChatManager.Instance.OnReplyTargetChanged += HandleReplyTargetChanged;
        if (replyPreviewCancel != null)
        {
            replyPreviewCancel.onClick.RemoveAllListeners();
            replyPreviewCancel.onClick.AddListener(() => ChatManager.Instance?.CancelReply());
        }
        if (replyPreviewBar != null) replyPreviewBar.SetActive(false);
```

In `OnDisable`:

```csharp
        if (ChatManager.Instance != null)
            ChatManager.Instance.OnReplyTargetChanged -= HandleReplyTargetChanged;
```

Add the handler:

```csharp
    private void HandleReplyTargetChanged(MessageViewModel target)
    {
        if (replyPreviewBar == null) return;
        if (target == null) { replyPreviewBar.SetActive(false); return; }

        replyPreviewBar.SetActive(true);
        string sender = ReplyParser.SenderLabel(target.isIncoming, target.senderName);
        if (replyPreviewSender != null)
            replyPreviewSender.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(sender, MissingEmojiMode.Hide);
        if (replyPreviewSnippet != null)
            replyPreviewSnippet.text = ReplyParser.CleanSnippet(
                UnicodeEmojiConverter.ConvertRealEmojisToSprites(
                    ReplyParser.SnippetFor(target.type, target.text), MissingEmojiMode.Hide));
    }
```

- [ ] **Step 2: Build the preview-bar UI in the Editor**

In `Assets/Scenes/Main.unity`, under the WhatsApp messages screen's bottom panel, add a `ReplyPreviewBar` GameObject as a **sibling ABOVE** the `ExpandableInput`-controlled input rect (NOT a child of it — `ExpandableInput` snapshots `bottomPanelRect.rect.height` once in `Start`, and `KeyboardAwarePanel` stomps runtime offsets). Structure: accent bar (Image, left) | text column (`replyPreviewSender` bold + `replyPreviewSnippet` one-line) | cancel `Button` (✕). Use the project's white card style with `RoundedCorners`. Wire the five serialized refs on the `MessagesBottomPanel` component. Set `replyPreviewSnippet` to `NoWrap` + `Ellipsis` + `maxVisibleLines = 1`.

- [ ] **Step 3: Manual verification (Editor)**

Enter Play mode, open a chat, long-press or swipe a message to start a reply: the bar appears above the input with sender + snippet; tapping ✕ hides it; opening another chat hides it; sending a message hides it. Open the keyboard — the bar stays put and the input still auto-grows.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Chat/MessagesBottomPanel.cs Assets/Scenes/Main.unity
git commit -m "feat(chat): reply preview bar above the composer

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Phase D — Quoted card in the bubble

### Task 12: Add the `QuotedCard` child to both message prefabs

**Files:**
- Modify: `Assets/Prefabs/MessageTextIncoming.prefab` (+ `.meta`)
- Modify: `Assets/Prefabs/MessageTextOutgoing.prefab` (+ `.meta`)

- [ ] **Step 1: Build the card hierarchy (open Editor)**

In each prefab, under the `Bubble` child (the one with the `VerticalLayoutGroup`), add a `QuotedCard` GameObject **seeded just after `SenderName`**. Inner layout (its own `HorizontalLayoutGroup`):
- `Accent` — `Image`, 4px wide, full height (the `quotedAccentBar`).
- `TextColumn` — `VerticalLayoutGroup`: `Sender` (`TextMeshProUGUI`, bold, ~22px) + `Snippet` (`TextMeshProUGUI`, ~20px, `NoWrap`/`Ellipsis`/`maxVisibleLines=1`).
- `Thumb` — `Image`, 32×32 (the `quotedThumbnail`), with `RoundedCorners`.

Give `QuotedCard` a `LayoutElement` with a clamped `preferredWidth` (e.g. `444`) so `childForceExpandWidth=true` doesn't stretch it. Background: light tint via null-sprite + `RoundedCorners` (per project UI memory — never `UISprite.psd`). Set `QuotedCard` inactive by default.

- [ ] **Step 2: Commit the prefab seeds (refs wired in Task 13)**

```bash
git add Assets/Prefabs/MessageTextIncoming.prefab Assets/Prefabs/MessageTextIncoming.prefab.meta \
        Assets/Prefabs/MessageTextOutgoing.prefab Assets/Prefabs/MessageTextOutgoing.prefab.meta
git commit -m "feat(chat): add QuotedCard child to message prefabs

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 13: Render the quoted card in `MessageItemView`

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs`
- Modify: both prefabs (wire serialized refs)

- [ ] **Step 1: Add serialized refs + a separate disposables list**

In the field block:

```csharp
    [Header("Reply Quote")]
    [SerializeField] private GameObject quotedCard;
    [SerializeField] private Image quotedAccentBar;
    [SerializeField] private TextMeshProUGUI quotedSenderText;
    [SerializeField] private TextMeshProUGUI quotedSnippetText;
    [SerializeField] private Image quotedThumbnail;

    // Quoted-thumbnail textures are owned here, NOT in _ownedDisposables — DisposeOwned()
    // runs at the head of every ApplyTextureAspectFill and would otherwise destroy them.
    private readonly System.Collections.Generic.List<UnityEngine.Object> _quotedDisposables = new System.Collections.Generic.List<UnityEngine.Object>();
```

- [ ] **Step 2: Implement `RenderQuotedCard` + free in `OnDestroy`**

```csharp
    private void DisposeQuoted()
    {
        for (int i = 0; i < _quotedDisposables.Count; i++)
            if (_quotedDisposables[i] != null) Destroy(_quotedDisposables[i]);
        _quotedDisposables.Clear();
    }

    private void RenderQuotedCard(MessageViewModel vm)
    {
        DisposeQuoted();
        if (quotedCard == null) return;

        if (vm == null || string.IsNullOrEmpty(vm.quotedMessageId))
        {
            quotedCard.SetActive(false);
            return;
        }

        quotedCard.SetActive(true);

        bool quotedIsOwn = vm.quotedSenderName == "You";
        if (quotedAccentBar != null)
            quotedAccentBar.color = quotedIsOwn ? new Color32(0x1F, 0xA8, 0x55, 0xFF) : GetSenderColor(vm.quotedSenderName);

        if (quotedSenderText != null)
        {
            quotedSenderText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(vm.quotedSenderName, MissingEmojiMode.Hide);
            quotedSenderText.color = quotedIsOwn ? new Color32(0x1F, 0xA8, 0x55, 0xFF) : GetSenderColor(vm.quotedSenderName);
        }

        if (quotedSnippetText != null)
            quotedSnippetText.text = ReplyParser.CleanSnippet(
                UnicodeEmojiConverter.ConvertRealEmojisToSprites(vm.quotedText, MissingEmojiMode.Hide));

        bool isMediaQuote = vm.quotedType == MessageType.Image || vm.quotedType == MessageType.Video;
        if (quotedThumbnail != null)
        {
            bool shown = false;
            if (isMediaQuote && !string.IsNullOrEmpty(vm.quotedThumbnailUrl))
            {
                string key = ThumbnailKeyResolver.Resolve(vm.quotedThumbnailUrl, vm.quotedMessageId,
                    MediaCacheManager.Instance.IsImageCached);
                Texture2D tex = MediaCacheManager.Instance.LoadImageFromCache(key);
                if (tex != null)
                {
                    var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    _quotedDisposables.Add(spr);
                    quotedThumbnail.sprite = spr;
                    shown = true;
                }
            }
            quotedThumbnail.gameObject.SetActive(shown);
        }

        // Tap -> scroll to original.
        var btn = quotedCard.GetComponent<Button>();
        if (btn == null) btn = quotedCard.AddComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
        {
            if (ScrollClickBlocker.IsBlocking) return;
            var list = GetComponentInParent<MessageListView>();
            if (list != null) list.ScrollToMessage(vm.quotedMessageId);
        });
    }
```

Add `DisposeQuoted();` to `OnDestroy()` (next to `DisposeOwned();`).

> `MediaCacheManager.LoadImageFromCache` / `ThumbnailKeyResolver.Resolve` signatures: verify against the call already used in `DisplayMedia` (~line 2200) and match it exactly. If `LoadImageFromCache` returns ownership differently, mirror whatever `DisplayMedia` does — just keep the result out of `_ownedDisposables`.

- [ ] **Step 3: Call it from `Bind`**

In `Bind`, right after the sender-name logic (~line 487) and before the per-type media branches, add:

```csharp
        RenderQuotedCard(vm);
```

- [ ] **Step 4: Place the card in `ReorderBubbleSiblings`**

In `ReorderBubbleSiblings` (~841), after the `senderNameText` block and before the `orderedMedia` array loop:

```csharp
        if (quotedCard != null && quotedCard.activeSelf)
            quotedCard.transform.SetSiblingIndex(currentIndex++);
```

- [ ] **Step 5: Clamp negative spacing when the card is present in `ApplyDynamicLayout`**

In the image/video branch (the no-caption case that sets `layout.spacing = -42`, ~1110) and the audio branch (`-34`, ~1073), guard the negative value:

```csharp
        // A quoted card is a top child; negative spacing would pull it onto the media.
        if (quotedCard != null && quotedCard.activeSelf && layout.spacing < 0f) layout.spacing = 0f;
```

Place this guard immediately after each branch sets its negative `layout.spacing`.

- [ ] **Step 6: Wire the five serialized refs in BOTH prefabs**

In the open Editor, assign `quotedCard`/`quotedAccentBar`/`quotedSenderText`/`quotedSnippetText`/`quotedThumbnail` on the `MessageItemView` component of `MessageTextIncoming.prefab` and `MessageTextOutgoing.prefab`.

- [ ] **Step 7: Build-check + manual verification**

Run: `Tools/run-tests-headless.sh "ReplyParser"` → PASS (compiles).
Then in the Editor Play mode at 1080×2400, verify: incoming reply shows the quote (sender color + snippet); outgoing reply shows "You" + green accent; an image-quote shows a 32px thumbnail; a no-caption image bubble that quotes another message does NOT overlap the quote onto the image; short one-word replies don't stretch full-width; the outgoing (green) bubble + `MirrorSize` look correct.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs \
        Assets/Prefabs/MessageTextIncoming.prefab Assets/Prefabs/MessageTextIncoming.prefab.meta \
        Assets/Prefabs/MessageTextOutgoing.prefab Assets/Prefabs/MessageTextOutgoing.prefab.meta
git commit -m "feat(chat): render quoted card in message bubbles

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Phase E — Triggers

### Task 14: `SwipeToReply` gesture

**Files:**
- Create: `Assets/Scripts/Chat/SwipeToReply.cs`
- Modify: both prefabs (add the component + a reply-arrow child)
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (reset on Bind)

- [ ] **Step 1: Implement the component (modeled on `AudioWaveform`)**

`Assets/Scripts/Chat/SwipeToReply.cs`:

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Swipe-right-to-reply on a message bubble. Per-bubble child of the message ScrollRect:
/// horizontal-right drags translate the bubble and reveal a reply arrow; vertical drags
/// are forwarded to the parent ScrollRect (modeled on AudioWaveform, NOT SwipeToBack —
/// never toggles ScrollRect.vertical, which is shared by every bubble).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SwipeToReply : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private RectTransform bubble;        // the inner "Bubble" rect to translate
    [SerializeField] private GameObject replyArrow;       // revealed icon
    [SerializeField] private MessageItemView itemView;    // for BoundVm
    [SerializeField] private float commitDistance = 90f;

    private ScrollRect _parentScroll;
    private bool _routeToParent;
    private bool _claimed;
    private float _startX;

    void Awake() => _parentScroll = GetComponentInParent<ScrollRect>();

    /// Called from MessageItemView.Bind to clear residual offset on a re-bound bubble.
    public void ResetState()
    {
        _routeToParent = false; _claimed = false;
        if (bubble != null) bubble.anchoredPosition = new Vector2(0f, bubble.anchoredPosition.y);
        if (replyArrow != null) replyArrow.SetActive(false);
    }

    public void OnInitializePotentialDrag(PointerEventData e) => _parentScroll?.OnInitializePotentialDrag(e);

    public void OnBeginDrag(PointerEventData e)
    {
        bool horizontal = Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y);
        _routeToParent = !horizontal || e.delta.x < 0f;   // vertical OR left-swipe -> scroll
        if (_routeToParent) { _parentScroll?.OnBeginDrag(e); return; }
        _claimed = true;
        _startX = bubble != null ? bubble.anchoredPosition.x : 0f;
        if (replyArrow != null) replyArrow.SetActive(true);
    }

    public void OnDrag(PointerEventData e)
    {
        if (_routeToParent) { _parentScroll?.OnDrag(e); return; }
        if (!_claimed || bubble == null) return;
        float dx = Mathf.Clamp(e.position.x - e.pressPosition.x, 0f, commitDistance * 1.4f);
        bubble.anchoredPosition = new Vector2(_startX + dx, bubble.anchoredPosition.y);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (_routeToParent) { _parentScroll?.OnEndDrag(e); _routeToParent = false; return; }
        _claimed = false;
        float dx = e.position.x - e.pressPosition.x;
        if (dx >= commitDistance && itemView != null && itemView.BoundVm != null)
            ChatManager.Instance?.BeginReply(itemView.BoundVm);
        ResetState();
    }
}
```

> Forward **all four** handlers to `_parentScroll` on the vertical branch — `SnappyFlickScrollRect`'s flick math needs the full sequence. Adjust the snap-back to a short DOTween if a hard reset looks abrupt.

- [ ] **Step 2: Reset on Bind**

In `MessageItemView`, add a `[SerializeField] private SwipeToReply swipeToReply;` ref and call `swipeToReply?.ResetState();` at the END of `Bind`.

- [ ] **Step 3: Wire in both prefabs**

Add `SwipeToReply` to the bubble root of each prefab; add a `ReplyArrow` icon child (hidden by default); assign `bubble` (the inner Bubble rect), `replyArrow`, and `itemView`. Ensure a raycast-target Graphic exists on the drag target (the bubble background Image works).

- [ ] **Step 4: Manual verification (Editor)**

Swipe a bubble right → arrow reveals, bubble follows, release past threshold starts a reply (preview bar appears); short swipe snaps back with no reply; vertical drag still scrolls the list smoothly; left-swipe does nothing but scroll.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/SwipeToReply.cs Assets/Scripts/Chat/SwipeToReply.cs.meta \
        Assets/Scripts/UI/MessageItemView.cs \
        Assets/Prefabs/MessageTextIncoming.prefab Assets/Prefabs/MessageTextIncoming.prefab.meta \
        Assets/Prefabs/MessageTextOutgoing.prefab Assets/Prefabs/MessageTextOutgoing.prefab.meta
git commit -m "feat(chat): swipe-right-to-reply gesture on bubbles

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 15: Long-press `MessageActionMenu` with Reply

**Files:**
- Create: `Assets/Scripts/Chat/MessageActionMenu.cs`
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (long-press detection)
- Modify: `Assets/Scenes/Main.unity` (menu popup, PopupUI-style)

- [ ] **Step 1: Long-press detection in `MessageItemView`**

Add a hold detector that starts on pointer-down, cancels on drag/up, and opens the menu after ~0.45s if movement < tolerance and `!ScrollClickBlocker.IsBlocking`. Implement via `IPointerDownHandler`/`IPointerUpHandler` + a coroutine (mirror `DragShield`'s `maxMoveFromDown` tolerance). On fire: `MessageActionMenu.Instance.Show(BoundVm)`.

```csharp
    private Coroutine _holdRoutine;
    private const float HoldSeconds = 0.45f;
    private const float HoldMoveTolerance = 15f;

    private System.Collections.IEnumerator HoldRoutine()
    {
        yield return new WaitForSeconds(HoldSeconds);
        if (!ScrollClickBlocker.IsBlocking && currentVm != null)
            MessageActionMenu.Instance?.Show(currentVm);
        _holdRoutine = null;
    }
```

Start in `OnPointerDown`, `StopCoroutine` + null in `OnPointerUp`/`OnBeginDrag` (and if the pointer moves beyond tolerance).

- [ ] **Step 2: Implement the menu controller (PopupUI-style)**

`Assets/Scripts/Chat/MessageActionMenu.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;

/// Long-press context menu over a message bubble. Built to hold Copy/Forward/React later;
/// v1 exposes Reply only.
public class MessageActionMenu : MonoBehaviour
{
    public static MessageActionMenu Instance { get; private set; }

    [SerializeField] private GameObject root;       // backdrop + card
    [SerializeField] private Button backdropButton;
    [SerializeField] private Button replyButton;

    private MessageViewModel _target;

    void Awake()
    {
        Instance = this;
        if (root != null) root.SetActive(false);
        if (backdropButton != null) backdropButton.onClick.AddListener(Hide);
        if (replyButton != null) replyButton.onClick.AddListener(() =>
        {
            if (_target != null) ChatManager.Instance?.BeginReply(_target);
            Hide();
        });
    }

    public void Show(MessageViewModel target)
    {
        _target = target;
        if (root != null) root.SetActive(true);
    }

    public void Hide()
    {
        _target = null;
        if (root != null) root.SetActive(false);
    }
}
```

- [ ] **Step 3: Build the menu UI (Editor)**

In `Main.unity`, add a `MessageActionMenu` panel inside the WhatsApp messages screen (per project memory: sheets live inside their screen panel, not canvas root). Backdrop `Image` (dim) + a `Card` with a `Reply` row (icon + label). Use `EventAbsorber` on the card and DOTween OutBack/InBack for show/hide (follow `PopupUI`). Wire `root`/`backdropButton`/`replyButton`.

- [ ] **Step 4: Manual verification (Editor)**

Long-press a bubble → menu pops; Reply starts a reply (preview bar appears); tapping the backdrop dismisses; a normal tap (open photo) or a fling does NOT open the menu; long-press on a Pending/Failed outgoing bubble → Reply is a no-op (D1).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/MessageActionMenu.cs Assets/Scripts/Chat/MessageActionMenu.cs.meta \
        Assets/Scripts/UI/MessageItemView.cs Assets/Scenes/Main.unity
git commit -m "feat(chat): long-press action menu with Reply

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Phase F — Scroll to original

### Task 16: `ScrollTargetMath` (TDD)

**Files:**
- Create: `Assets/Scripts/Chat/ScrollTargetMath.cs`
- Test: `Assets/Tests/Editor/Chat/ScrollTargetMathTests.cs`

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/Editor/Chat/ScrollTargetMathTests.cs`:

```csharp
using NUnit.Framework;

public class ScrollTargetMathTests
{
    [Test]
    public void ShortContent_ReturnsZero()
    {
        Assert.AreEqual(0f, ScrollTargetMath.CenteredNormalizedPosition(500f, 800f, 1f));
    }

    [Test]
    public void CentersTarget()
    {
        // target = 1000 - 800*0.4 = 680; 1 - 680/2000 = 0.66
        Assert.AreEqual(0.66f, ScrollTargetMath.CenteredNormalizedPosition(1000f, 800f, 2000f), 0.001f);
    }

    [Test]
    public void ClampsAboveTop_ReturnsOne()
    {
        Assert.AreEqual(1f, ScrollTargetMath.CenteredNormalizedPosition(100f, 800f, 2000f), 0.001f);
    }

    [Test]
    public void ClampsBelowBottom_ReturnsZero()
    {
        Assert.AreEqual(0f, ScrollTargetMath.CenteredNormalizedPosition(99999f, 800f, 2000f), 0.001f);
    }
}
```

- [ ] **Step 2: Run to verify it FAILS**

Run: `Tools/run-tests-headless.sh "ScrollTargetMath"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement**

`Assets/Scripts/Chat/ScrollTargetMath.cs`:

```csharp
using UnityEngine;

/// Pure math for landing a target bubble ~40% down the viewport when jumping to a
/// quoted original. verticalNormalizedPosition: 1 = top, 0 = bottom.
public static class ScrollTargetMath
{
    public static float CenteredNormalizedPosition(float distanceFromTop, float viewportHeight, float scrollableHeight)
    {
        if (scrollableHeight <= 1f) return 0f;
        float target = Mathf.Clamp(distanceFromTop - viewportHeight * 0.4f, 0f, scrollableHeight);
        return 1f - target / scrollableHeight;
    }
}
```

- [ ] **Step 4: Run to verify it PASSES**

Run: `Tools/run-tests-headless.sh "ScrollTargetMath"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/ScrollTargetMath.cs Assets/Scripts/Chat/ScrollTargetMath.cs.meta \
        Assets/Tests/Editor/Chat/ScrollTargetMathTests.cs Assets/Tests/Editor/Chat/ScrollTargetMathTests.cs.meta
git commit -m "feat(chat): ScrollTargetMath for centered scroll-to-original

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

### Task 17: `ScrollToMessage` + `FlashHighlight`

**Files:**
- Modify: `Assets/Scripts/UI/MessageListView.cs`
- Modify: `Assets/Scripts/UI/MessageItemView.cs`
- Modify: both prefabs (highlight overlay Image)

- [ ] **Step 1: Add `ScrollToMessage` to `MessageListView`**

After `ScrollSeparatorToTop` (~989), using the same world-corner→content-local math and the `HandleScrollToBottomClicked` tween pattern:

```csharp
    private Tween _scrollToMessageTween;

    public void ScrollToMessage(string messageId)
    {
        if (string.IsNullOrEmpty(messageId) || content == null || scrollRect == null) return;

        MessageItemView target = null;
        for (int i = 0; i < content.childCount; i++)
        {
            var v = content.GetChild(i).GetComponent<MessageItemView>();
            if (v != null && v.BoundVm != null && v.BoundVm.messageId == messageId) { target = v; break; }
        }
        if (target == null) return;   // not instantiated -> v1 no-op (D5)

        Canvas.ForceUpdateCanvases();
        var contentRt = (RectTransform)content;
        var targetRt = (RectTransform)target.transform;
        var corners = new Vector3[4];
        targetRt.GetWorldCorners(corners);
        Vector3 localTop = contentRt.InverseTransformPoint(corners[1]);
        float scrollableH = Mathf.Max(0f, contentRt.rect.height - ((RectTransform)scrollRect.viewport).rect.height);
        float distanceFromTop = Mathf.Clamp(contentRt.rect.yMax - localTop.y, 0f, scrollableH);
        float viewportH = ((RectTransform)scrollRect.viewport).rect.height;
        float targetNp = ScrollTargetMath.CenteredNormalizedPosition(distanceFromTop, viewportH, scrollableH);

        _scrollToMessageTween?.Kill();
        scrollRect.velocity = Vector2.zero;
        isLoadingData = true;   // suppress pagination during the programmatic scroll
        _scrollToMessageTween = DOTween.To(
            () => scrollRect.verticalNormalizedPosition,
            v => scrollRect.verticalNormalizedPosition = v,
            targetNp, 0.3f).SetEase(Ease.OutCubic)
            .OnComplete(() => { isLoadingData = false; target.FlashHighlight(); });
    }
```

> Confirm the exact field names (`content`, `scrollRect`, `scrollRect.viewport`, `isLoadingData`) against `MessageListView`; mirror `ScrollSeparatorToTop`'s actual references. Kill `_scrollToMessageTween` in `OnDisable` and `OnChatSelected` alongside `_scrollToBottomTween`.

- [ ] **Step 2: Add `FlashHighlight` to `MessageItemView` (overlay Image)**

Add a `[SerializeField] private Image highlightOverlay;` (a full-bubble child Image, `raycastTarget=false`, transparent by default) to both prefabs, and:

```csharp
    public void FlashHighlight()
    {
        if (highlightOverlay == null) return;
        highlightOverlay.DOKill();
        var c = highlightOverlay.color; c.a = 0f; highlightOverlay.color = c;
        highlightOverlay.gameObject.SetActive(true);
        highlightOverlay.DOFade(0.25f, 0.18f).SetLoops(2, LoopType.Yoyo)
            .OnComplete(() => highlightOverlay.gameObject.SetActive(false));
    }
```

(Overlay flash, not a `bubbleBackground.color` tween — works on transparent sticker/jumbo bubbles and is immune to `UpdateBubbleVisuals`.)

- [ ] **Step 3: Build-check + manual verification**

Run: `Tools/run-tests-headless.sh "ScrollTargetMath"` → PASS.
In the Editor: tap a quoted card whose original is on-screen → the list scrolls to center it and it flashes; tap a quote whose original is paged-out → nothing happens (no error). Pagination is not spuriously triggered by the jump.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/MessageListView.cs Assets/Scripts/UI/MessageItemView.cs \
        Assets/Prefabs/MessageTextIncoming.prefab Assets/Prefabs/MessageTextIncoming.prefab.meta \
        Assets/Prefabs/MessageTextOutgoing.prefab Assets/Prefabs/MessageTextOutgoing.prefab.meta
git commit -m "feat(chat): tap quoted card to scroll to + flash the original

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Phase G — End-to-end verification

### Task 18: Full device/Editor pass

**Files:** none (verification).

- [ ] **Step 1: Run the full EditMode suite**

Run: `Tools/run-tests-headless.sh` (no filter)
Expected: all tests PASS (ReplyParser, QuotedFieldsCache, WappiSendTextRequest, OutboxEntryReply, ScrollTargetMath, plus pre-existing suites).

- [ ] **Step 2: End-to-end scenarios (Editor Play / device)**

Verify each: receive an incoming text reply (quote renders, correct sender color); receive an incoming reply to your image (thumbnail in quote); swipe-to-reply and long-press→Reply both open the preview bar; send the reply (optimistic bubble shows the quote instantly, server ack keeps it); reopen the chat (sent reply still shows the quote from cache); tap a quote to scroll+flash the original; quote a still-sending message → trigger is a no-op; switch chats mid-reply → preview bar clears; keyboard open with the preview bar visible behaves.

- [ ] **Step 3: Final commit (if any polish edits)**

```bash
git add -A
git commit -m "polish(chat): reply-to-message end-to-end verification fixes

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-review (completed during planning)

**Spec coverage:** §4 data model → Tasks 2,4; §5 ReplyParser → Task 3; §6 Normalize/CreateViewModel/FindActiveById → Task 5; §7 send/compose → Tasks 6–11; §8 lifecycle (L1 chat-switch → Task 10; L2/L3 made N/A by D1 default → Task 8 guard); §9 bubble card → Tasks 12–13; §10 triggers → Tasks 14–15; §11 scroll → Tasks 16–17; §12 Phase 0 → Task 1; §14 tests → embedded in Tasks 3,4,6,7,16. All covered.

**Placeholder scan:** code shown for every code step; UI/prefab/gesture steps are explicitly manual-verify with exact hierarchy + checks (not unit-testable). The two `> Note:` callouts (DeliveryStatus member names, MediaCacheManager/MessageListView field names) flag names to confirm against the live code during execution, not deferred design.

**Type consistency:** `quoted*` field names, `QuotedPreview`, `ReplyParser.Resolve/SenderLabel/SnippetFor/CleanSnippet`, `BeginReply/CancelReply/OnReplyTargetChanged/ReplyTarget`, `quotedMessageId` (request + outbox), `FindActiveById`, `ScrollToMessage`, `ScrollTargetMath.CenteredNormalizedPosition`, `FlashHighlight`, `RenderQuotedCard`, `_quotedDisposables` — used consistently across tasks.
