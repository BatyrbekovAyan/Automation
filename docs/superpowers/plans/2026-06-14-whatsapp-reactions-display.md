# WhatsApp Reactions (Display + Persistence) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display incoming WhatsApp emoji reactions on the message bubble they target, persisted so they survive chat reopen. (Sending reactions is a deliberately separate later phase.)

**Architecture:** Reactions arrive from Wappi as standalone `type:"reaction"` events whose `stanzaId` equals the target message's `id`. They are intercepted at the three points where `ChatManager` currently filters `Unknown` messages, reduced into per-reactor state by a pure `ReactionStore`, stored inline on the target `MessageViewModel.reactions` list (so they ride the existing `ChatHistoryCache` for free), and pushed to the rendered bubble in place via a new `OnMessageReactionsChanged` event — mirroring the established `OnMessageMediaRefreshed` mutate-in-place pattern. A floating `ReactionPillView` on each message prefab renders an aggregated, group-aware neutral pill.

**Tech Stack:** Unity 6 (6000.3.9f1), C# 9, Newtonsoft.Json (network), JsonUtility (cache), TMPro + EmojiOne sprite asset, Nobi.UiRoundedCorners, NUnit EditMode tests via the project test bridge.

---

## Design Summary (decisions locked during brainstorming)

- **Receive/display only this phase.** No send UI, no `/message/react` endpoint, no who-reacted detail sheet (`raycastTarget = false`).
- **Pill style:** single **neutral light pill** (white fill + soft border) on both incoming and outgoing bubbles — good contrast over the light-green outgoing bubble.
- **Group-aware:** reactor identity keyed by `from` (jid) so multiple group participants aggregate correctly. Pill shows distinct emojis (cap 3) + total reactor count when ≥ 2 reactors.
- **Chat-list preview unchanged** — `ChatPreviewFormatter` already renders Wappi's pre-formatted reaction sentence from `last_message_data`. Do not touch it.
- **Known v1 limitations (acceptable):** an un-react whose removal event has aged out of the latest sync window won't retroactively clear a cached reaction; reactions on far-back messages outside the ~100-message cached window are re-derived on demand rather than persisted; no detail sheet.

### Reaction event shape (confirmed from live Wappi payloads)
```
type      = "reaction"
body      = "😘"   (the emoji; empty string => un-react/remove)
stanzaId  = "3ADF387319908DDAF3E3"   (== target message id; bare, same format)
id        = "3A20BE12126FFBB94E2E"   (the reaction event's OWN id, distinct from stanzaId)
from      = "77026998844@c.us"       (reactor jid — needed for group aggregation)
fromMe    = true/false
senderName, chatId, time
```
`RawMessage` currently has **no** `stanzaId` or `from` field — both must be added (Task 1).

---

## File Structure

**Create:**
- `Assets/Scripts/Chat/MessageReaction.cs` — `[Serializable]` data row stored on a message (emoji, reactorKey, senderName, fromMe, time).
- `Assets/Scripts/Chat/ReactionParser.cs` — `ReactionEvent` model + static `ReactionParser` (turns a `RawMessage` into a `ReactionEvent`, computes the stable `reactorKey`). Pure, unit-tested.
- `Assets/Scripts/Chat/ReactionStore.cs` — stateful reducer: apply event to a target message, buffer reactions whose target isn't loaded, drain on arrival. Pure logic, unit-tested.
- `Assets/Scripts/Chat/ReactionSummary.cs` — static aggregation for display (distinct emojis cap 3 + reactor count). Pure, unit-tested.
- `Assets/Scripts/UI/ReactionPillView.cs` — `MonoBehaviour` that renders a pill from `List<MessageReaction>`; re-renders on `EmojiPatchService.OnEmojiReady`.
- `Assets/Editor/MessageReactionPillBuilder.cs` — `[MenuItem]` builder that adds the `ReactionPill` GameObject + `ReactionPillView` to both message prefabs and wires the serialized refs (mirrors `ChatItemUnreadBadgeBuilder`).
- `Assets/Tests/Editor/Chat/ReactionParserTests.cs`
- `Assets/Tests/Editor/Chat/ReactionStoreTests.cs`
- `Assets/Tests/Editor/Chat/ReactionSummaryTests.cs`

**Modify:**
- `Assets/Scripts/Chat/MessageType.cs` — append `Reaction`.
- `Assets/Scripts/Chat/RawMessage.cs` — add `stanzaId`, `from`.
- `Assets/Scripts/UI/MessageViewModel.cs` — add `using System.Collections.Generic;` + `public List<MessageReaction> reactions;`.
- `Assets/Scripts/Main/ChatManager.cs` — `OnMessageReactionsChanged` event, `_reactions` field, `ParseMessageType` case, clear on chat switch, three intercepts + two drains + `HandleReactionEvent` helper.
- `Assets/Scripts/UI/MessageItemView.cs` — `reactionPill` field, subscribe/unsubscribe, `HandleReactionsChanged`, render in `Bind`, `PositionReactionPill`.
- `Assets/Prefabs/MessageTextIncoming.prefab`, `Assets/Prefabs/MessageTextOutgoing.prefab` — via the builder (Task 8), not by hand.

---

## Conventions for every task

**Running tests** (per the project test bridge — see `CLAUDE.md` → "Running EditMode tests"):
- **Editor open (preferred):** create the trigger `Temp/claude/run-tests.trigger`, then read the result from `Temp/claude/test-summary.json`.
- **Editor closed:** `Tools/run-tests-headless.sh "<filterRegex>"` — writes NUnit results to `Tools/test-output/`. The first run of a task imports new `.cs` files and generates their `.meta`.

**Commits** (per the established loop — verify GREEN first, then commit per-task **with user consent**):
- Stage the `.cs` **and** its Unity-generated `.meta` (the `.meta` exists only after Unity has imported the file — i.e. after a test-bridge run or an editor focus).
- Conventional-commit style matching this repo (`feat(chat): …`, `feat(ui): …`).
- End the commit message with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

