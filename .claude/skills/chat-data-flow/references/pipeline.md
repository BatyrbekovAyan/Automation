# Chat Data Flow — Full Pipeline Reference

Read this for the complete per-layer field tables, the per-type `Normalize` breakdown, the cache layout, and a worked end-to-end example. Source of truth: `ChatManager.cs` (`Normalize`, `CreateViewModel`, `ParseMessageType`), `Assets/Scripts/Chat/*.cs`, `Assets/Scripts/UI/*ViewModel.cs`.

## Table of contents
1. The four message layers (field tables)
2. Normalize, per message type
3. Delivery status
4. Chat list path (ChatViewModel)
5. Caches
6. Worked example — add a field end to end

---

## 1. The four message layers

**`RawMessage`** (`Chat/RawMessage.cs`) — raw API shape, parsed by `JsonConvert`:
| Field | Type | Note |
|-------|------|------|
| `id`, `type`, `chatId`, `senderName` | string | `type` is the Wappi type string |
| `fromMe` | bool | |
| `time` | long | unix seconds |
| `caption` | string | media caption (sometimes) |
| `deliveryStatusRaw` | string | `[JsonProperty("delivery_status")]` |
| `body` | `JToken` | inline payload; "encrypted"/fallback URLs, `JPEGThumbnail`, dims, document meta |
| `s3Info` | `JToken` | hosted URLs (the good ones) + `expire` |
| `mediaInfo` | `JToken` | `[JsonProperty("media_info")]`; dims, duration |

**`NormalizedMessage`** (`Chat/NormalizedMessage.cs`) — typed, media resolved:
`id, chatId, senderName, messageType (enum), fromMe, time, text, thumbnailUrl, mediaUrl, mimeType, aspectRatio, fileName, videoUrl, duration, isSticker, expireTime, fileSize, pageCount, deliveryStatus`.

**`MessageViewModel`** (`UI/MessageViewModel.cs`) — flat UI binding model. Same media/meta fields, plus UI-specific: `isIncoming` (= `!fromMe`), `timestamp` (= `time`), `videoRotation` (from NativeGallery, 0 = unknown). `[Serializable]` so it can be cached to disk.

**`MessageItemView`** — reads the VM and renders the bubble (per-type padding, media, ticks).

## 2. Normalize, per message type

After setting the common fields (`id/chatId/senderName/messageType/fromMe/time`) and delivery status (fromMe only), `Normalize` branches:

- **Chat:** `text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(raw.body?.ToString())`.
- **All media types:** extract caption from `raw.caption`, else `body["caption"]`; emoji-convert into `text`. Compute `aspectRatio = width/height` from `mediaInfo ?? body` (`JObject`), default `1.0`.
- **Video:** `thumbnailUrl = StageServerThumbnail(id, body["JPEGThumbnail"])`; `videoUrl = s3Info["url"]`; `expireTime = s3Info["expire"]`.
- **Image / Sticker:** `thumbnailUrl` from `body["JPEGThumbnail"]`; `mediaUrl` from `body["url"]` then overwritten by `s3Info["url"]` (HD); sticker sets `isSticker = true`. `expireTime` from `s3Info["expire"]`.
- **Audio / Voice:** `mediaUrl = s3Info["url"]`; `duration` from `mediaInfo["duration"]`. (Voice = Wappi `"ptt"`.)
- **Document:** `fileName = body["fileName"] ?? body["title"]`; `mimeType = body["mimetype"]`; `fileSize = body["fileLength"]`; `pageCount = body["pageCount"]`; `mediaUrl = s3Info["url"]`. If the caption equals the filename (ZWS-aware compare), null out `text` so it doesn't double-render under the document card.

`CreateViewModel` then copies every field across and sets `isIncoming = !fromMe`, `timestamp = time`; for `Video` it calls `EnqueueIncomingVideoThumb(vm)` to replace the server thumb with a native HD frame.

## 3. Delivery status

`DeliveryStatus` enum: `None, Pending, Sent, Delivered, Read, Failed`.
- Only set for `raw.fromMe` (incoming → `None`, no tick).
- `DeliveryTickFormatter.ParseWappiString(raw.deliveryStatusRaw)`; if that yields `None` for a fromMe message, fall back to `Sent` (Wappi has at least received it).
- Live changes propagate via `OnMessageStatusChanged(oldId, newId, status)`; the optimistic-send→ack transition swaps a temp id for the real Wappi id.

## 4. Chat list path (ChatViewModel)

Separate from messages. `chats/filter` → `JsonUtility.FromJson<ChatsResponse>` → for each chat, `new ChatViewModel(id, displayName, thumbnail, lastMsg, unixTime, unread, lastMsgId, lastMsgType, lastDeliveryStatus, isMine)` → `OnChatAdded`. `ChatViewModel` is mutable via `UpdateLastMessage`, `UpdateUnreadCount`, `UpdateLastMessageMeta`, etc., each firing `OnUpdated`/`OnLastMessageChanged` only when something actually changed (cheap diffing — don't bypass it). `FormatTimestamp` renders HH:mm / Yesterday / weekday / dd.MM.yy.

## 5. Caches

- **Chat list:** `persistentDataPath/all_chats_cache.json`.
- **Message history:** `ChatHistoryCache`, per `chatId`, capped at 100 messages.
- **Media/thumbnails:** `MediaCacheManager`, disk, keyed by URL or `thumb://{id}`.
- **Open-chat working set:** `_activeChatCache` (rendered VMs, mutated in place), `_cachedQueue` (un-rendered tail drained by `LoadNextPage`), `seenMessageIds` (dedup).
- Pattern: on open, render from cache instantly, then diff against server and reconcile (refresh URLs in place, append brand-new via `OnLiveMessagesReceived`). `PurgeCacheForBot(botName)` drops a bot's caches on delete.

## 6. Worked example — add a field end to end

Goal: surface a server `forwarded` flag on the bubble.

1. **`RawMessage.cs`** — `public bool forwarded;` (or `[JsonProperty("is_forwarded")] public bool forwarded;` if snake_case).
2. **`NormalizedMessage.cs`** — `public bool isForwarded;`
3. **`Normalize`** — after the common fields: `msg.isForwarded = raw.forwarded;` (or read from a `JObject` if it's nested in `body`).
4. **`MessageViewModel.cs`** — `public bool isForwarded;`
5. **`CreateViewModel`** — add `isForwarded = msg.isForwarded,` to the initializer.
6. **`MessageItemView`** — show a "Forwarded" label when `vm.isForwarded`.

Skip step 3 or 5 and the bubble always sees `false` — no error, just silently wrong. That's the whole reason this pipeline needs a checklist.
