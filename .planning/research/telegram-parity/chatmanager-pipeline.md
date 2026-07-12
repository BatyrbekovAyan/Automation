# chatmanager-pipeline

## Summary
The entire ChatManager pipeline is hardwired to WhatsApp at three levels: (1) 11 hardcoded `https://wappi.pro/api/sync/...` URL strings across 6 files (plus WappiMediaRequestFactory's Base constant); (2) a single profile resolver — ChatManager.BotState.cs GetActiveProfileId() returns only bot.whatsappProfileId and is the sole funnel for all 14 network call sites; (3) WhatsApp-keyed state: the empty-state machine (EmptyStateReason.BotHasNoWhatsApp, WhatsAppTabStateResolver, "WhatsappSyncUntil" PlayerPrefs key), chat-id parsing (`@c.us`/`@g.us` suffix assumptions in 6 places incl. a `chat.id[..^5]` slice), and the per-bot cache root BotCache/{botGameObjectName}/ which is bot-scoped but NOT channel-scoped — a bot with both channels would merge/collide chats.json, the messages/ dir, outboxes, and resolver caches. Concurrency guards (_chatFetchesInFlight, CrossChatResponseGuard, serial media/vthumb queues) are transport-agnostic and need no change beyond reset-on-channel-switch. The cleanest seam: a ChatChannel enum + ActiveChannel on ChatManager, one WappiEndpoints.Sync(channel, path) URL builder replacing string literals, GetActiveProfileId → channel-aware resolver, and GetCacheRoot → BotCache/{botId}/{channel}.

## Open questions
- tapi response shapes are unverified against the WhatsApp-tuned wire models: does Telegram chats/filter return thumbnail (avatars), isDeleted (chat delete stickiness), last_message_sender.pushname, unread_count? Does tapi messages/get carry chatId per message (CrossChatResponseGuard depends on it), s3Info, body.JPEGThumbnail, delivery_status, isReply/reply_message?
- tapi endpoint parity unknown for: chat/delete, message/reaction, message/mark/read, messages/id/get, message/{img|video|document}/send — CLAUDE.md documents only auth/status/profile endpoints for the tapi base; could not verify against live API (authenticated calls off-limits).
- Telegram chat-id format (and minimum length) — determines whether ChatManager.cs:288's chat.id[..^5] fallback would throw ArgumentOutOfRange for short ids, and what the recipient format for sends should be.
- Whether Wappi's concurrent-response crossing bug (messages/get + media/download) also affects tapi — assumed yes (keep the serial queues and _chatFetchesInFlight gate) but unconfirmed.
- Whether Telegram needs a post-auth sync window analogous to WhatsAppSyncWindowSeconds=300 (and whether Wappi tapi has the same slow initial-sync behavior).
- ParseMessageType's tapi type-string vocabulary (does Telegram voice arrive as 'ptt'? are there Telegram-only types like animation/round video that currently fall to Unknown and get silently dropped?).
- Whether the channel selector UX will be per-bot (one active channel at a time in the chats tab) — the recommended cache split assumes channel-scoped roots either way, but the in-memory single Chats list requires exactly one active channel at a time unless ChatViewModel gains a channel tag.

## Report
# WhatsApp touchpoint inventory — ChatManager chat pipeline

All paths under `/Users/ayan/Projects/Automation/`.

## 1. Hardcoded `wappi.pro/api/sync` URLs (chat pipeline)

| File:line | Endpoint | Method using it |
|---|---|---|
| `Assets/Scripts/Main/ChatManager.cs:391` | `chats/filter` (GET) | `SyncAllChats` — chat-list sync |
| `Assets/Scripts/Main/ChatManager.cs:525` | `messages/get` offset=0 (GET) | `SyncLatestMessages` — live sync of open chat |
| `Assets/Scripts/Main/ChatManager.cs:1102` | `messages/get` (GET) | `ValidateCachePageAgainstServer` — background URL validation |
| `Assets/Scripts/Main/ChatManager.cs:1175` | `messages/get` (GET) | `GetMessagesRoutine` — page fetch / pagination |
| `Assets/Scripts/Main/ChatManager.cs:1812` | `message/media/download` (GET) | `DownloadMediaRoutine` (serial queue worker; also reused by VideoThumbs `RefetchVideoUrl`, ChatManager.VideoThumbs.cs:255-269) |
| `Assets/Scripts/Main/ChatManager.cs:1933` | `message/send` (POST) | `PostTextMessageRoutine` (text send + retry) |
| `Assets/Scripts/Main/ChatManager.cs:2023` | `message/mark/read?mark_all=true` (POST) | `MarkChatAsRead` |
| `Assets/Scripts/Main/ChatManager.DeleteChat.cs:52` | `chat/delete` (POST) | `DeleteChatRoutine` |
| `Assets/Scripts/Main/ChatManager.ReactionSend.cs:66` | `message/reaction` (POST) | `PostReactionRoutine` |
| `Assets/Scripts/Main/ChatManager.ReactionResolve.cs:74` | `messages/get` offset=0 (GET) | `DrainReactionResolveQueue` — chat-list row detail backfill |
| `Assets/Scripts/Main/ChatManager.QuoteResolve.cs:96` | `messages/id/get` (GET) | `DrainQuoteResolveQueue` — quoted-message recovery |
| `Assets/Scripts/Chat/WappiMediaRequestFactory.cs:12` | `private const string Base = "https://wappi.pro/api/sync/message/"` → `img/send`, `video/send`, `document/send` (lines 14-20) | `EndpointFor(kind, profileId)`, consumed by `PostMediaMessageRoutine` (ChatManager.MediaSend.cs:260) |
| `Assets/Scripts/Main/WappiUnitySync.cs:31,82` | `messages/all/get`, `message/media/download` | LEGACY — class is not referenced by any script or the scene (only a comment in `LocalDataWipe.cs:29`); safe to ignore/delete |

Outside ChatManager but same base: `Manager.cs` (auth/QR/status/profile mgmt — already has tapi twins), `BotSettings.Auth.cs:98,184,220` (status/logout — WhatsApp-side of the already-split auth UI).

Auth header everywhere: `www.SetRequestHeader("Authorization", Manager.wappiAuthToken)` — same token for api and tapi, so only the base path (`/api/sync/` vs `/tapi/sync/`) is channel-variant.

## 2. `GetActiveProfileId` — the single profile seam

`Assets/Scripts/Main/ChatManager.BotState.cs:142-147`:
```csharp
private string GetActiveProfileId()
{
    Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
    if (bot == null) return null;
    return IsValidProfileId(bot.whatsappProfileId) ? bot.whatsappProfileId : null;
}
```
`IsValidProfileId` (BotState.cs:135-136) rejects null/empty and `Bot.UnauthedProfileSentinel` (`"-1"`, Bot.cs:67). Hardcoded to `bot.whatsappProfileId`; `bot.telegramProfileId` exists (Bot.cs:70) but is never read by the pipeline.

Call sites (17):
- ChatManager.cs:385 (SyncAllChats), 517 (SyncLatestMessages), 1098 (ValidateCachePageAgainstServer), 1168 (GetMessagesRoutine), 1806 (DownloadMediaRoutine), 1851 (SendTextMessageRoutine), 2004 (MarkChatAsRead)
- ChatManager.DeleteChat.cs:43 (DeleteChatRoutine)
- ChatManager.ReactionSend.cs:32 (SendReaction)
- ChatManager.MediaSend.cs:76 (StageLocalMedia — snapshotted into the OutboxEntry)
- ChatManager.ReactionResolve.cs:57,63,116 (drain loop + bot-changed abort checks)
- ChatManager.QuoteResolve.cs:85,91,142 (same pattern)

Note: outbox entries persist the profileId at send time (`ChatManager.cs:1909`, `MediaSend.cs:204`) and retries reuse `entry.profileId` (`ChatManager.Outbox.cs:83`) — but the retry URL is still built from the WhatsApp-only endpoint constants, so `OutboxStore.OutboxEntry` needs a channel field for correct cross-session retry.

Direct bypass: `Assets/Scripts/Chat/N8nSuggestionsProvider.cs:75-76` reads `bot.whatsappProfileId` / `bot.whatsappWorkflowId` directly for the SuggestReplies webhook payload.

## 3. Empty-state logic (WhatsApp-keyed)

- `EmptyStateReason` enum: `ChatManager.cs:2064-2068` — exactly two values: `NoBotsExist`, `BotHasNoWhatsApp`.
- `ComputeCurrentEmptyState` (`ChatManager.BotState.cs:174-189`): `hasWhatsApp = IsValidProfileId(bot.whatsappProfileId)`; `syncing = IsWhatsAppSyncing(...)`; maps through `WhatsAppTabStateResolver.Resolve(botCount, hasWhatsApp, syncing)` (`Assets/Scripts/Main/WhatsAppTabState.cs:11-20`, 4-state enum NoBots/NoWhatsApp/Syncing/Ready).
- `BeginLoadForActiveBot` (`BotState.cs:197-215`): aborts with `OnEmptyState(BotHasNoWhatsApp)` when `bot.whatsappProfileId` invalid; gates on the post-creation sync window (`IsWhatsAppSyncing`, BotState.cs:158-165, PlayerPrefs key suffix `"WhatsappSyncUntil"` BotState.cs:150 — written at `Manager.cs:1436` on WhatsApp auth confirm, deleted at `Bot.cs:185`; window constant `WhatsAppSyncWindowSeconds = 300` ChatManager.cs:102) and fires `OnWhatsAppSyncing`/`OnWhatsAppSyncReady` (ChatManager.cs:105-108).
- `SyncAllChats` also fires `BotHasNoWhatsApp` on null profile (`ChatManager.cs:388`).
- `RefreshActiveBotChats` (`BotState.cs:237-247`) — same whatsappProfileId + syncing guards; called by `BottomTabManager.cs:171` via `TabRefreshGate.ShouldRefreshChats(index, isInitial, WhatsAppTabIndex)` — the "WhatsApp tab" concept itself.
- Consumers: `Assets/Scripts/UI/EmptyStateView.cs:105-127` (hardcoded copy "WhatsApp not connected" / "Connect WhatsApp"), `Assets/Scripts/UI/SyncingView.cs:39-53` (subscribes OnWhatsAppSyncing/Ready + IsWhatsAppSyncing), `ChatListView.cs`.

## 4. Cache keying — and what `{botId}` is

`{botId}` = the bot **GameObject name** under BotsParent (e.g. `"Bot0"`) = `ChatManager.CurrentBotId` (BotState.cs:14), sanitized by `SanitizeBotId` (BotState.cs:87-96). It identifies the BOT, not the channel.

- Cache root: `GetCacheRoot()` → `{persistentDataPath}/BotCache/{botId}/` (BotState.cs:20-26).
- Chat list: `{root}/chats.json` (BotState.cs:219, 244) — **one file per bot**. A bot with both channels would interleave/overwrite WhatsApp and Telegram chat lists here, and the in-memory `Chats`/`chatLookup` (ChatManager.cs:43-44) would merge both channels into one list. This is the biggest collision.
- Message history: `ChatHistoryCache` → `{root}/messages/{chatId}.json` (ChatHistoryCache.cs:40,69). Collision only if a Telegram chatId string equals a WhatsApp one (unlikely — WhatsApp ids end `@c.us`/`@g.us`) but both channels' files share one directory, so PurgeCacheForBot/privacy clears stay correct.
- Media: `MediaCacheManager` → `{root}/media/{MD5(full URL)}` (MediaCacheManager.cs:34-43, 123-135); memo dictionary is keyed per `CurrentBotId` (`EnsureBotScoped`, lines 45-57). URL-keyed → no cross-channel collision, but also bot-level only; synthetic keys `thumb://{msgId}`, `vthumb://{msgId}`, `staged://...` collide only on message-id equality across channels.
- Resolver caches: `QuotedMessageCache` → `{root}/quoted_messages.json` keyed by quoted message id (QuotedMessageCache.cs); `ReactionTargetCache` → `{root}/reaction_targets.json`. Shared file per bot; message-id keyed.
- Outbox: `{root}/outbox_{chatId}.json` (per ChatManager.PrivacyClear.cs:128); `_outbox` dropped on SetActiveBot (BotState.cs:114) — but not on any channel switch (no such concept exists).
- Verdict: **a dual-channel bot collides on chats.json and merges lists in memory**; everything else is keyed finely enough but shares directories. Cleanest fix: `BotCache/{botId}/{channel}/` with a one-time migration treating existing content as `whatsapp`.

## 5. Concurrency guards

- `_chatFetchesInFlight` (ChatManager.cs:137): int counter, **global per ChatManager instance** — counts every chat-scoped `messages/get` (open/sync/pagination/validation: incremented at ChatManager.cs:530, 1107, 1180). `WaitForChatFetchesToDrain` (ChatManager.cs:1315-1320, 3s bound) is awaited by the quote-resolve drain (QuoteResolve.cs:104), reaction-resolve drain (ReactionResolve.cs:82), and exposed publicly for suggestions (ChatManager.Suggestions.cs:18). Reset to 0 in SetActiveBot (BotState.cs:124), SelectChat (ChatManager.cs:496), ClearAllLocalHistory (PrivacyClear.cs:82). Not per-endpoint, not per-channel — if both channels ever fetch concurrently, the gate serializes them together (safe; possibly conservative). The crossing bug it guards is a **Wappi server** behavior — unknown whether tapi shares it (assume yes until proven).
- `CrossChatResponseGuard` (Assets/Scripts/Chat/CrossChatResponseGuard.cs): pure static check on `RawMessage.chatId` vs requested chatId — transport-agnostic, works for Telegram iff tapi messages carry `chatId`.
- Serial media download queue: `_mediaDownloadQueue` + `_mediaDownloadDraining` (ChatManager.cs:1769-1802) — one global serial worker, endpoint baked into `DownloadMediaRoutine` (line 1812). Cleared on bot switch via `ClearMediaDownloadQueue`.
- Serial video-thumb queue: `VideoThumbMaxConcurrent = 1` (ChatManager.VideoThumbs.cs:25); reuses `DownloadMediaForMessage` for URL re-fetch → inherits the WhatsApp endpoint.
- `_chatListSyncing` (ChatManager.cs:378) — collapses duplicate chats/filter syncs; bot-level, would need to be per-channel or reset on channel switch.

## 6. Avatar flow

- Actual runtime path: `chats/filter` response field `ChatDialog.thumbnail` (Assets/Scripts/Chat/ChatDialog.cs:9) → `ParseChatsJson` passes it as `avatarUrl` into `ChatViewModel` (ChatManager.cs:326; ChatViewModel.cs:48 `AvatarUrl`) → `ChatItemView` loads it: disk-cache hit via MediaCacheManager (ChatItemView.cs:68-88) else direct `UnityWebRequest.Get(vm.AvatarUrl)` + SaveImageToCache (ChatItemView.cs:134-144); fallback = deterministic colored-initial default (ApplyDefaultAvatarColor). Dashboard reuses the cached sprite via `TryGetChatAvatar` (ChatManager.Dashboard.cs:31-53).
- This path is **channel-agnostic** — it keys on whatever URL the server hands back. Open question: does tapi `chats/filter` populate `thumbnail`?
- `GreenApiAvatarFetcher` (Assets/Scripts/Chat/GreenApiAvatarFetcher.cs:15-73): POSTs `{chatId}` to Green API `getAvatar` (WhatsApp-only; response even has `existsWhatsapp`). **Dormant** — the only reference is commented out (`Manager.cs:219`); it is not in the runtime avatar path. For a Telegram chat id it would be meaningless; no work needed beyond leaving it out of the Telegram path.

## 7. Action-endpoint call sites

- **mark/read**: `MarkChatAsRead` ChatManager.cs:2000-2043 (URL :2023, `mark_all=true`, body `{message_id: vm.LastMessageId}`). Called from `SelectChat` (ChatManager.cs:482) when the opened chat had unread.
- **message/send** (+ `quoted_message_id`): `PostTextMessageRoutine` ChatManager.cs:1924-1994; recipient derived at :1932 (`chatId.EndsWith("@c.us") ? Replace : passthrough`); DTO `WappiSendTextRequest` ChatManager.cs:2046-2054 (`quoted_message_id` omitted when null). Callers: `SendTextMessageRoutine` (:1914, entered via public `SendTextMessage` :1837, runs on Manager.Instance to survive bot switch) and `RetryRoutine` (ChatManager.Outbox.cs:83).
- **media send**: `PostMediaMessageRoutine` ChatManager.MediaSend.cs:251-402; endpoint via `WappiMediaRequestFactory.EndpointFor` (:260), body via `BuildBody` (:330, recipient stripped by `NormalizeRecipient` WappiMediaRequestFactory.cs:22-23). Callers: `StageLocalMedia` (:224) and `RetryRoutine` (Outbox.cs:81).
- **message/reaction**: `PostReactionRoutine` ChatManager.ReactionSend.cs:62-124 (URL :66; body `{body, message_id}`; empty body removes). Caller: `SendReaction` (:59, optimistic apply + revert-on-failure).
- **chat/delete**: `DeleteChatRoutine` ChatManager.DeleteChat.cs:41-91 (URL :52, recipient via `WappiRecipient.FromChatId` :51). Caller: public `DeleteChat` (:28, optimistic remove + rollback). Note: Wappi's WhatsApp-side `isDeleted` sticky-flag semantics (honored in `ParseChatsJson` ChatManager.cs:274-282) may not exist on tapi.

## 8. Other channel-coupled logic

**Chat-id format (`@c.us` / `@g.us`)**
- `ChatManager.cs:288` — display-name fallback `chat.id[..^5]` slices exactly 5 chars (the `@c.us` suffix). A Telegram id shorter than 5 chars would throw; any Telegram id gets mangled.
- `ChatManager.cs:1932` — inline recipient strip (text send).
- `Assets/Scripts/Chat/WappiRecipient.cs:13-15` — `FromChatId` strips `@c.us` (used by DeleteChat, DashboardPage.cs:459).
- `Assets/Scripts/Chat/WappiMediaRequestFactory.cs:22-23` — `NormalizeRecipient` (duplicate of the above).
- `Assets/Scripts/UI/ChatViewModel.cs:25` — `IsGroup => ChatId.EndsWith("@g.us")` (drives group-row sender-name logic + ReactionResolve `RowNeedsResolve` ReactionResolve.cs:45).
- `Assets/Scripts/UI/MessageListView.cs:522,789` — per-bubble `isGroup = chatId.EndsWith("@g.us")` (group sender headers).
- Note `ChatDialog.isGroup` (ChatDialog.cs:7) exists in the wire model but the code derives groupness from the id suffix instead — for Telegram, switch to the server flag or a channel-aware IdFormat helper.

**Message-type strings**: `ParseMessageType` (ChatManager.cs:1610-1624) maps Wappi WhatsApp type strings (`"chat"`, `"ptt"`, `"sticker"`, `"reaction"`, ...). tapi type vocabulary unverified — Unknown types are dropped silently (:613, :1253).

**Wire-model assumptions** (Assets/Scripts/Chat/RawMessage.cs, MessagesResponseRaw.cs, ChatsResponse.cs, ChatDialog.cs): fields like `s3Info`, `body.JPEGThumbnail`, `delivery_status`, `reply_message`/`contact_name` (ReplyParser.cs:106-109), `last_message_sender.pushname` are all observed WhatsApp-Wappi shapes; parsing (Normalize, ChatManager.cs:1370-1546) is null-tolerant so unknown shapes degrade rather than crash, but every media/reply/reaction feature needs tapi shape verification.

**Dashboard** (WhatsApp-only v1 by design): `DashboardPage.cs:90,106-108` builds profileIds and profile→bot map exclusively from `bot.whatsappProfileId`; `SessionChatMap.cs` doc says "WhatsApp profile id"; deep-link `SetActiveBot`+`SelectChat` (DashboardPage.cs:408,423) assumes the chat lives in the (WhatsApp) list.

**Suggestions**: `N8nSuggestionsProvider.cs:75-76` (whatsappProfileId/whatsappWorkflowId in payload). The ChatManager accessors it uses (Suggestions.cs, RecentMessages.cs) are channel-agnostic.

**Reply-mode store**: `SemiAutoStore.Key = {botId}_semiAuto_{chatId}` (SemiAutoStore.cs:29) — bot+chat keyed; no channel collision because chatIds differ per channel, but the per-bot default (`ReplyModeToggleBinder.GetMode(botId)`) is bot-level and would apply to both channels.

**Notification/unread**: `IncomingNotifyPolicy`, `NotificationFx`, `ApplyUnreadBadge` — channel-agnostic (keyed on chatId/currentChatId only).

**Debug dump**: `SyncAllChats` writes `persistentDataPath/response.txt` unconditionally (ChatManager.cs:401-405) — shared, unkeyed (cosmetic).

## Recommended channel-resolution seam

The code is already funnel-shaped: **every** pipeline request resolves its profile through `GetActiveProfileId()` and builds its URL inline. Minimal-diff seam:

1. `ChatChannel` enum (`WhatsApp`, `Telegram`) + `ChatManager.ActiveChannel` alongside `CurrentBotId` (BotState.cs), set by whatever UI selects the channel; `SetActiveBot` grows a channel parameter (or a separate `SetActiveChannel` that reuses the same stop-coroutines/reset/BeginLoad block, BotState.cs:104-129 — the reset choreography is exactly what a channel switch needs too).
2. Static URL builder, e.g. `WappiEndpoints.Sync(ChatChannel ch, string pathAndQuery)` → `https://wappi.pro/{api|tapi}/sync/{path}` — replaces the 11 literals; extend `WappiMediaRequestFactory.EndpointFor(kind, profileId, channel)` (it's already the pure, unit-tested factory pattern to copy).
3. Replace `GetActiveProfileId()` body with a channel switch over `bot.whatsappProfileId` / `bot.telegramProfileId` (same `IsValidProfileId` guard) — zero call-site churn. Add `channel` (and keep `profileId`) on `OutboxStore.OutboxEntry` so retries rebuild the right URL.
4. `GetCacheRoot()` → `BotCache/{botId}/{channelSuffix}/` (whatsapp = legacy-compatible, e.g. keep existing root for WhatsApp and add `/telegram` subdir, or migrate). This automatically channel-scopes chats.json, messages/, media/, outboxes, and both resolver caches, and keeps `PurgeCacheForBot`/`PrivacyClear` correct (they already iterate `BotCache/*` recursively).
5. Channel-aware id helper (one home for `WappiRecipient.FromChatId`, `NormalizeRecipient`, the `[..^5]` slice, and `IsGroup`) — e.g. `ChatIdFormat.DisplayFallback(id, ch)`, `.Recipient(id, ch)`, `.IsGroup(id, ch)` (Telegram: prefer `ChatDialog.isGroup`).
6. Generalize the empty-state axis: `EmptyStateReason.BotHasNoWhatsApp` → `BotHasNoChannel` (+ channel payload on `OnEmptyState`), parameterize `WhatsAppTabStateResolver`, and make the sync-window key suffix channel-qualified (`"WhatsappSyncUntil"` / `"TelegramSyncUntil"`) — `IsWhatsAppSyncing`/`WhatsAppSyncGate` are otherwise pure.