**Branch first:** before the first commit, create a feature branch off `main` (the repo's default):
```bash
git checkout -b feat/whatsapp-reactions
```

---

## Task 1: Data model scaffolding

No behaviour yet — just the types every later task references. Verified by compilation.

**Files:**
- Modify: `Assets/Scripts/Chat/MessageType.cs`
- Modify: `Assets/Scripts/Chat/RawMessage.cs`
- Create: `Assets/Scripts/Chat/MessageReaction.cs`
- Modify: `Assets/Scripts/UI/MessageViewModel.cs`

- [ ] **Step 1: Append `Reaction` to the message-type enum (at the END to preserve serialized int values)**

`Assets/Scripts/Chat/MessageType.cs`:
```csharp
public enum MessageType
{
    Chat,
    Image,
    Video,
    Audio,
    Voice,
    Sticker,
    Document,
    Unknown,
    Reaction
}
```

- [ ] **Step 2: Add `stanzaId` and `from` to `RawMessage`**

`Assets/Scripts/Chat/RawMessage.cs` — add these fields (JSON keys `stanzaId` and `from` match the field names, so Newtonsoft binds them automatically; no `[JsonProperty]` needed):
```csharp
    public string stanzaId;   // For reactions: id of the target message being reacted to.
    public string from;       // Reactor jid (group aggregation keys on this, not senderName).
```
Place them alongside the existing scalar fields (e.g. after `public string caption;`).

- [ ] **Step 3: Create the `MessageReaction` data row**

Create `Assets/Scripts/Chat/MessageReaction.cs`:
```csharp
using System;

/// <summary>
/// One person's reaction to a message. Stored inline on the target
/// MessageViewModel.reactions so it persists in ChatHistoryCache. [Serializable]
/// with public primitive fields so Unity's JsonUtility round-trips it.
/// </summary>
[Serializable]
public class MessageReaction
{
    public string emoji;       // Raw unicode emoji, e.g. "😘". Converted to a TMP sprite at render time.
    public string reactorKey;  // Stable per-reactor identity: "me" or the reactor jid.
    public string senderName;  // Display name of the reactor.
    public bool fromMe;        // Reaction came from the account owner.
    public long time;          // Reaction event time (unix seconds).
}
```

- [ ] **Step 4: Add the `reactions` list to `MessageViewModel`**

`Assets/Scripts/UI/MessageViewModel.cs`:
- Add to the usings at the top:
```csharp
using System.Collections.Generic;
```
- Add this field (near the other persisted fields, e.g. after `public DeliveryStatus deliveryStatus;`):
```csharp
    // Reactions targeting this message, keyed per-reactor by ReactionStore.
    // Null or empty == no reactions. JsonUtility serializes List<MessageReaction>.
    public List<MessageReaction> reactions;
```

- [ ] **Step 5: Verify it compiles**

Run (editor open): create `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json`.
Or (editor closed): `Tools/run-tests-headless.sh "MessageOrderTests"` (any existing filter — we only need a clean compile here).
Expected: build succeeds, existing suite still PASSES (no test added this task).

- [ ] **Step 6: Commit (on consent)**
```bash
git add Assets/Scripts/Chat/MessageType.cs Assets/Scripts/Chat/MessageType.cs.meta \
        Assets/Scripts/Chat/RawMessage.cs Assets/Scripts/Chat/RawMessage.cs.meta \
        Assets/Scripts/Chat/MessageReaction.cs Assets/Scripts/Chat/MessageReaction.cs.meta \
        Assets/Scripts/UI/MessageViewModel.cs Assets/Scripts/UI/MessageViewModel.cs.meta
git commit -m "feat(chat): add reaction data model (MessageType.Reaction, RawMessage.stanzaId/from, MessageReaction, VM.reactions)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: ReactionParser (TDD)

Turn a raw Wappi reaction into a typed `ReactionEvent`, and compute the stable reactor key. Pure — fully unit-testable.

**Files:**
- Create: `Assets/Tests/Editor/Chat/ReactionParserTests.cs`
- Create: `Assets/Scripts/Chat/ReactionParser.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/ReactionParserTests.cs`:
```csharp
using NUnit.Framework;
using Newtonsoft.Json.Linq;

public class ReactionParserTests
{
    private static RawMessage Raw(string type, string stanzaId, object body,
                                  bool fromMe = false, string from = "", string sender = "") =>
        new RawMessage
        {
            type = type,
            stanzaId = stanzaId,
            body = body == null ? null : JToken.FromObject(body),
            fromMe = fromMe,
            from = from,
            senderName = sender,
            time = 42
        };

    [Test]
    public void FromRaw_ParsesIncomingReaction()
    {
        var ev = ReactionParser.FromRaw(Raw("reaction", "T1", "😘", fromMe: false, from: "111@c.us", sender: "Zhanym"));

        Assert.IsNotNull(ev);
        Assert.AreEqual("T1", ev.targetId);
        Assert.AreEqual("😘", ev.emoji);
        Assert.AreEqual("111@c.us", ev.reactorKey);   // not-fromMe keys on jid
        Assert.IsFalse(ev.IsRemoval);
        Assert.AreEqual(42, ev.time);
    }

    [Test]
    public void FromRaw_FromMe_KeysOnMe()
    {
        var ev = ReactionParser.FromRaw(Raw("reaction", "T1", "👍", fromMe: true, from: "999@c.us", sender: "Ayan"));
        Assert.AreEqual("me", ev.reactorKey);
    }

    [Test]
    public void FromRaw_EmptyBody_IsRemoval()
    {
        var ev = ReactionParser.FromRaw(Raw("reaction", "T1", "", from: "111@c.us"));
        Assert.IsNotNull(ev);
        Assert.IsTrue(ev.IsRemoval);
        Assert.AreEqual("", ev.emoji);
    }

    [Test]
    public void FromRaw_NullBody_IsRemoval()
    {
        var ev = ReactionParser.FromRaw(Raw("reaction", "T1", null, from: "111@c.us"));
        Assert.IsNotNull(ev);
        Assert.IsTrue(ev.IsRemoval);
    }

    [Test]
    public void FromRaw_NonReactionType_ReturnsNull()
    {
        Assert.IsNull(ReactionParser.FromRaw(Raw("chat", "T1", "hi")));
    }

    [Test]
    public void FromRaw_MissingStanzaId_ReturnsNull()
    {
        Assert.IsNull(ReactionParser.FromRaw(Raw("reaction", "", "😘")));
    }

    [Test]
    public void ReactorKey_FallsBackJidThenSenderThenUnknown()
    {
        Assert.AreEqual("me", ReactionParser.ReactorKey(true, "x", "y"));
        Assert.AreEqual("jid", ReactionParser.ReactorKey(false, "jid", "name"));
        Assert.AreEqual("name", ReactionParser.ReactorKey(false, "", "name"));
        Assert.AreEqual("unknown", ReactionParser.ReactorKey(false, "", ""));
    }
}
```

- [ ] **Step 2: Run it — verify it fails**

Run: `Tools/run-tests-headless.sh "ReactionParserTests"` (or the editor trigger).
Expected: FAIL — `ReactionParser` / `ReactionEvent` do not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Chat/ReactionParser.cs`:
```csharp
/// <summary>
/// A parsed Wappi reaction event. targetId == the reacted-to message id (stanzaId).
/// An empty emoji means the reactor removed their reaction.
/// </summary>
public class ReactionEvent
{
    public string targetId;
    public string emoji;
    public string reactorKey;
    public string senderName;
    public bool fromMe;
    public long time;

    public bool IsRemoval => string.IsNullOrEmpty(emoji);
}

/// <summary>
/// Parses RawMessage reaction events. Pure/static so it is unit-testable and
/// callable from the ChatManager message loops without a MonoBehaviour.
/// </summary>
public static class ReactionParser
{
    /// <summary>
    /// Returns a ReactionEvent for a usable reaction, or null when the raw is not a
    /// reaction or lacks a target. Stores the emoji RAW (unconverted) — display layers
    /// convert to a TMP sprite so a not-yet-downloaded emoji survives for re-conversion.
    /// </summary>
    public static ReactionEvent FromRaw(RawMessage raw)
    {
        if (raw == null) return null;
        if (raw.type != "reaction") return null;
        if (string.IsNullOrEmpty(raw.stanzaId)) return null;

        return new ReactionEvent
        {
            targetId = raw.stanzaId,
            emoji = raw.body?.ToString() ?? "",
            reactorKey = ReactorKey(raw.fromMe, raw.from, raw.senderName),
            senderName = raw.senderName,
            fromMe = raw.fromMe,
            time = raw.time
        };
    }

    /// <summary>
    /// Stable identity for "who reacted". Account owner is always "me"; otherwise the
    /// reactor jid (group-safe), falling back to senderName then a constant.
    /// </summary>
    public static string ReactorKey(bool fromMe, string from, string senderName)
    {
        if (fromMe) return "me";
        if (!string.IsNullOrEmpty(from)) return from;
        if (!string.IsNullOrEmpty(senderName)) return senderName;
        return "unknown";
    }
}
```

- [ ] **Step 4: Run it — verify it passes**

Run: `Tools/run-tests-headless.sh "ReactionParserTests"`.
Expected: PASS (7 tests).

- [ ] **Step 5: Commit (on consent)**
```bash
git add Assets/Scripts/Chat/ReactionParser.cs Assets/Scripts/Chat/ReactionParser.cs.meta \
        Assets/Tests/Editor/Chat/ReactionParserTests.cs Assets/Tests/Editor/Chat/ReactionParserTests.cs.meta
git commit -m "feat(chat): parse Wappi reaction events into ReactionEvent

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: ReactionStore (TDD)

The reducer: apply an event to its target message in place, buffer reactions whose target isn't loaded, drain when the target arrives. Stateful but pure (no Unity deps beyond `MessageViewModel`).

**Files:**
- Create: `Assets/Tests/Editor/Chat/ReactionStoreTests.cs`
- Create: `Assets/Scripts/Chat/ReactionStore.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/ReactionStoreTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class ReactionStoreTests
{
    private static MessageViewModel Msg(string id) => new MessageViewModel { messageId = id };

    private static ReactionEvent Ev(string target, string emoji, string reactor, long time = 1) =>
        new ReactionEvent { targetId = target, emoji = emoji, reactorKey = reactor, time = time };

    [Test]
    public void Apply_AddsReactionToFoundTarget()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel> { Msg("A") };

        var hit = store.Apply(Ev("A", "❤️", "111"), msgs);

        Assert.AreSame(msgs[0], hit);
        Assert.AreEqual(1, msgs[0].reactions.Count);
        Assert.AreEqual("❤️", msgs[0].reactions[0].emoji);
    }

    [Test]
    public void Apply_ReplacesSameReactorsEmoji()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel> { Msg("A") };

        store.Apply(Ev("A", "❤️", "111", 1), msgs);
        var hit = store.Apply(Ev("A", "😂", "111", 2), msgs);

        Assert.AreSame(msgs[0], hit);
        Assert.AreEqual(1, msgs[0].reactions.Count);     // replaced, not appended
        Assert.AreEqual("😂", msgs[0].reactions[0].emoji);
    }

    [Test]
    public void Apply_RemovalDeletesReactorsEntry()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel> { Msg("A") };

        store.Apply(Ev("A", "❤️", "111"), msgs);
        var hit = store.Apply(Ev("A", "", "111"), msgs);   // empty emoji = un-react

        Assert.AreSame(msgs[0], hit);
        Assert.AreEqual(0, msgs[0].reactions.Count);
    }

    [Test]
    public void Apply_DifferentReactorsAggregate()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel> { Msg("A") };

        store.Apply(Ev("A", "❤️", "me"), msgs);
        store.Apply(Ev("A", "👍", "111"), msgs);

        Assert.AreEqual(2, msgs[0].reactions.Count);
    }

    [Test]
    public void Apply_SameEmojiTwice_IsIdempotentNoOp()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel> { Msg("A") };

        store.Apply(Ev("A", "❤️", "111"), msgs);
        var second = store.Apply(Ev("A", "❤️", "111"), msgs);   // re-delivered on next sync

        Assert.IsNull(second);                                  // no change => null
        Assert.AreEqual(1, msgs[0].reactions.Count);
    }

    [Test]
    public void Apply_TargetNotLoaded_BuffersAndDrainsOnArrival()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel>();                // target not present yet

        var hit = store.Apply(Ev("A", "❤️", "111"), msgs);
        Assert.IsNull(hit);                                     // buffered

        var late = Msg("A");
        Assert.IsTrue(store.DrainInto(late));                   // applies buffered reaction
        Assert.AreEqual(1, late.reactions.Count);
        Assert.AreEqual("❤️", late.reactions[0].emoji);

        Assert.IsFalse(store.DrainInto(Msg("A")));              // buffer consumed
    }

    [Test]
    public void Buffer_CollapsesByReactor_LatestEmojiWins()
    {
        var store = new ReactionStore();
        store.Apply(Ev("A", "❤️", "111", 1), new List<MessageViewModel>());
        store.Apply(Ev("A", "😂", "111", 2), new List<MessageViewModel>());

        var late = Msg("A");
        store.DrainInto(late);
        Assert.AreEqual(1, late.reactions.Count);
        Assert.AreEqual("😂", late.reactions[0].emoji);
    }

    [Test]
    public void Clear_DropsPending()
    {
        var store = new ReactionStore();
        store.Apply(Ev("A", "❤️", "111"), new List<MessageViewModel>());
        store.Clear();
        Assert.IsFalse(store.DrainInto(Msg("A")));
    }
}
```

- [ ] **Step 2: Run it — verify it fails**

Run: `Tools/run-tests-headless.sh "ReactionStoreTests"`.
Expected: FAIL — `ReactionStore` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Chat/ReactionStore.cs`:
```csharp
using System.Collections.Generic;

/// <summary>
/// Reduces reaction events into per-message reaction state. Reactions arrive
/// before their (older) target during newest-first pagination, so events whose
/// target isn't loaded are buffered per (target, reactor) — latest wins — and
/// drained when the target message later enters the list. One ReactionStore per
/// open chat; Clear() on chat switch.
/// </summary>
public class ReactionStore
{
    // targetMessageId -> (reactorKey -> latest buffered event)
    private readonly Dictionary<string, Dictionary<string, ReactionEvent>> _pending
        = new Dictionary<string, Dictionary<string, ReactionEvent>>();

    /// <summary>
    /// Apply an event to its target in <paramref name="messages"/>. Returns the mutated
    /// target VM when state actually changed (caller fires OnMessageReactionsChanged +
    /// marks the cache dirty), or null when the target wasn't found (buffered) or the
    /// event was a no-op (idempotent re-delivery).
    /// </summary>
    public MessageViewModel Apply(ReactionEvent ev, IReadOnlyList<MessageViewModel> messages)
    {
        if (ev == null || string.IsNullOrEmpty(ev.targetId)) return null;

        var target = FindById(messages, ev.targetId);
        if (target != null)
            return ApplyToMessage(target, ev) ? target : null;

        Buffer(ev);
        return null;
    }

    /// <summary>
    /// Apply any buffered events targeting <paramref name="message"/>. Returns true if
    /// the message's reactions changed. Called right after a fresh VM is created from a
    /// server page, so a reaction seen earlier in the same (or a newer) page lands.
    /// </summary>
    public bool DrainInto(MessageViewModel message)
    {
        if (message == null || string.IsNullOrEmpty(message.messageId)) return false;
        if (!_pending.TryGetValue(message.messageId, out var byReactor)) return false;

        bool changed = false;
        foreach (var ev in byReactor.Values)
            changed |= ApplyToMessage(message, ev);

        _pending.Remove(message.messageId);
        return changed;
    }

    public void Clear() => _pending.Clear();

    /// <summary>
    /// Pure set/replace/remove of one reactor's reaction on a message. Returns true if
    /// the reactions list changed. Idempotent: re-applying the same emoji is a no-op.
    /// </summary>
    public static bool ApplyToMessage(MessageViewModel message, ReactionEvent ev)
    {
        if (message == null || ev == null) return false;
        message.reactions ??= new List<MessageReaction>();

        int idx = message.reactions.FindIndex(r => r.reactorKey == ev.reactorKey);

        if (ev.IsRemoval)
        {
            if (idx < 0) return false;
            message.reactions.RemoveAt(idx);
            return true;
        }

        if (idx >= 0)
        {
            var existing = message.reactions[idx];
            if (existing.emoji == ev.emoji) return false;   // idempotent re-delivery
            existing.emoji = ev.emoji;
            existing.time = ev.time;
            existing.senderName = ev.senderName;
            existing.fromMe = ev.fromMe;
            return true;
        }

        message.reactions.Add(new MessageReaction
        {
            emoji = ev.emoji,
            reactorKey = ev.reactorKey,
            senderName = ev.senderName,
            fromMe = ev.fromMe,
            time = ev.time
        });
        return true;
    }

    private void Buffer(ReactionEvent ev)
    {
        if (!_pending.TryGetValue(ev.targetId, out var byReactor))
        {
            byReactor = new Dictionary<string, ReactionEvent>();
            _pending[ev.targetId] = byReactor;
        }
        byReactor[ev.reactorKey] = ev;   // latest wins
    }

    private static MessageViewModel FindById(IReadOnlyList<MessageViewModel> messages, string id)
    {
        if (messages == null) return null;
        for (int i = 0; i < messages.Count; i++)
            if (messages[i] != null && messages[i].messageId == id) return messages[i];
        return null;
    }
}
```

