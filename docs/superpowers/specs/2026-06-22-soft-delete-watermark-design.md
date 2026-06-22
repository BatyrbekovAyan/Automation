# Persistent soft-delete watermark for chat deletion — design spec

Date: 2026-06-22
Status: approved for planning

## Goal

Make a deleted chat **stay deleted** across bot switches and app restarts, and **reappear
("revive") when it becomes active again** (a newer message arrives from the contact, another
device, or the WhatsApp app). Keep bots independent.

## Background — confirmed Wappi behavior

Wappi's `POST /api/sync/chat/delete` is a **soft delete**: the chat stays in
`chats/filter` with `isDeleted: true`. That flag is **sticky** — Wappi never clears it, even
after new messages. **However** (confirmed by testing): when a soft-deleted chat receives a new
message, Wappi **does** advance that chat's `last_message_id` / `last_timestamp` in
`chats/filter` while `isDeleted` stays `true`.

Today `ParseChatsJson` ignores `isDeleted` entirely (it adds/updates every dialog), so a deleted
chat reappears as soon as the in-memory guard clears (bot switch) or the app restarts. The
current in-memory `DeletedChatGuard` was a session-only stopgap and cannot express "revive on
activity."

## Design — persistent per-bot deletion watermark

Because the last-message timestamp advances under a sticky `isDeleted`, we drive everything off
that timestamp instead of trusting `isDeleted` for visibility.

### Persistence

A per-bot file `deleted_chats.json` in `GetCacheRoot()` (already namespaced by `CurrentBotId`,
which is what makes two bots independent). It stores a map `chatId → watermark`, where the
watermark is the chat's last-message Unix timestamp captured at the moment of deletion.

Serialized via a `JsonUtility`-friendly wrapper (parallel to `ChatHistoryCache`):

```csharp
[Serializable] class Entry   { public string id; public long ts; }
[Serializable] class Wrapper { public List<Entry> entries; }
```

### The decision rule (pure, testable)

For each chat in a sync, with `ts` = its parsed `last_timestamp` (Unix seconds), `isDeleted` =
Wappi's flag, and the per-bot watermark map:

- **Watermark `W` exists for the chat** → **hide if `ts <= W`**; **show if `ts > W`** (revived by
  newer activity). The watermark is **kept** (never cleared by a revival), so once `ts > W` the
  chat stays shown until a future delete bumps `W`.
- **No watermark, `isDeleted == true`** → **adopt**: record `W = ts`, hide. This is the real fix
  for "deleted directly in WhatsApp / on another device but still shown here."
- **No watermark, `isDeleted == false`** → show normally.

This is stable because `ts` is monotonic: once `ts > W` it stays shown; deleting again sets
`W = newTs` and it hides until the next message. We never clear `W` except by overwrite on a
newer delete (or on a delete rollback).

Extracted as a pure helper for unit testing:

```csharp
public static class DeletedChatRule
{
    // Returns true if the chat should be hidden. When the chat must be newly watermarked
    // (external-delete adoption), adoptWatermark is set to ts; otherwise it is -1.
    public static bool ShouldHide(bool hasWatermark, long watermark, long ts, bool isDeleted,
                                  out long adoptWatermark)
    {
        if (hasWatermark) { adoptWatermark = -1; return ts <= watermark; }
        if (isDeleted)    { adoptWatermark = ts; return true; }
        adoptWatermark = -1; return false;
    }
}
```

### Components

**New**
- `Assets/Scripts/Chat/DeletedChatRule.cs` — the pure decision function above.
- `Assets/Scripts/Chat/DeletedChatStore.cs` — `static Dictionary<string,long> Load(string cacheRoot)`
  and `static void Save(string cacheRoot, Dictionary<string,long> map)` for `deleted_chats.json`
  (null/empty/corrupt-safe, mirrors `ChatHistoryCache`).

**Modified**
- `Assets/Scripts/Chat/ChatDialog.cs` — add `public bool isDeleted;`.
- `Assets/Scripts/Main/ChatManager.cs` — hold `Dictionary<string,long> _deletedWatermarks` for the
  active bot; load it when the active bot's list loads; rewrite `ParseChatsJson` to use
  `DeletedChatRule` (hide / adopt / show), removing an on-screen row that becomes hidden (fires
  `OnChatRemoved`), and persisting adopted watermarks.
- `Assets/Scripts/Main/ChatManager.DeleteChat.cs` — on delete, set
  `_deletedWatermarks[chatId] = vm.LastMessageTime` and save; keep optimistic removal +
  `ChatHistoryCache.DeleteHistory` + the Wappi `chat/delete` call; on failure rollback, also remove
  the watermark and save.
