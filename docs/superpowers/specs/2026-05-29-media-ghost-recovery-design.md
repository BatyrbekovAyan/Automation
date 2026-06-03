# Media Ghost-Recovery — Cross-Session Reconciliation for Media Sends

**Date**: 2026-05-29
**Status**: Approved, awaiting implementation plan
**Predecessors**: `2026-05-29-attach-panel-media-send-design.md` (part "c" — Wappi media upload + persistence + outbox). This is the deferred follow-up flagged there.
**Scope**: new `Assets/Scripts/Chat/MediaGhostMatch.cs` (pure static), additive edits to the ghost-recovery block in `Assets/Scripts/Main/ChatManager.cs` (~452–516 inside `SyncLatestMessages`), new EditMode tests under `Assets/Tests/Editor/Chat/`. No other files change.

## 1. Problem

`SyncLatestMessages` in `ChatManager.cs` has a cross-session "ghost-recovery" block (~445–519) that reconciles a persisted optimistic send-bubble against the server's echoed-back real message after an app restart. It is gated at ~452 on:

```csharp
if (norm.fromMe && norm.messageType == MessageType.Chat)
```

So it only fires for **text**. Media outbox entries (`kind == OutboxKind.Media`, `attachmentKind` Photo/GalleryImage/GalleryVideo/Document) are never reconciled across a session boundary.

**Consequence (the bug):** if the app is killed in the narrow window *after* `StageLocalMedia` persists the outbox entry + optimistic bubble but *before* `PostMediaMessageRoutine` processes the Wappi ack, then on the next launch:

- the server's real media message renders as a fresh bubble (`newMessages.Add(CreateViewModel(norm))` at ~518), **and**
- the old staged bubble loads from `ChatHistoryCache` and is promoted to `Failed` by the kind-agnostic pending→failed pass in `OpenChatRoutine` (~645–655),
- → the user sees **two bubbles** for the same media message (one Failed ghost, one real),
- the outbox entry is never removed (the `Outbox.RemoveAt` at ~498 is inside the text-only branch),
- and the `tempId` leaks in `seenMessageIds` (the `seenMessageIds.Remove(ghostTempId)` at ~499 never runs).

The in-session happy path is unaffected — `PostMediaMessageRoutine`'s reconcile handles that correctly. This is purely the crash-during-upload, next-session edge case.

## 2. Goal

Extend the cross-session ghost-recovery so a media send (Image / Video / Document) reconciles exactly like text: the persisted optimistic bubble is swapped in place to the server's real `message_id`, adopts the server delivery status + timestamp, the outbox entry is removed, the `seenMessageIds` leak is cleared, and the duplicate append is suppressed. The behavior must mirror the proven text branch — that path is the template.

## 3. Decisions locked from brainstorming

- **Match key: attachment kind + timestamp only (Q1).** Match on `attachmentKind → MessageType` (Photo/GalleryImage→Image, GalleryVideo→Video, Document→Document) within the existing ±120s window, smallest delta wins. **Caption is ignored.** Rationale: media captions are frequently empty; a reliable caption compare would have to re-extract the *raw* server caption (`raw.caption ?? raw.body["caption"]`) to dodge the emoji→`<sprite>` rewrite `Normalize` applies — a fragile surface that only helps in the rare "two same-kind media within 120s during the exact crash window" case. YAGNI.
- **Code structure: Approach A (extract the tail + a pure mapping helper).** A new pure static `MediaGhostMatch` holds the kind→type mapping (unit-testable); the shared swap/remove/fire tail is extracted into a private `ReconcileGhostSend` used by both branches; the two byte-identical window+best-delta loops are unified into a tiny private `BestGhostMatch(unresolved, serverTime, predicate)` taking a distinct predicate per branch.
- **No change to text matching behavior.** The text predicate stays `e.text == rawBody` (compared against the raw body, not `norm.text`). We are not adding a `kind == Text` guard to the text path — out of scope, and it would alter proven behavior.
- **No change to the pending→failed pass.** It is already kind-agnostic (promotes any non-incoming cached bubble whose `messageId` is still in the outbox). The sync-side reconcile corrects the Failed status via the in-place swap + `OnMessageStatusChanged` fire.