- [ ] **Step 4: Run it — verify it passes**

Run: `Tools/run-tests-headless.sh "ReactionStoreTests"`.
Expected: PASS (8 tests).

- [ ] **Step 5: Commit (on consent)**
```bash
git add Assets/Scripts/Chat/ReactionStore.cs Assets/Scripts/Chat/ReactionStore.cs.meta \
        Assets/Tests/Editor/Chat/ReactionStoreTests.cs Assets/Tests/Editor/Chat/ReactionStoreTests.cs.meta
git commit -m "feat(chat): reduce reaction events with pending-target buffering (ReactionStore)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: ReactionSummary (TDD)

Aggregate a message's reactions into what the pill shows: distinct emojis (cap 3) + total reactor count.

**Files:**
- Create: `Assets/Tests/Editor/Chat/ReactionSummaryTests.cs`
- Create: `Assets/Scripts/Chat/ReactionSummary.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/ReactionSummaryTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class ReactionSummaryTests
{
    private static MessageReaction R(string emoji, string reactor) =>
        new MessageReaction { emoji = emoji, reactorKey = reactor };

    [Test]
    public void Build_NullOrEmpty_IsZero()
    {
        var (e1, c1) = ReactionSummary.Build(null);
        Assert.AreEqual(0, e1.Count);
        Assert.AreEqual(0, c1);

        var (e2, c2) = ReactionSummary.Build(new List<MessageReaction>());
        Assert.AreEqual(0, e2.Count);
        Assert.AreEqual(0, c2);
    }

    [Test]
    public void Build_SingleReactor()
    {
        var (emojis, count) = ReactionSummary.Build(new List<MessageReaction> { R("❤️", "me") });
        CollectionAssert.AreEqual(new[] { "❤️" }, emojis);
        Assert.AreEqual(1, count);
    }

    [Test]
    public void Build_TwoReactorsSameEmoji_OneDistinctCountTwo()
    {
        var (emojis, count) = ReactionSummary.Build(new List<MessageReaction>
        {
            R("❤️", "me"), R("❤️", "111")
        });
        CollectionAssert.AreEqual(new[] { "❤️" }, emojis);
        Assert.AreEqual(2, count);
    }

    [Test]
    public void Build_PreservesFirstSeenOrder()
    {
        var (emojis, _) = ReactionSummary.Build(new List<MessageReaction>
        {
            R("😂", "a"), R("❤️", "b"), R("👍", "c")
        });
        CollectionAssert.AreEqual(new[] { "😂", "❤️", "👍" }, emojis);
    }

    [Test]
    public void Build_CapsDistinctEmojisAtThree_CountReflectsAll()
    {
        var (emojis, count) = ReactionSummary.Build(new List<MessageReaction>
        {
            R("😂", "a"), R("❤️", "b"), R("👍", "c"), R("🔥", "d")
        });
        Assert.AreEqual(3, emojis.Count);                 // capped
        CollectionAssert.AreEqual(new[] { "😂", "❤️", "👍" }, emojis);
        Assert.AreEqual(4, count);                        // count still counts everyone
    }
}
```

- [ ] **Step 2: Run it — verify it fails**

Run: `Tools/run-tests-headless.sh "ReactionSummaryTests"`.
Expected: FAIL — `ReactionSummary` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Chat/ReactionSummary.cs`:
```csharp
using System.Collections.Generic;

/// <summary>
/// Display aggregation for the reaction pill: the distinct emojis to show
/// (first-seen order, capped) and the total number of reactors. Pure/static.
/// </summary>
public static class ReactionSummary
{
    public const int MaxEmojis = 3;

    public static (List<string> emojis, int count) Build(List<MessageReaction> reactions)
    {
        var emojis = new List<string>();
        if (reactions == null || reactions.Count == 0) return (emojis, 0);

        foreach (var r in reactions)
        {
            if (r == null || string.IsNullOrEmpty(r.emoji)) continue;
            if (emojis.Count < MaxEmojis && !emojis.Contains(r.emoji))
                emojis.Add(r.emoji);
        }
        return (emojis, reactions.Count);
    }
}
```

