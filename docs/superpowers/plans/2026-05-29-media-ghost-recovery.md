# Media Ghost-Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the cross-session ghost-recovery in `SyncLatestMessages` so a media send (Image/Video/Document) reconciles its persisted optimistic bubble against the server's echoed-back real message after an app restart — exactly as text already does — eliminating the duplicate-bubble + leaked-outbox bug on crash-during-upload.

**Architecture:** Add one pure, unit-tested helper (`MediaGhostMatch`) for the `AttachmentKind → MessageType` mapping. In `ChatManager.cs`, extract the existing text branch's swap/remove/fire tail into a private `ReconcileGhostSend`, collapse the byte-identical window+best-delta loop into a private `BestGhostMatch(unresolved, time, predicate)`, then restructure the gated block into a `norm.fromMe` block with a text matcher and a media matcher both feeding that shared tail. Match key is attachment kind + ±120s only (no caption).

**Tech Stack:** Unity 6 (6000.3.9f1) C#, URP. Coroutines (no async/await). EditMode tests via NUnit in the predefined `Assembly-CSharp-Editor` (no asmdef in this project). No new JSON, no new network calls.

**Source spec:** `docs/superpowers/specs/2026-05-29-media-ghost-recovery-design.md`

---

## Conventions for this plan

**Project has no `.asmdef` files.** All runtime scripts compile into `Assembly-CSharp`; everything under an `Editor/` folder compiles into `Assembly-CSharp-Editor`, which auto-references `Assembly-CSharp` + NUnit. New EditMode tests go in `Assets/Tests/Editor/Chat/` with `using NUnit.Framework;` — **do not create or edit any asmdef.** Helper types under test must be `public` so the editor test assembly can see them (mirrors `AttachmentDisplayFormat`).

**Running EditMode tests** (every "run the test" step below) — the human runs these in their already-open Editor:
- Unity Editor → `Window → General → Test Runner` → **EditMode** tab → `Run All` (or right-click the class → Run).
- CLI alternative (only if the project is not open elsewhere): `Unity -batchmode -runTests -projectPath . -testPlatform EditMode -testResults /tmp/results.xml -quit`, then inspect `/tmp/results.xml`.