## 4. Root cause (line-by-line, approximate)

| Line | Current behavior | Why it breaks media |
|---|---|---|
| ~452 | `if (norm.fromMe && norm.messageType == MessageType.Chat)` | Media (`Image`/`Video`/`Document`) never enters the recovery block. |
| ~466 | `if (unresolved[i].text != rawBody) continue;` | Keys on exact body text; media captions are usually empty and wouldn't disambiguate photo-vs-video anyway. |
| ~498–499 | `Outbox.RemoveAt(...)`, `seenMessageIds.Remove(...)` | Inside the text-only branch → media outbox entry + seen-id both leak. |
| ~516 | `if (isGhostRecovery) continue;` | Never set for media → the server message is appended as a second bubble at ~518. |

**Why a sync-side reconcile is sufficient (verified):** `OpenChatRoutine` runs the pending→failed pass on `cachedMessages` (~645–655), sets `_activeChatCache = cachedMessages` (~659), then starts `SyncLatestMessages(chatId, cachedMessages)` (~676) **with the same list reference**. So when sync's reconcile mutates `cachedList[j].messageId = raw.id` + `deliveryStatus = norm.deliveryStatus`, it overwrites the very bubble the pass just marked `Failed`; `Outbox.RemoveAt` clears the entry; and `if (isGhostRecovery) continue;` suppresses the duplicate. No change to `OpenChatRoutine` is required.

## 5. Scope

**In scope**
- New `Assets/Scripts/Chat/MediaGhostMatch.cs` — pure static kind→type mapping + `IsKindMatch`.
- Restructure the gated ghost-recovery block in `SyncLatestMessages` (~452–516) into a `norm.fromMe` block with a text matcher and a media matcher, both feeding one shared reconcile tail.
- Extract `ReconcileGhostSend(...)` (private instance method on `ChatManager`) and `BestGhostMatch(...)` (private static loop helper).
- New EditMode tests for `MediaGhostMatch` under `Assets/Tests/Editor/Chat/`.

**Out of scope**
- The pending→failed pass, `OpenChatRoutine`, `StageLocalMedia`, `PostMediaMessageRoutine`, `RetryRoutine`, `OutboxStore` — all unchanged.
- Caption-based matching.
- Audio/voice (no `AttachmentKind.Audio`).
- Any change to text-send reconciliation semantics.

## 6. Design (Approach A)

### 6.1 `MediaGhostMatch` (new pure static, Assembly-CSharp, mirrors `AttachmentDisplayFormat.cs`)

```csharp
public static class MediaGhostMatch
{
    // Staged AttachmentKind → the MessageType the server echoes back.
    public static MessageType ToMessageType(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Photo or AttachmentKind.GalleryImage => MessageType.Image,
        AttachmentKind.GalleryVideo                         => MessageType.Video,
        AttachmentKind.Document                             => MessageType.Document,
        _                                                   => MessageType.Unknown,
    };

    // True iff this unresolved entry is a Media send whose kind matches the server
    // message type. Does NOT check timestamp — caller owns the ±window + best-delta.
    public static bool IsKindMatch(OutboxStore.OutboxEntry entry, MessageType serverType) =>
        entry != null
        && entry.kind == (int)OutboxKind.Media
        && ToMessageType((AttachmentKind)entry.attachmentKind) == serverType;
}
```

`ToMessageType` returns `Unknown` for any out-of-range ordinal, so `IsKindMatch` is false for non-media or corrupt entries. Callers only ever pass `serverType ∈ {Image, Video, Document}` (the block is gated on those; `Unknown` is already skipped at ~448).

### 6.2 `ReconcileGhostSend` (new private instance method on `ChatManager`)

The shared swap/remove/fire tail, lifted verbatim from the existing text branch so semantics are identical:

```csharp
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
```

Preserves the existing quirk: `RemoveAt` + `seenMessageIds.Remove` run even when the cached bubble was already evicted (>100-msg cap), so a stale outbox entry is still cleaned and the server message renders fresh. Returns `true` only when a bubble was found and mutated → caller maps that to both `isGhostRecovery` (suppress duplicate) and `hasStatusUpdates`.