- [ ] **Step 4: Run it — verify it passes**

Run: `Tools/run-tests-headless.sh "ReactionSummaryTests"`.
Expected: PASS (5 tests).

- [ ] **Step 5: Commit (on consent)**
```bash
git add Assets/Scripts/Chat/ReactionSummary.cs Assets/Scripts/Chat/ReactionSummary.cs.meta \
        Assets/Tests/Editor/Chat/ReactionSummaryTests.cs Assets/Tests/Editor/Chat/ReactionSummaryTests.cs.meta
git commit -m "feat(chat): aggregate reactions for pill display (ReactionSummary)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: ChatManager integration

Wire the reducer into the message pipeline: parse the type, intercept reaction events at all three filter points, drain pending onto freshly loaded messages, persist, and notify the UI. No reaction ever becomes a bubble.

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

Verified by: existing suite still GREEN + a manual reaction event no longer produces a phantom row (Task 9 device check). There is no new EditMode test here because every intercept point is inside a `MonoBehaviour` coroutine that calls the network; the reducer logic it delegates to is already covered by Tasks 2–4.

- [ ] **Step 1: Add the `OnMessageReactionsChanged` event**

In the events block (right after `OnMessageMediaRefreshed`, ~line 65):
```csharp
    /// <summary>
    /// Fires when a reaction is added/changed/removed on an already-loaded message.
    /// Carries the same MessageViewModel reference held in the active cache — its
    /// .reactions list is already mutated, so a listener re-renders its pill in place.
    /// </summary>
    public event Action<MessageViewModel> OnMessageReactionsChanged;