- `Assets/Scripts/Main/ChatManager.BotState.cs` — load `_deletedWatermarks` for the new bot on
  `SetActiveBot` / the no-bots path (replacing the `_deletedChats.ClearAll()` calls).

**Removed (superseded by the watermark)**
- `Assets/Scripts/Chat/DeletedChatGuard.cs` and `DeletedChatGuardTests.cs`.
- The `ParseChatsJson` reconcile/suppress lines (`_deletedChats.ReconcileWithServer` /
  `ShouldSuppress`).
- The `chats.json` rewrite-on-delete inside `EvictChatCaches` (the watermark hides the chat on the
  next load, so rewriting the cache is redundant). `ChatHistoryCache.DeleteHistory` stays.
- `ChatListCacheEditor` becomes unused; remove it and its tests too.

The "drop chats absent from the response" safety added earlier **stays** (it handles chats
genuinely gone from `chats/filter`, a separate case from `isDeleted`).

## Delete flow (sequence)

```
Confirm delete →
  ChatManager.DeleteChat(chatId):
    1. vm = chatLookup[chatId]; watermark = vm.LastMessageTime
    2. _deletedWatermarks[chatId] = watermark; DeletedChatStore.Save(GetCacheRoot(), …)
    3. RemoveChatLocally(chatId) → OnChatRemoved → row collapses out
    4. ChatHistoryCache.DeleteHistory(GetCacheRoot(), chatId)
    5. POST /api/sync/chat/delete (soft-deletes server-side; drives isDeleted adoption on
       this contact's OTHER bots/devices)
         success → done (watermark already persisted)
         failure → RollbackDelete: remove _deletedWatermarks[chatId] + Save; re-add the row
```

## ParseChatsJson (per-chat, both initial cache load and background sync)

```
ts = parse(last_timestamp)
hide = DeletedChatRule.ShouldHide(_deletedWatermarks.TryGetValue(id, out w), w, ts,
                                  chat.isDeleted, out adopt)
if (adopt >= 0) { _deletedWatermarks[id] = adopt; dirty = true }
if (hide) {
    if (chatLookup has id) { remove from Chats + chatLookup; OnChatRemoved?.Invoke(id) }
    continue
}
… existing add / smart-merge update …
// after the loop: if (dirty) DeletedChatStore.Save(GetCacheRoot(), _deletedWatermarks)
```

The watermark check runs on `isInitialLoad` too, so deleted chats never flash in from the
`chats.json` cache on a cold launch.

## Edge cases

- **Pre-soft-delete race** (our delete done, Wappi not yet `isDeleted`): the chat returns with
  `isDeleted:false` but `ts == W` → `ts <= W` → still hidden. The watermark, not `isDeleted`,
  governs our own deletes.
- **Delete succeeds server-side but we miss the response** → rollback removes the watermark and
  re-shows; the next sync sees `isDeleted:true` with no watermark → adopts → hides again. Brief
  flicker, self-correcting.
- **Two bots, same contact** — separate Wappi profiles and separate `deleted_chats.json`, so a
  delete on bot A never hides the chat on bot B. Intended.
- **Watermark file growth** — one entry per ever-deleted chat, never auto-pruned (pruning would
  re-trigger adoption and re-hide). Bounded by chat count; tiny. No LRU for now (YAGNI).
- **`ts == 0`** (no/zero timestamp) — treated as the smallest value; a watermark of 0 hides until a
  real (`>0`) message arrives. Harmless.

## Testing

EditMode (`Assets/Tests/Editor/Chat/`):
- `DeletedChatRuleTests` — the pure function across: fresh delete (`ts == W` → hide),
  no-new-activity (`ts < W` → hide), revived (`ts > W` → show, no adopt), external delete
  (`!hasWatermark && isDeleted` → hide + adopt = ts), never-deleted (`!hasWatermark && !isDeleted`
  → show), `ts == 0`.
- `DeletedChatStoreTests` — round-trip save/load, missing file → empty map, corrupt file → empty
  map, null/empty cacheRoot safe.

Runtime behavior (manual / device): delete sticks across bot switch + restart; revives on a new
message; deleting in WhatsApp directly hides it here on the next sync; rollback on server failure.

## Out of scope

- A server-side "un-delete" (Wappi exposes none; we override visibility locally via the watermark).
- Pruning/capping the watermark file.
- Telegram (chat list is WhatsApp-only).
- Reviving via the incoming webhook/n8n path (the confirmed `chats/filter` timestamp signal makes
  it unnecessary).
