# Chat Mark-Read — Persist Read State to Wappi (Phase 1.5 follow-up)

## Problem

Phase 1's unread badge does an optimistic local reset (`UpdateUnreadCount(0)`) when the user opens a chat. That hides the badge instantly — good UX — but the reset is **client-only**. The next `/chats/filter` sync returns the unchanged `unread_count` from the server, and the badge reappears. The user sees this clearly on app restart: every chat they read in-app still shows its badge.

Real WhatsApp persists read state server-side. Wappi exposes `POST /api/sync/message/mark/read` for the same purpose.

## Goal

When the user opens a chat with `UnreadCount > 0`, tell Wappi the chat has been read so the server's `unread_count` drops to 0. After this, the optimistic reset and the next sync agree, and the badge stays hidden across app restarts.

## Non-goals

- No per-message read receipts (Phase 2 territory — ✓✓ blue ticks).
- No "mark unread" reverse action.
- No bulk mark-all-chats-read action.
- No UI for read state beyond the existing badge.
- No retry on failure — next sync corrects any drift.

## Data model — Wappi response (confirmed from user's earlier API sample)

Every dialog in `/chats/filter` already includes:

```json
{
  "id": "77472714618@c.us",
  "last_message_id": "3A3A11795E74705F43A4",
  "last_timestamp": "2026-05-11T06:54:37Z",
  "unread_count": 0,
  ...
}
```

`last_message_id` is the field we'll use to identify the chat to the mark-read endpoint. It is currently dropped by `ChatDialog` (the field is not declared).

## Wappi endpoint contract

```
POST https://wappi.pro/api/sync/message/mark/read?profile_id={profileId}&mark_all=true
Authorization: {wappiAuthToken}
Content-Type: application/json

{ "message_id": "<chat's last_message_id>" }
```

`mark_all=true` is the right choice for "user opened the chat" — it marks every unread message in the chat that owns `message_id`. One call, one chat, fire-and-forget.

Response on success (200):
```json
{ "status": "done", "timestamp": 1683969533, "time": "...", "uuid": "..." }
```

We don't need to consume the response body — a 200 result is enough. Log non-200 outcomes for debugging but don't surface errors to the user; next sync will correct any drift.

## Target file changes

Three files. All edits.

### 1. `Assets/Scripts/Chat/ChatDialog.cs` — add field

Add one field after `unread_count`:

```csharp
public string last_message_id;
```

Snake_case matches the JSON key. `JsonUtility` populates it automatically.

### 2. `Assets/Scripts/UI/ChatViewModel.cs` — expose `LastMessageId`

Add a property + a constructor parameter (default-valued so the existing call site keeps compiling between tasks):

```csharp
public string LastMessageId { get; private set; }
```

Constructor gains `string lastMessageId = null` as a new parameter, assigned in the body. Update on merge via a new mutator:

```csharp
public void UpdateLastMessageId(string id)
{
    if (LastMessageId == id) return;
    LastMessageId = id;
    // No NotifyUpdated — this is metadata for API calls, not visual state.
}
```

The `UpdateLastMessageId` method does NOT fire `OnUpdated` because nothing visual depends on `LastMessageId` — only `MarkChatAsRead` reads it. Firing the event would needlessly re-run row repaints.

### 3. `Assets/Scripts/Main/ChatManager.cs` — coroutine + invocation

Wire the new field through `ParseChatsJson` (both create and merge paths, like `unread_count`). Add a new coroutine `MarkChatAsRead(string chatId)` that follows the project's canonical POST pattern (mirrors the existing send-message coroutine at line 698). Call it from `SelectChat` immediately after the optimistic local reset, gated on `UnreadCount > 0` (no point hitting the API if the chat has nothing unread).

## Behavior

When user taps a chat:
1. Optimistic local reset fires (existing). Badge hides immediately. Row stays in place (post-fix from Phase 1).
2. `MarkChatAsRead(chatId)` coroutine starts as fire-and-forget.
3. Coroutine looks up the chat's `LastMessageId` + active `profile_id`. If either is missing, log warning and exit silently.
4. Coroutine POSTs `mark/read?profile_id=X&mark_all=true` with body `{"message_id": "<lastMessageId>"}`.
5. On 200: silent success.
6. On non-200 / timeout / error: log error with status + response body; do nothing else. Next sync will refresh `unread_count` to whatever the server thinks (typically still 0 because the server processed our call, OR back to the real value if the call failed).

## Failure handling

- **API call fails** (network, 5xx, auth): badge stays hidden locally (optimistic reset already won); next sync returns the actual `unread_count` from the server. If that's still > 0, the badge reappears — which is correct behavior. The user sees the reality.
- **`last_message_id` is missing/empty**: skip the API call, log a warning. The chat list hasn't synced yet OR the chat genuinely has no messages (unusual). Badge stays hidden locally; next sync corrects.
- **`profile_id` is missing**: skip the API call. This means no bot is connected — `SelectChat` shouldn't be reachable in that state anyway.

## Non-blocking call

`StartCoroutine(MarkChatAsRead(chatId))` runs alongside the existing slide-in animation. The API call typically returns in 100-500 ms; the slide animation is ~300 ms. By the time the user sees the message panel, the call has usually completed. No user-visible waiting.

## Testing

Manual on device:

1. Connect a Wappi WhatsApp bot. Find a chat with `unread_count > 0`.
2. Open the chat. Verify badge hides immediately.
3. Force-quit the app and reopen.
4. Navigate to the WhatsApp page. Wait for sync.
5. **Expected:** Badge stays hidden (server now says `unread_count = 0`).
6. **Regression check from Phase 1:** Row does not jump to top when badge hides on tap.
7. **Failure-mode check:** Disconnect network, open a chat with unread, then reconnect. Verify next sync re-establishes correct counts (badge re-appears if the server still has unread messages, or stays hidden if the call queued/succeeded).
8. **Console log check:** No errors on success. On simulated 4xx/5xx, expect a single `Debug.LogError` line with the status code.

## Risks

- **Wappi rate limits:** if a user opens many chats fast (10+ in 5 sec), we'd send a corresponding number of POSTs. Wappi rate limits are unknown; if hit, calls fail silently and next sync corrects. Acceptable.
- **Double mark-read:** if user opens the same chat twice (close and re-open in 1 sec), we send two POSTs. Idempotent on the server (the second is a no-op). Acceptable.
- **`mark_all=true` semantics on archived chats:** untested. If the user opens an archived chat, the call still goes through; server should still mark messages read. Phase 1 didn't introduce archived-chat-specific behavior so this is no worse than current.