```

- [ ] **Step 2: Add the per-chat ReactionStore field**

Near the other private state fields (e.g. beside `seenMessageIds`):
```csharp
    private readonly ReactionStore _reactions = new ReactionStore();
```

- [ ] **Step 3: Add the `"reaction"` case to `ParseMessageType` (~line 1269)**
```csharp
            "document" => MessageType.Document,
            "reaction" => MessageType.Reaction,
            _ => MessageType.Unknown
```

- [ ] **Step 4: Clear the store on chat switch**

In `SelectChat`, beside the existing `seenMessageIds.Clear();` (~line 353):
```csharp
        _reactions.Clear();
```

- [ ] **Step 5: Add the shared reaction handler helper**

Add this private method to `ChatManager` (place it near `RefreshCachedMessageMedia`):
```csharp
    /// <summary>
    /// Applies a reaction event to the open chat's messages. Returns true if the
    /// cache changed (caller marks dirty / saves). Fires OnMessageReactionsChanged
    /// for the in-place bubble update. Reactions targeting a not-yet-loaded message
    /// are buffered by ReactionStore and applied when that message arrives.
    /// </summary>
    private bool HandleReactionEvent(RawMessage raw, IReadOnlyList<MessageViewModel> messages)
    {
        ReactionEvent ev = ReactionParser.FromRaw(raw);
        if (ev == null) return false;

        MessageViewModel target = _reactions.Apply(ev, messages);
        if (target == null) return false;   // buffered, or idempotent no-op

        OnMessageReactionsChanged?.Invoke(target);
        return true;
    }
```

- [ ] **Step 6: Intercept in the live/sync path (`SyncLatestMessages`)**

In the brand-new branch, immediately after `NormalizedMessage norm = Normalize(raw);` and BEFORE `if (norm.messageType == MessageType.Unknown) continue;` (~line 451–452):
```csharp
                        NormalizedMessage norm = Normalize(raw);

                        if (norm.messageType == MessageType.Reaction)
                        {
                            if (HandleReactionEvent(raw, cachedList)) hasStatusUpdates = true;
                            continue;
                        }

                        if (norm.messageType == MessageType.Unknown) continue;
```
Then, right after the new VM is built (`var newVm = CreateViewModel(norm);` ~line 497), drain any reaction buffered before this target loaded:
```csharp
                        var newVm = CreateViewModel(norm);
                        _reactions.DrainInto(newVm);
                        newVm.sequence = serverSequence;
                        newMessages.Add(newVm);
                        continue;