**"Verify it fails" in Unity:** a test referencing a not-yet-created type makes the whole assembly fail to compile (red Console wall, Test Runner won't run). To get a genuine **red assertion** instead, this plan uses the **stub-then-real** cycle: create the helper with deliberately-wrong bodies first so the assembly compiles and the tests fail on `Assert`, then fill in the real logic.

**Unity recompiles on file save.** After each Create/Edit, the human returns to the Editor, lets it recompile, and confirms the Console has **zero** errors before moving on. The repo's `validate-cs.sh` hook also runs on each Edit/Write.

**Enums (global, no namespaces):** `MessageType { Chat, Image, Video, Audio, Voice, Sticker, Document, Unknown }`. `AttachmentKind { Photo=0, GalleryImage=1, GalleryVideo=2, Document=3 }`. `OutboxKind { Text=0, Media=1 }`.

**Commits:** conventional-commit style (`feat(chat):`, `fix(chat):`). **Each commit is made only after the human confirms** (per-task consent). For new files, stage the `.cs` **and** its Unity-generated `.meta` sibling. Stage only the files the task names.

---

## File structure

**New (runtime):**
- `Assets/Scripts/Chat/MediaGhostMatch.cs` — `public static class`, no Unity dependency. `AttachmentKind → MessageType` mapping + `IsKindMatch(entry, serverType)`. The only unit-tested surface.

**New (tests):**
- `Assets/Tests/Editor/Chat/MediaGhostMatchTests.cs` — NUnit `[TestCase]` coverage for both methods.

**Modified:**
- `Assets/Scripts/Main/ChatManager.cs` — add `ReconcileGhostSend` + `BestGhostMatch` private helpers below `SyncLatestMessages`; restructure the gated ghost-recovery block (~452–512) to add the media matcher and route both branches through the shared tail.

**Untouched (do not edit):** `OpenChatRoutine` and its pending→failed pass, `OutboxStore.cs`, `ChatManager.MediaSend.cs`, `ChatManager.Outbox.cs`, `MessageItemView.cs`, `MediaCacheManager.cs`, all `Assets/Editor/*Builder.cs`. Text-send reconciliation semantics must remain byte-for-byte equivalent after the refactor.

---

## Task 1: `MediaGhostMatch` (pure kind→type matcher)

**Files:**
- Create: `Assets/Scripts/Chat/MediaGhostMatch.cs`
- Test: `Assets/Tests/Editor/Chat/MediaGhostMatchTests.cs`

- [ ] **Step 1: Create the stub so the assembly compiles**

Create `Assets/Scripts/Chat/MediaGhostMatch.cs` with deliberately-wrong bodies (real logic lands in Step 5):

```csharp
public static class MediaGhostMatch
{
    // STUB — replaced in Step 5. Wrong on purpose so Step 4 sees a red assertion.
    public static MessageType ToMessageType(AttachmentKind kind) => MessageType.Unknown;

    // STUB — replaced in Step 5.
    public static bool IsKindMatch(OutboxStore.OutboxEntry entry, MessageType serverType) => false;
}
```

- [ ] **Step 2: Let Unity compile the stub**

Return to the Editor, let it recompile. Expected: Console has **zero** errors (the stub is valid C#).

- [ ] **Step 3: Write the failing tests**

Create `Assets/Tests/Editor/Chat/MediaGhostMatchTests.cs`:

```csharp
using NUnit.Framework;

public class MediaGhostMatchTests
{
    // ── ToMessageType ─────────────────────────────────────────────

    [TestCase(AttachmentKind.Photo,        MessageType.Image)]
    [TestCase(AttachmentKind.GalleryImage, MessageType.Image)]
    [TestCase(AttachmentKind.GalleryVideo, MessageType.Video)]
    [TestCase(AttachmentKind.Document,     MessageType.Document)]
    public void ToMessageType_Returns_Expected(AttachmentKind kind, MessageType expected)
    {
        Assert.AreEqual(expected, MediaGhostMatch.ToMessageType(kind));
    }

    // ── IsKindMatch (true: media entry whose kind maps to the server type) ──

    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.Photo,        MessageType.Image,    true)]
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.GalleryImage, MessageType.Image,    true)]
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.GalleryVideo, MessageType.Video,    true)]
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.Document,     MessageType.Document, true)]
    // false: cross-kind mismatches
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.GalleryVideo, MessageType.Image,    false)]
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.Photo,        MessageType.Video,    false)]
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.Document,     MessageType.Image,    false)]
    // false: a Text-kind entry never matches a media server message
    [TestCase((int)OutboxKind.Text,  (int)AttachmentKind.Photo,        MessageType.Image,    false)]
    public void IsKindMatch_Returns_Expected(int kind, int attachmentKind, MessageType serverType, bool expected)
    {
        var entry = new OutboxStore.OutboxEntry { kind = kind, attachmentKind = attachmentKind };
        Assert.AreEqual(expected, MediaGhostMatch.IsKindMatch(entry, serverType));
    }

    [Test]
    public void IsKindMatch_NullEntry_ReturnsFalse()
    {
        Assert.IsFalse(MediaGhostMatch.IsKindMatch(null, MessageType.Image));
    }
}
```

- [ ] **Step 4: Run the tests and verify they FAIL**

Run the EditMode tests (Test Runner → EditMode → run `MediaGhostMatchTests`).
Expected: **RED**. The four `ToMessageType` cases fail (`Expected: Image, But was: Unknown`, etc.) and the four `true`-expecting `IsKindMatch` cases fail (`Expected: True, But was: False`). The `false`-expecting cases and the null case pass against the stub — that's fine; the genuine red assertions confirm the tests exercise real behavior.

- [ ] **Step 5: Replace the stub with the real implementation**

Edit `Assets/Scripts/Chat/MediaGhostMatch.cs` to its final form:

```csharp
public static class MediaGhostMatch
{
    // Staged AttachmentKind → the MessageType the server echoes back for that send.
    public static MessageType ToMessageType(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Photo or AttachmentKind.GalleryImage => MessageType.Image,
        AttachmentKind.GalleryVideo                         => MessageType.Video,
        AttachmentKind.Document                             => MessageType.Document,
        _                                                   => MessageType.Unknown,
    };

    // True iff this unresolved entry is a Media send whose attachment kind corresponds
    // to the given server message type. Does NOT check timestamp — the caller owns the
    // ±window + best-delta selection (see ChatManager.BestGhostMatch).
    public static bool IsKindMatch(OutboxStore.OutboxEntry entry, MessageType serverType) =>
        entry != null
        && entry.kind == (int)OutboxKind.Media
        && ToMessageType((AttachmentKind)entry.attachmentKind) == serverType;
}
```

- [ ] **Step 6: Run the tests and verify they PASS**

Run the EditMode tests again. Expected: **GREEN** — all `MediaGhostMatchTests` pass. Confirm Console has zero errors.

- [ ] **Step 7: Commit (after human confirms)**

New file → stage the `.cs` and its generated `.meta`:

```bash
git add Assets/Scripts/Chat/MediaGhostMatch.cs Assets/Scripts/Chat/MediaGhostMatch.cs.meta \
        Assets/Tests/Editor/Chat/MediaGhostMatchTests.cs Assets/Tests/Editor/Chat/MediaGhostMatchTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(chat): add MediaGhostMatch kind matcher for media ghost-recovery

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Wire media into the cross-session ghost-recovery

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` — add two private helpers below `SyncLatestMessages`; restructure the gated block at ~452–512.

No new tests: this is integration logic over instance state + `Outbox` disk I/O, validated in the Editor (spec §8). The novel pure logic was covered in Task 1.

- [ ] **Step 1: Add the `ReconcileGhostSend` + `BestGhostMatch` helpers**

In `Assets/Scripts/Main/ChatManager.cs`, insert the two methods **immediately before the `OpenChatRoutine` doc-comment** (i.e., right after the closing brace of `SyncLatestMessages`). Use this Edit:

Find this anchor:

```csharp
    /// <summary>
    /// Phase A (Prep) of chat-open. Runs cache load + sort + first-screen split synchronously
```

Replace it with:

```csharp
    /// <summary>
    /// Shared swap/remove/fire tail for cross-session ghost-recovery (text + media).
    /// Swaps the cached optimistic bubble's tempId → the server's real id, adopts the
    /// server delivery status + timestamp, removes the resolved outbox entry, clears the
    /// tempId from seenMessageIds, and fires OnMessageStatusChanged so the rendered bubble
    /// re-renders its tick in place. Returns true iff a cached bubble was found and mutated
    /// (caller then skips appending a duplicate to newMessages). RemoveAt + the seenMessageIds
    /// clear run even when no cached bubble is found, so a stale outbox entry for an
    /// already-evicted bubble (>100-msg cap) is still cleaned up.
    /// </summary>
    private bool ReconcileGhostSend(string ghostTempId, RawMessage raw, NormalizedMessage norm,
                                    List<MessageViewModel> cachedList, string chatId)
    {
        bool found = false;
        for (int j = 0; j < cachedList.Count; j++)
        {
            if (cachedList[j].messageId == ghostTempId)
            {
                cachedList[j].messageId      = raw.id;
                cachedList[j].deliveryStatus = norm.deliveryStatus;
                cachedList[j].timestamp      = norm.time;
                found = true;
                break;
            }
        }

        Outbox.RemoveAt(GetCacheRoot(), chatId, ghostTempId);
        seenMessageIds.Remove(ghostTempId);

        if (found) OnMessageStatusChanged?.Invoke(ghostTempId, raw.id, norm.deliveryStatus);
        return found;
    }

    /// <summary>
    /// Finds the unresolved outbox entry that best matches a server message: the smallest
    /// |entry.timestamp - serverTime| within ±120s among entries the predicate accepts.
    /// Returns the winning tempId, or null if none match. Shared by the text matcher
    /// (predicate = raw-body equality) and the media matcher (predicate = MediaGhostMatch.IsKindMatch).
    /// </summary>
    private static string BestGhostMatch(IReadOnlyList<OutboxStore.OutboxEntry> unresolved,
                                         long serverTime, Func<OutboxStore.OutboxEntry, bool> predicate)
    {
        int bestIndex = -1;
        long bestDelta = long.MaxValue;
        for (int i = 0; i < unresolved.Count; i++)
        {
            if (!predicate(unresolved[i])) continue;
            long delta = Math.Abs(unresolved[i].timestamp - serverTime);
            if (delta > 120) continue;
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestIndex = i;
            }
        }
        return bestIndex >= 0 ? unresolved[bestIndex].tempId : null;
    }

    /// <summary>
    /// Phase A (Prep) of chat-open. Runs cache load + sort + first-screen split synchronously
```

(`System.Func`, `System.Math`, and `System.Collections.Generic.IReadOnlyList` are already available — the file uses `Math.Abs`, `List<>`, and `Action` events throughout. No new `using` needed.)

- [ ] **Step 2: Restructure the gated ghost-recovery block**

In the same file, inside `SyncLatestMessages` (~452–512), replace the text-only gated block. The line `bool isGhostRecovery = false;` above it and the `if (isGhostRecovery) continue;` / `newMessages.Add(CreateViewModel(norm));` below it stay exactly as-is.

Find (replace this exact block):

```csharp
                        if (norm.fromMe && norm.messageType == MessageType.Chat)
                        {
                            // Compare against the RAW server body, not norm.text. Normalize()
                            // rewrites Unicode emoji into TMP <sprite name="..."> tags via
                            // UnicodeEmojiConverter — the outbox entry's text is the raw user
                            // input and only matches the raw body, not the converted form.
                            string rawBody = raw.body?.ToString();
                            if (!string.IsNullOrEmpty(rawBody))
                            {
                                var unresolved = Outbox.GetFor(chatId);
                                int bestMatchIndex = -1;
                                long bestMatchDelta = long.MaxValue;
                                for (int i = 0; i < unresolved.Count; i++)
                                {
                                    if (unresolved[i].text != rawBody) continue;
                                    long delta = Math.Abs(unresolved[i].timestamp - norm.time);
                                    if (delta > 120) continue;
                                    if (delta < bestMatchDelta)
                                    {
                                        bestMatchDelta = delta;
                                        bestMatchIndex = i;
                                    }
                                }

                                if (bestMatchIndex >= 0)
                                {
                                    string ghostTempId = unresolved[bestMatchIndex].tempId;

                                    // Mutate the cached VM in place: swap to the real id,
                                    // adopt the server's status + timestamp. The rendered
                                    // bubble subscribes to OnMessageStatusChanged and will
                                    // match its currentVm.messageId against ghostTempId
                                    // before this loop's event fire, swap to raw.id, and
                                    // re-render the tick.
                                    for (int j = 0; j < cachedList.Count; j++)
                                    {
                                        if (cachedList[j].messageId == ghostTempId)
                                        {
                                            cachedList[j].messageId = raw.id;
                                            cachedList[j].deliveryStatus = norm.deliveryStatus;
                                            cachedList[j].timestamp = norm.time;
                                            isGhostRecovery = true;
                                            break;
                                        }
                                    }

                                    Outbox.RemoveAt(GetCacheRoot(), chatId, ghostTempId);
                                    seenMessageIds.Remove(ghostTempId);

                                    if (isGhostRecovery)
                                    {
                                        // Fire the id swap + status change to the rendered
                                        // bubble. HandleStatusChanged swaps messageId →
                                        // raw.id and calls SetDeliveryStatus(norm.deliveryStatus),
                                        // which re-renders the tick. No duplicate bubble.
                                        OnMessageStatusChanged?.Invoke(ghostTempId, raw.id, norm.deliveryStatus);
                                        hasStatusUpdates = true;
                                    }
                                }
                            }
                        }
```

Replace with:

```csharp
                        if (norm.fromMe)
                        {
                            // Cross-session ghost-recovery: a previous-session send reached
                            // Wappi but the client never saw the ack, so the outbox still holds
                            // the tempId while the server now echoes the same message under its
                            // real id. Match the unresolved outbox entry, then reconcile the
                            // cached bubble in place. Text keys on the raw body; media keys on
                            // attachment kind + timestamp (captions are frequently empty and
                            // can't disambiguate a photo from a video sent seconds apart).
                            string ghostTempId = null;
                            var unresolved = Outbox.GetFor(chatId);

                            if (norm.messageType == MessageType.Chat)
                            {
                                // Compare against the RAW server body, not norm.text. Normalize()
                                // rewrites Unicode emoji into TMP <sprite name="..."> tags via
                                // UnicodeEmojiConverter — the outbox entry's text is the raw user
                                // input and only matches the raw body, not the converted form.
                                string rawBody = raw.body?.ToString();
                                if (!string.IsNullOrEmpty(rawBody))
                                    ghostTempId = BestGhostMatch(unresolved, norm.time, e => e.text == rawBody);
                            }
                            else if (norm.messageType == MessageType.Image ||
                                     norm.messageType == MessageType.Video ||
                                     norm.messageType == MessageType.Document)
                            {
                                ghostTempId = BestGhostMatch(unresolved, norm.time,
                                                             e => MediaGhostMatch.IsKindMatch(e, norm.messageType));
                            }

                            if (!string.IsNullOrEmpty(ghostTempId))
                            {
                                isGhostRecovery = ReconcileGhostSend(ghostTempId, raw, norm, cachedList, chatId);
                                if (isGhostRecovery) hasStatusUpdates = true;
                            }
                        }
```

Behavior notes (for review): the text predicate is unchanged (`e.text == rawBody`); the window (±120s) and best-delta tie-break are unchanged (now inside `BestGhostMatch`); `ReconcileGhostSend` does the same swap/RemoveAt/seen-clear/fire as before. The only intentional difference is that `Outbox.GetFor(chatId)` is now called once per `fromMe` message (hoisted above the type check) instead of only for text-with-body — it's a cached, side-effect-free read, so this is safe and slightly more uniform.

- [ ] **Step 3: Let Unity compile**

Return to the Editor, let it recompile. Expected: Console has **zero** errors (the block now references `BestGhostMatch`, `ReconcileGhostSend`, and `MediaGhostMatch.IsKindMatch`, all defined).

- [ ] **Step 4: Run the full EditMode suite and verify GREEN**

Run the EditMode tests (Test Runner → EditMode → `Run All`).
Expected: **all green** — `MediaGhostMatchTests` plus the pre-existing `OutboxStoreTests`, `OutboxEntryMediaCompatTests`, `AttachmentDisplayFormatTests`, and any others. The refactor must not regress the text path.

- [ ] **Step 5: Manual crash-window smoke test (recommended, if feasible)**

This is the bug the change fixes; verify in Play Mode / on device if the force-kill timing is reproducible:
1. Open a WhatsApp chat; send a photo (or video/document) and force-kill the app *before* the send acks (Pending bubble still showing). On device: swipe-kill; in Editor: stop Play Mode mid-upload.
2. Relaunch and reopen the same chat.
3. Expected: **exactly one** bubble for that media, resolved to **Sent** (single grey/blue tick) — no second "fresh" bubble, no red Failed ghost.
4. Confirm the chat's `outbox_*.json` under the bot's cache root no longer contains the staged entry.

If the timing can't be reproduced by hand, note that and rely on the design parity with the proven text path + the Task 1 unit coverage.

- [ ] **Step 6: Commit (after human confirms)**

Existing file (no new `.meta`):

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "$(cat <<'EOF'
fix(chat): reconcile media sends in cross-session ghost-recovery

Media outbox entries (kind == Media) were only reconciled in-session by
PostMediaMessageRoutine; a crash between StageLocalMedia and the Wappi ack
left a duplicate bubble + leaked outbox entry on next launch. SyncLatestMessages
now matches media by attachment kind + ±120s timestamp and runs the same
in-place swap/remove/fire reconcile as text, via shared ReconcileGhostSend +
BestGhostMatch helpers.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Self-review

**Spec coverage:**
- Spec §6.1 `MediaGhostMatch` → Task 1 (stub → tests → real). ✓
- Spec §6.2 `ReconcileGhostSend` → Task 2 Step 1. ✓
- Spec §6.3 `BestGhostMatch` → Task 2 Step 1. ✓
- Spec §6.4 restructured block (text + media matchers → shared tail) → Task 2 Step 2. ✓
- Spec §3 decisions: kind+timestamp only (no caption) → media predicate is `MediaGhostMatch.IsKindMatch`, no caption compare. ✓ Text behavior unchanged → predicate `e.text == rawBody` preserved, no `kind==Text` guard added. ✓
- Spec §7 untouched surfaces → "Untouched" list + no edits to `OpenChatRoutine`/`OutboxStore`/`MediaSend`. ✓
- Spec §8 testing: unit-test the pure mapping (Task 1), Editor-verify the integration (Task 2 Steps 4–5). ✓

**Placeholder scan:** no TBD/TODO; every code step shows complete code; every test step shows the assertion; commands are exact. ✓

**Type consistency:** `MediaGhostMatch.ToMessageType(AttachmentKind)` and `IsKindMatch(OutboxStore.OutboxEntry, MessageType)` are referenced identically in Task 1 (def + tests) and Task 2 (`e => MediaGhostMatch.IsKindMatch(e, norm.messageType)`). `BestGhostMatch(IReadOnlyList<OutboxStore.OutboxEntry>, long, Func<…,bool>)` and `ReconcileGhostSend(string, RawMessage, NormalizedMessage, List<MessageViewModel>, string)` signatures match their call sites in the restructured block. ✓