### 6.3 `BestGhostMatch` (new private static loop helper)

```csharp
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
        if (delta < bestDelta) { bestDelta = delta; bestIndex = i; }
    }
    return bestIndex >= 0 ? unresolved[bestIndex].tempId : null;
}
```

Window (±120s) and best-delta selection are identical to the current text loop. The per-call closure allocation is acceptable: `SyncLatestMessages` is a once-per-sync coroutine, not an `Update`-path hot loop (`unity-general.md`'s LINQ/allocation rule targets `Update`/`FixedUpdate`).

### 6.4 Restructured gated block (~452–516)

```csharp
if (norm.fromMe)
{
    string ghostTempId = null;
    var unresolved = Outbox.GetFor(chatId);

    if (norm.messageType == MessageType.Chat)
    {
        // Compare against the RAW server body, not norm.text — Normalize() rewrites
        // emoji into <sprite> tags; the outbox stores the raw user input.
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

if (isGhostRecovery) continue;   // unchanged — suppresses the duplicate append
newMessages.Add(CreateViewModel(norm));
```

## 7. What does NOT change

- `if (isGhostRecovery) continue;` (~516) and the `else if (hasStatusUpdates)` save path (~606–612) are untouched; they already do the right thing once `isGhostRecovery`/`hasStatusUpdates` are set for media.
- The pending→failed pass (~645–655), `OpenChatRoutine`, `StageLocalMedia`, `PostMediaMessageRoutine`, `RetryRoutine`, `OutboxStore`.
- Text-send reconciliation semantics (predicate, window, tail) are byte-for-byte equivalent after the refactor.

## 8. Testing

**EditMode unit tests** — `Assets/Tests/Editor/Chat/MediaGhostMatchTests.cs` (mirrors `AttachmentDisplayFormatTests` `[TestCase]` style; visible to `Assembly-CSharp-Editor`, which references `Assembly-CSharp` — no asmdefs in the project):

- `ToMessageType`: Photo→Image, GalleryImage→Image, GalleryVideo→Video, Document→Document.
- `IsKindMatch` true: Media+Photo vs Image, Media+GalleryImage vs Image, Media+GalleryVideo vs Video, Media+Document vs Document.
- `IsKindMatch` false: cross-kind (Media+GalleryVideo vs Image; Media+Photo vs Video; Media+Document vs Image), Text-kind entry (`kind=0`) vs Image, `null` entry.

`BestGhostMatch`'s window/best-delta and `ReconcileGhostSend`'s mutation are validated by the human in the Editor (they touch instance state / `Outbox` disk I/O and aren't worth a harness). The novel, pure logic — the kind mapping — is the unit-tested surface, matching the task's "extract the match predicate" guidance.

**Editor verification (human-run):**
1. Test Runner → EditMode → `MediaGhostMatchTests` green; full suite still green.
2. Console clean on compile (the project's `validate-cs.sh` hook also runs on each Edit/Write).
3. Manual repro of the crash window if feasible: stage a media send, force-kill before ack, relaunch, reopen chat → exactly one bubble at Sent, no Failed ghost, outbox file for the chat cleared.

## 9. Constraints

- Coroutines only, no async/await in MonoBehaviours. No namespaces / no asmdefs (Assembly-CSharp). Newtonsoft for JSON elsewhere in the file (this change adds no new JSON). Follows `.claude/rules/` (networking.md, unity-general.md, csharp-quality.md).
- `MediaGhostMatch` members are `public` (not `internal`) so the editor test assembly can see them, matching `AttachmentDisplayFormat`.

## 10. Risks

- **Refactoring the proven text loop into `BestGhostMatch`** is the only touch to working code. Mitigation: the loop is byte-identical (same window, same best-delta tie-break); the text predicate is unchanged; the extracted tail is lifted verbatim. Editor full-suite + manual text-send smoke test covers regression.
- **Two same-kind media within 120s during the exact crash window** would match the smaller-delta entry (could be the wrong one if both are unresolved). Accepted: vanishingly unlikely given the window is the gap between `StageLocalMedia` persist and the Wappi ack on a single send; both reconcile to real server messages on subsequent syncs regardless.