```
(`hasStatusUpdates == true` already routes to `ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, cachedList);` at ~line 612, so reaction-only changes persist.)

- [ ] **Step 7: Intercept in the batch/paginated path (`GetMessagesRoutine`)**

After `NormalizedMessage norm = Normalize(raw);` and BEFORE `if (norm.messageType == MessageType.Unknown) continue;` (~line 1013–1014):
```csharp
                    NormalizedMessage norm = Normalize(raw);

                    if (norm.messageType == MessageType.Reaction)
                    {
                        if (HandleReactionEvent(raw, _activeChatCache ?? loadedMessages)) cacheDirty = true;
                        continue;
                    }

                    if (norm.messageType == MessageType.Unknown) continue;
```
Then, right after `MessageViewModel vm = CreateViewModel(norm);` (~line 1016), drain pending onto the fresh page message:
```csharp
                    MessageViewModel vm = CreateViewModel(norm);
                    _reactions.DrainInto(vm);
                    vm.sequence = MessageOrder.WithinSecondSequence(responseTimes, rawServerCount - 1);
                    loadedMessages.Add(vm);
```
(No extra save needed here — these page VMs persist through the normal cache-merge of the loaded page.)

- [ ] **Step 8: Intercept in the cache-validation refresh path (`ValidateCachePageAgainstServer`)**

Replace the loop body (~line 924–931) so it parses once and routes reactions:
```csharp
                foreach (var raw in response.messages)
                {
                    if (string.IsNullOrEmpty(raw.id)) continue;

                    NormalizedMessage norm = Normalize(raw);
                    if (norm.messageType == MessageType.Reaction)
                    {
                        if (HandleReactionEvent(raw, _activeChatCache)) cacheDirty = true;
                        continue;
                    }

                    if (RefreshCachedMessageMedia(norm, _activeChatCache))
                    {
                        cacheDirty = true;
                    }
                }
```

- [ ] **Step 9: Verify the existing suite still passes (no regressions)**

Run (editor open): create `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json`.
Or (editor closed): `Tools/run-tests-headless.sh ""` (full suite).
Expected: build succeeds, ALL existing tests + Tasks 2–4 tests PASS.

- [ ] **Step 10: Commit (on consent)**
```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): route reaction events into reaction store across batch/live/refresh paths

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: ReactionPillView component

A runtime view that renders the aggregated pill text from a message's reactions and re-renders when a missing emoji's sprite finishes downloading.

**Files:**
- Create: `Assets/Scripts/UI/ReactionPillView.cs`

Verified by: compilation (Task 8 wires it; Task 9 shows it on device).

- [ ] **Step 1: Write the component**

Create `Assets/Scripts/UI/ReactionPillView.cs`:
```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Renders a neutral reaction pill (emoji[s] + count) onto a message bubble.
/// Hidden when there are no reactions. The emoji string is converted to TMP
/// sprite tags at render time (display layer) and re-rendered when a previously
/// missing emoji's sprite is downloaded.
/// </summary>
public class ReactionPillView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;

    private List<MessageReaction> _last;

    private void OnEnable()  { EmojiPatchService.OnEmojiReady += HandleEmojiReady; }
    private void OnDisable() { EmojiPatchService.OnEmojiReady -= HandleEmojiReady; }

    /// <summary>Render the pill for a message's reactions (null/empty hides it).</summary>
    public void Render(List<MessageReaction> reactions)
    {
        _last = reactions;

        var (emojis, count) = ReactionSummary.Build(reactions);
        if (emojis.Count == 0)
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }

        if (!gameObject.activeSelf) gameObject.SetActive(true);

        string raw = string.Concat(emojis);
        string sprites = UnicodeEmojiConverter.ConvertRealEmojisToSprites(raw, MissingEmojiMode.Hide);
        if (label != null)
            label.text = count >= 2 ? $"{sprites} {count}" : sprites;
    }

    public bool HasReactions => _last != null && _last.Count > 0;

    private void HandleEmojiReady(string spriteName)
    {
        if (HasReactions) Render(_last);
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `Tools/run-tests-headless.sh "ReactionSummaryTests"` (a clean build confirms the component type-checks).
Expected: build succeeds.

- [ ] **Step 3: Commit (on consent)**
```bash
git add Assets/Scripts/UI/ReactionPillView.cs Assets/Scripts/UI/ReactionPillView.cs.meta
git commit -m "feat(ui): ReactionPillView renders aggregated reaction pill

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: MessageItemView wiring

