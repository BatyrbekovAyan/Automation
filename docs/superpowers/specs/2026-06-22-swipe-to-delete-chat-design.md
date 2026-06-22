# Swipe-to-delete chat — design spec

Date: 2026-06-22
Status: approved for planning

## Goal

Add a WhatsApp-style swipe-left gesture to each row in the chats list. Swiping a
row left reveals a red **Delete** button; tapping it (after a confirm dialog)
**permanently deletes the conversation on the WhatsApp server** via Wappi and
removes it locally. Swiping back right re-hides the button.

## User-facing behavior

1. User drags a chat row left. The row content slides left, revealing a red
   Delete panel pinned to the right edge.
2. Release past ~50% of the reveal width → the row snaps open and stays open.
   Release before the threshold, or swipe right → it snaps closed.
3. Opening another row (or scrolling) closes any currently-open row.
4. While a row is open or mid-drag, tapping the row does **not** open the chat.
5. Tapping **Delete** opens a confirm dialog ("Delete this chat? This cannot be
   undone."). Cancel closes it; Delete commits.
6. On commit (optimistic): the row collapses out of the list immediately and a
   `POST /api/sync/chat/delete` is sent. On server success nothing more happens.
   On server failure the row slides back into the list and a brief error toast
   appears.
7. There is **no Undo** — the server delete is irreversible
   (Wappi: *"Метод позволяет безвозвратно удалить чат"*); the confirm dialog is
   the safety gate.

## Scope notes

- The chats list is **WhatsApp-only** (`ChatManager` reads from the WhatsApp
  Wappi base and resolves `profile_id` from `bot.whatsappProfileId`; there are no
  `tapi`/Telegram references). Only the WhatsApp `/api/sync/chat/delete` endpoint
  is needed — no Telegram equivalent.
- Failure handling is **optimistic + rollback** (snappier, WhatsApp-like).

## The Wappi API call

Confirmed from Wappi's API docs (`/api-documentation` → "Работа с чатами"):

```
POST https://wappi.pro/api/sync/chat/delete?profile_id={activeProfileId}
Authorization: {Manager.wappiAuthToken}
Content-Type: application/json

{ "recipient": "<chatId with @c.us stripped>" }

→ 200 OK: { "status": "done", "timestamp": ..., "time": ..., "uuid": ... }
```

- `recipient` derivation mirrors `PostTextMessageRoutine`
  ([ChatManager.cs:1835](Assets/Scripts/Main/ChatManager.cs)):
  `chatId.EndsWith("@c.us") ? chatId.Replace("@c.us","") : chatId`. Group chats
  (`@g.us`) pass the full id, exactly as send already does.
- Success = `status == "done"` (same convention as `message/send`).
- Request DTO: new `WappiDeleteChatRequest { recipient }`. Response DTO: new
  `WappiDeleteChatResponse { status }` (other fields ignored).
- Follows the project POST pattern: `UnityWebRequest(url,"POST")` +
  `UploadHandlerRaw` + `DownloadHandlerBuffer` + `Authorization` header +
  `timeout = 30` + `request.result` check before parse.

## Components

### New

- **`Assets/Scripts/Chat/SwipeToDelete.cs`** — the row gesture. Modeled on
  [SwipeToReply.cs](Assets/Scripts/Chat/SwipeToReply.cs). Implements
  `IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler,
  IEndDragHandler`. Lives on the row's `SwipeContent` and translates it on the X
  axis.
- **`Assets/Scripts/Main/ChatManager.DeleteChat.cs`** — new partial class. Public
  entry `DeleteChat(string chatId)`; private `DeleteChatRoutine` coroutine that
  calls the endpoint; local-removal + cache-eviction helpers; the resurrection
  guard set.

### Modified

- **`Assets/Scripts/Main/ChatManager.cs`** — add `OnChatRemoved` event,
  `RemoveChatLocally`, and the resurrection-guard check inside `ParseChatsJson`.
- **`Assets/Scripts/Chat/ChatHistoryCache.cs`** — add
  `DeleteHistory(baseDir, chatId)`.
- **`Assets/Scripts/UI/ChatListView.cs`** — subscribe to `OnChatRemoved`; collapse
  + destroy the row and drop it from `itemsByChatId`.
- **`Assets/Scripts/UI/ChatItemView.cs`** — expose hooks the gesture needs
  (close-on-rebind/recycle; suppress the row tap while open). Reuse the existing
  serialized refs.
- **`Assets/Prefabs/ChatItem.prefab`** — restructure for the reveal layer
  (below).

## Delete flow (sequence)

```
SwipeToDelete reveals Delete  →  user taps Delete  →  confirm dialog
   └─ Cancel: close dialog
   └─ Delete:
        ChatManager.DeleteChat(chatId):
          1. capture the ChatViewModel (for rollback) + its sibling index
          2. add chatId to recentlyDeleted guard
          3. RemoveChatLocally(chatId): remove from Chats + chatLookup;
             OnChatRemoved?.Invoke(chatId)  → ChatListView collapses+destroys row
          4. evict caches: ChatHistoryCache.DeleteHistory(GetCacheRoot(), chatId);
             rewrite chats.json without this chat
          5. StartCoroutine(DeleteChatRoutine(chatId)):
               POST chat/delete
               success (status=="done"): leave guard until a later sync confirms
                   absence (or a timeout) clears it
               failure: rollback — re-add the ChatViewModel at its old index,
                   OnChatAdded?.Invoke(vm), remove from guard, fire an error toast
```

## Resurrection guard

`SyncAllChats` ([ChatManager.cs:298](Assets/Scripts/Main/ChatManager.cs)) runs
`/chats/filter` and merges via `ParseChatsJson(json, false)`, which **adds any
chat not already in `chatLookup`** (the `else` branch at
[ChatManager.cs:280](Assets/Scripts/Main/ChatManager.cs)). A sync that is
in-flight or fires immediately after a delete — before the server finishes
processing it — would re-add the just-deleted row.

Mitigation: a session-scoped `HashSet<string> recentlyDeleted` on `ChatManager`.
`ParseChatsJson`'s add-branch skips any `chat.id` in that set. An entry is cleared
once a subsequent `/chats/filter` response no longer contains it (server confirmed
the delete), or after a safety timeout. In-memory only — see edge cases for the
app-kill window.

## Cache eviction

Per-bot cache root is `{persistentDataPath}/BotCache/{CurrentBotId}/`
(`GetCacheRoot()`).

- **Messages** — `{root}/messages/{chatId}.json`. New
  `ChatHistoryCache.DeleteHistory(baseDir, chatId)` deletes the file (null/empty
  guarded, mirrors the existing path construction).
- **Chat list** — `{root}/chats.json`. On delete, rewrite it with the deleted
  chat filtered out so a cold launch before the next sync doesn't resurrect it.
- **Media** — `MediaCacheManager` is keyed by URL MD5 and shared across chats.
  **Intentionally left untouched** (harmless disk cache; per-chat eviction would
  require orphan-URL analysis). Out of scope.

## Prefab restructure (`ChatItem.prefab`)

Today: root (`ChatItem`, 600×200) has `Image` (white bg) + `Button` (row tap) +
`HorizontalLayoutGroup`, with children `Avatar` and `TextBlock`.

Target layering, front-to-back:

- `ChatItem` (root) — keep `ChatItemView`; **remove** the layout group from the
  root.
  - `DeleteButton` (sibling index 0, behind) — anchored to the right edge, fixed
    width (~150 ref units), full height; red background; trash icon + "Delete"
    label; `Button` that calls into `ChatItemView`/`SwipeToDelete` to raise the
    confirm dialog. Layout-independent (root has no layout group).

The confirm dialog **reuses the project's existing confirm-popup pattern**
(`PopupUI` / the delete-bot confirm) rather than introducing a new dialog — the
exact reuse target is chosen during planning after inspecting both. New UI is a
last resort per the project's "check existing assets first" rule.
  - `SwipeContent` (sibling index 1, in front) — stretch-fill the row; carries
    the `HorizontalLayoutGroup` + the white background `Image` + the row-tap
    `Button` + the `Avatar`/`TextBlock` children + the new `SwipeToDelete`
    component. This is the object that translates on X.

Implementation note (carries real risk): moving the background `Image`, the tap
`Button`, and the `Avatar`/`TextBlock` children under `SwipeContent` must preserve
`ChatItemView`'s serialized references. Reparenting in-editor keeps references by
fileID, but any destroy+recreate silently nulls them (see the project's
"builders must rewire consumers" gotcha). Verify every `[SerializeField]` on
`ChatItemView` still resolves after the restructure.

## Gesture details (`SwipeToDelete`)

- **Direction lock** (from `SwipeToReply`): on first significant movement, lock to
  horizontal only if `|dx| > |dy|`. A left drag (`dx < 0`) drives the reveal; a
  right drag past the open state re-closes. Vertical / right-from-closed /
  ambiguous drags route to the parent `SnappyFlickScrollRect`
  (`scroll.OnBeginDrag/OnDrag/OnEndDrag`) so the list still scrolls.
- **Clamp** translate to `[-revealWidth, 0]`.
- **Snap** with DOTween `DOAnchorPosX` (project convention): open at
  `-revealWidth` if released past 50%, else 0.
- **Guards**: respect `ScrollClickBlocker.IsBlocking` (don't start a gesture
  during fling momentum). While open or dragging, suppress the row-tap `Button`
  so a swipe never opens the chat.
- **Single open row**: opening one closes others (ChatListView tracks the open
  row, or rows close on any new drag-begin).
- **Recycle safety**: a row reset to closed (`translateX = 0`) on `Bind` so a
  reused/rebound prefab never shows a stale open state.

## Events & manager changes

- `event Action<string> OnChatRemoved` on `ChatManager` (alongside `OnChatAdded`
  / `OnChatListCleared`).
- `RemoveChatLocally(string chatId)` — removes from `Chats` + `chatLookup`, fires
  `OnChatRemoved`. (No single-chat removal exists today — only `Chats.Clear()`.)
- `ParseChatsJson` add-branch consults `recentlyDeleted`.

## Edge cases

- **Server delete fails** → optimistic rollback re-adds the row at its prior
  index + error toast.
- **App killed between local removal and server success** → chat is gone locally
  but still on the server; the next launch's `/chats/filter` re-adds it. Accepted:
  the delete genuinely did not complete. (Not worth a persisted tombstone given
  the server is the source of truth once the call succeeds.)
- **No active profile id** (`GetActiveProfileId()` null) → abort the delete, keep
  the row, log a warning. Should not happen from a populated list, but guarded.
- **Group chats (`@g.us`)** → handled by the same `recipient` derivation as send.
- **Row open during a background sync that reorders rows** → the open row should
  close on list mutation to avoid a stranded offset.

## Testing strategy

EditMode tests (`Assets/Tests/Editor/Chat/`, no asmdef) for the pure logic:

- `recipient` derivation (`@c.us` stripped, `@g.us` preserved, bare id passthrough).
- Resurrection guard: a chat id in `recentlyDeleted` is not re-added by a parse;
  cleared once absent from a later response.
- `ChatHistoryCache.DeleteHistory`: removes the file; null/empty-safe; no-throw
  when the file is absent.
- Snap-threshold math (open vs. closed decision) if extracted to a pure helper,
  in the spirit of `ScrollFabMath`.

Gesture feel, the prefab reveal layer, scroll-vs-swipe arbitration, and the real
`chat/delete` round-trip are verified in the Editor / on device (the gesture and
prefab can't be asserted headlessly).

## Out of scope

- Per-chat media cache eviction.
- Telegram chat deletion (no Telegram chats in this list).
- Archive / mute / pin / mark-unread swipe actions.
- Multi-select / bulk delete.
- Persisted tombstones across app restarts.