Give the bubble a `reactionPill` reference, render it on `Bind`, and update it in place when reactions change. (The serialized field must exist before Task 8's builder can wire it.)

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs`

- [ ] **Step 1: Add the serialized reference**

In the field block (e.g. under a new header near the other UI refs, ~line 48):
```csharp
    [Header("Reactions")]
    [SerializeField] private ReactionPillView reactionPill;
```

- [ ] **Step 2: Subscribe / unsubscribe to the event (mirror the media-refresh wiring)**

In `OnEnable`, in the `if (ChatManager.Instance != null)` block (~line 248):
```csharp
            ChatManager.Instance.OnMessageMediaRefreshed += HandleMediaRefreshed;
            ChatManager.Instance.OnMessageReactionsChanged += HandleReactionsChanged;
```
In `OnDisable`, in the matching block (~line 278):
```csharp
            ChatManager.Instance.OnMessageMediaRefreshed -= HandleMediaRefreshed;
            ChatManager.Instance.OnMessageReactionsChanged -= HandleReactionsChanged;
```

- [ ] **Step 3: Render reactions during Bind**

In `Bind`, right after `currentVm = vm;` (~line 455):
```csharp
        currentVm = vm;
        currentShowTail = showTail;

        RenderReactions();
```

- [ ] **Step 4: Add the handler + render helper (mirror `HandleMediaRefreshed`)**

Add near `HandleMediaRefreshed` (~line 4100):
```csharp
    private void HandleReactionsChanged(MessageViewModel changed)
    {
        if (currentVm == null || changed == null) return;
        if (currentVm.messageId != changed.messageId) return;

        // ChatManager mutates the cached VM in place, so currentVm.reactions is
        // already current. Re-render the pill only — no full re-bind needed.
        RenderReactions();
    }

    private void RenderReactions()
    {
        if (reactionPill == null) return;
        reactionPill.Render(currentVm != null ? currentVm.reactions : null);
        PositionReactionPill();
    }

    /// <summary>
    /// Places the pill at the bubble's bottom trailing-inner corner, hanging below
    /// the bottom edge, and reserves row clearance so the hanging half does not
    /// collide with the next message. Incoming bubbles anchor the pill to the right,
    /// outgoing to the left. Exact insets/clearance are tuned in-editor (Task 9).
    /// </summary>
    private void PositionReactionPill()
    {
        if (reactionPill == null) return;
        // Placement is authored on the prefab by MessageReactionPillBuilder (anchors,
        // pivot, offsets per incoming/outgoing). This hook exists for the in-editor
        // tuning pass and any future runtime clearance adjustment; leave as a no-op
        // until Task 9 determines whether runtime clearance is needed.
    }
```

- [ ] **Step 5: Verify it compiles**

Run: `Tools/run-tests-headless.sh "ReactionSummaryTests"` (clean build).
Expected: build succeeds, suite PASSES. (`reactionPill` is unassigned until Task 8 — `RenderReactions` null-guards it, so no NRE.)

- [ ] **Step 6: Commit (on consent)**
```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "feat(ui): wire reaction pill render + in-place update into MessageItemView

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Editor builder — add the pill to both prefabs

Build the `ReactionPill` GameObject (neutral rounded pill + TMP label + `ReactionPillView`) into `MessageTextIncoming.prefab` and `MessageTextOutgoing.prefab`, and wire `MessageItemView.reactionPill` + `ReactionPillView.label`. Mirrors `ChatItemUnreadBadgeBuilder` exactly (idempotent, `PrefabUtility.LoadPrefabContents` → build → `SerializedObject` wire → `SaveAsPrefabAsset`).

**Files:**
- Create: `Assets/Editor/MessageReactionPillBuilder.cs`

> **Pill placement note:** the pill is parented to the bubble container so it tracks the bubble's size/position. The builder must locate the bubble transform (the child that holds `bubbleBackground` + the `VerticalLayoutGroup`). Confirm its name when running the builder; the constant `BubbleName` below is the expected name and must be adjusted to match the actual prefab if different. Incoming anchors the pill to the bubble's bottom-right, outgoing to the bottom-left. Final pixel offsets are tuned in Task 9.

- [ ] **Step 1: Write the builder**

Create `Assets/Editor/MessageReactionPillBuilder.cs`:
```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Nobi.UiRoundedCorners;

/// <summary>
/// Adds a floating ReactionPill to a message bubble prefab and wires the
/// MessageItemView.reactionPill + ReactionPillView.label serialized refs.
///
/// Pill structure (under the bubble container, ignoreLayout so it does not
/// disturb the bubble's VerticalLayoutGroup):
///
///   ReactionPill (Image + ImageWithRoundedCorners + LayoutElement + HLG + ContentSizeFitter + ReactionPillView)
///     Label (TMP — emoji sprites + optional count)
///
/// Run BOTH menu items after Task 7 (MessageItemView must already have the
/// 'reactionPill' field). Idempotent — re-running destroys any existing pill.
/// </summary>
public static class MessageReactionPillBuilder
{
    private const string IncomingPath = "Assets/Prefabs/MessageTextIncoming.prefab";
    private const string OutgoingPath = "Assets/Prefabs/MessageTextOutgoing.prefab";
    private const string BubbleName = "Bubble";   // adjust if the prefab names it differently
    private const string PillName = "ReactionPill";
    private const string LabelName = "Label";

    // Neutral light pill: near-white fill, soft gray border tone baked via a slightly
    // darker outline pill is overkill for v1 — a single off-white fill reads as neutral
    // on both the white incoming bubble and the light-green outgoing bubble.
    private static readonly Color PillFill = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color LabelColor = new Color32(0x11, 0x1B, 0x21, 0xFF);

    [MenuItem("Tools/Chat/Add Reaction Pill To Incoming Bubble")]
    public static void BuildIncoming() => Build(IncomingPath, incoming: true);

    [MenuItem("Tools/Chat/Add Reaction Pill To Outgoing Bubble")]
    public static void BuildOutgoing() => Build(OutgoingPath, incoming: false);

    private static void Build(string prefabPath, bool incoming)
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[ReactionPill] Failed to load prefab at {prefabPath}");
            return;
        }

        try
        {
            var bubble = FindChildRecursive(prefabRoot.transform, BubbleName);
            if (bubble == null)
            {
                Debug.LogError($"[ReactionPill] '{BubbleName}' not found under {prefabPath}. " +
                               "Set BubbleName to the actual bubble-container object name.");
                return;
            }

            var existing = bubble.Find(PillName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var pill = BuildPill(bubble, incoming);
            var label = BuildLabel(pill.transform);
            var view = pill.GetComponent<ReactionPillView>();

            WireViewLabel(view, label);

            if (!WireMessageItemViewRef(prefabRoot, view))
                return;

            // Start hidden — Render() activates it when a message has reactions.
            pill.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            Debug.Log($"[ReactionPill] Built pill under {prefabPath} → {BubbleName}/{PillName} (incoming={incoming})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static GameObject BuildPill(Transform parent, bool incoming)
    {
        var pill = new GameObject(
            PillName,
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter),
            typeof(ImageWithRoundedCorners),
            typeof(ReactionPillView));
        pill.transform.SetParent(parent, false);
        pill.transform.SetAsLastSibling();

        var rt = (RectTransform)pill.transform;
        // Trailing-inner corner of the bubble; hang below the bottom edge. Pivot at top
        // so anchoredPosition.y moves the pill downward out of the bubble. Tuned in Task 9.
        float x = incoming ? 1f : 0f;
        rt.anchorMin = new Vector2(x, 0f);
        rt.anchorMax = new Vector2(x, 0f);
        rt.pivot = new Vector2(x, 1f);
        rt.anchoredPosition = new Vector2(incoming ? -16f : 16f, 4f);
        rt.sizeDelta = new Vector2(0f, 52f);   // width driven by ContentSizeFitter

        var image = pill.GetComponent<Image>();
        image.color = PillFill;
        image.raycastTarget = false;           // non-interactive in v1 (no detail sheet)

        var le = pill.GetComponent<LayoutElement>();
        le.ignoreLayout = true;

        var hlg = pill.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 16, 4, 4);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var fitter = pill.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var rounded = pill.GetComponent<ImageWithRoundedCorners>();
        rounded.radius = 26f;                  // full pill (half of height)
        rounded.Validate();
        rounded.Refresh();

        return pill;
    }

    private static TextMeshProUGUI BuildLabel(Transform parent)
    {
        var go = new GameObject(LabelName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.fontSize = 30f;
        tmp.color = LabelColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        return tmp;
    }

    private static void WireViewLabel(ReactionPillView view, TextMeshProUGUI label)
    {
        var so = new SerializedObject(view);
        var labelProp = so.FindProperty("label");
        if (labelProp == null)
        {
            Debug.LogError("[ReactionPill] ReactionPillView is missing the 'label' field.");
            return;
        }
        labelProp.objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static bool WireMessageItemViewRef(GameObject prefabRoot, ReactionPillView view)
    {
        var item = prefabRoot.GetComponent<MessageItemView>();
        if (item == null)
        {
            Debug.LogError("[ReactionPill] No MessageItemView component on prefab root.");
            return false;
        }

        var so = new SerializedObject(item);
        var pillProp = so.FindProperty("reactionPill");
        if (pillProp == null)
        {
            Debug.LogError("[ReactionPill] MessageItemView is missing the 'reactionPill' field. Did Task 7 land?");
            return false;
        }
        pillProp.objectReferenceValue = view;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindChildRecursive(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }
}
#endif
```

- [ ] **Step 2: Confirm the bubble container name, then run both builders (editor open, focused)**

1. Open `MessageTextIncoming.prefab`; confirm the child object that holds `bubbleBackground` + the `VerticalLayoutGroup`. If it is not literally named `Bubble`, set `BubbleName` accordingly.
2. Run menu: **Tools ▸ Chat ▸ Add Reaction Pill To Incoming Bubble**.
3. Run menu: **Tools ▸ Chat ▸ Add Reaction Pill To Outgoing Bubble**.
   - Via the Unity MCP if available: `execute_menu_item` for each path.
   - Confirm the console logs `[ReactionPill] Built pill …` twice with no errors, and that `MessageItemView.reactionPill` is assigned on both prefab roots.

- [ ] **Step 3: Verify build is clean**

Run: `Tools/run-tests-headless.sh ""` (full suite) — or the editor trigger.
Expected: build succeeds, all tests PASS.

- [ ] **Step 4: Commit (on consent)** — stage the builder + both modified prefabs + their `.meta`:
```bash
git add Assets/Editor/MessageReactionPillBuilder.cs Assets/Editor/MessageReactionPillBuilder.cs.meta \
        Assets/Prefabs/MessageTextIncoming.prefab Assets/Prefabs/MessageTextOutgoing.prefab
git commit -m "feat(ui): build reaction pill into both message prefabs

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: End-to-end verification, placement tuning, persistence check

Make it look right and prove it works against real data.

- [ ] **Step 1: Visual placement pass (editor Game view, 1080×2400)**

Bind sample messages with reactions (or run against a live chat that has reactions) and confirm:
- Incoming bubble: pill hangs off the **bottom-right**, overlapping the bubble's bottom edge, not clipped.
- Outgoing bubble: pill hangs off the **bottom-left**.
- Pill reads as a neutral light pill on both the white and the light-green bubble.
- Multi-reactor bubble shows distinct emojis + count (e.g. `😂 ❤️ 2`).
Tune `anchoredPosition`, `sizeDelta`, `padding`, `radius`, and `fontSize` in `MessageReactionPillBuilder` and re-run the menu items until it matches the approved mockup.

- [ ] **Step 2: Row-clearance check**

Confirm the hanging pill does **not** overlap the next message. If it does, reserve clearance — preferred: add a layout-driven `ReactionSpacer` (a `LayoutElement`-only child the bubble's `VerticalLayoutGroup` lays out last) toggled by `ReactionPillView`/`MessageItemView` when reactions are present (`preferredHeight = clearance` else `0`), and implement the toggle inside `PositionReactionPill()`. Add this only if Step 1 shows a collision; document the chosen mechanism in the commit.

- [ ] **Step 3: Live-update check**

With a chat open, have someone react to a visible message on the real WhatsApp account. Confirm the pill appears within a sync cycle (no reopen), changing the emoji updates it, and un-reacting removes it — all without the reaction appearing as its own bubble.

- [ ] **Step 4: Persistence check**

React to a message, close the chat, reopen it. Confirm the reaction renders instantly from cache (before the sync completes). Inspect the cache file to confirm `reactions` is persisted:
```bash
# CurrentBotId-scoped path; find the active chat's cache file:
find "$HOME/Library/Application Support" -path "*BotCache*messages*.json" 2>/dev/null
# then grep one for the field:
# grep -o '"reactions":\[[^]]*\]' <file> | head
```
Expected: the target message object contains a populated `"reactions":[ … ]` array.

- [ ] **Step 5: Full regression**

Run the complete EditMode suite (editor trigger or `Tools/run-tests-headless.sh ""`).
Expected: ALL tests PASS (existing + Tasks 2–4).

- [ ] **Step 6: Final commit (on consent)** — any tuning/clearance changes from this task:
```bash
git add Assets/Editor/MessageReactionPillBuilder.cs \
        Assets/Prefabs/MessageTextIncoming.prefab Assets/Prefabs/MessageTextOutgoing.prefab \
        Assets/Scripts/UI/MessageItemView.cs Assets/Scripts/UI/ReactionPillView.cs
git commit -m "feat(ui): tune reaction pill placement and row clearance

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review (completed)

- **Spec coverage:** parse (T2) · reduce + buffer/drain (T3) · aggregate (T4) · intercept all three filter points + drain + persist + notify (T5) · render (T6) · in-place update (T7) · pill in both prefabs (T8) · placement/persistence verified (T9). Chat-list preview intentionally untouched. ✓
- **Type consistency:** `ReactionEvent{targetId,emoji,reactorKey,senderName,fromMe,time,IsRemoval}`, `MessageReaction{emoji,reactorKey,senderName,fromMe,time}`, `ReactionStore.Apply/DrainInto/Clear/ApplyToMessage`, `ReactionSummary.Build → (List<string>,int)`, `ReactionParser.FromRaw/ReactorKey`, `ChatManager.HandleReactionEvent/_reactions/OnMessageReactionsChanged`, `ReactionPillView.Render/HasReactions`, `MessageItemView.reactionPill/HandleReactionsChanged/RenderReactions/PositionReactionPill` — names used consistently across tasks. ✓
- **Known editor-tuning steps** (T8 `BubbleName` confirmation, T9 pixel offsets + optional clearance spacer) are explicitly flagged as in-editor work, not silent placeholders — the logic they depend on is fully specified and tested. ✓
