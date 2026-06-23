# Architecture

**Analysis Date:** 2026-06-23

## Pattern Overview

**Overall:** Monolithic single-scene Canvas-based mobile app with God-object orchestrator, event-driven UI binding, and three-phase async state machine for chat lifecycle.

**Key Characteristics:**
- Single persistent scene (`Assets/Scenes/Main.unity`) — all pages/dialogs toggled via `GameObject.SetActive()`, no scene transitions
- Manager singleton (`Manager.Instance`) orchestrates bot creation, settings, auth flows, and n8n/Wappi API calls
- ChatManager singleton (`ChatManager.Instance`) owns the entire chat UI system with explicit state phases (Idle, Prep, Slide, Populate)
- Event-driven UI: Views subscribe to ChatManager events (OnChatAdded, OnBatchMessagesLoaded, OnLiveMessagesReceived, OnMessageStatusChanged, etc.) rather than polling
- Coroutine-based async for all network I/O — UnityWebRequest + IEnumerator, no async/await
- 4-layer message pipeline: RawMessage (API) → NormalizedMessage (normalized) → MessageViewModel (UI binding) → rendered by MessageItemView

## Layers

**Orchestration (Manager, ChatManager):**
- Purpose: Top-level coordination of page state, API calls, bot CRUD, chat lifecycle, message sync
- Location: `Assets/Scripts/Main/Manager.cs`, `Assets/Scripts/Main/ChatManager.cs` (and ChatManager partials)
- Contains: Page state management, API call orchestration, event firing, bot activation/settings
- Depends on: Secrets, Bot, BotSettings, all external APIs (Wappi, n8n, GreenAPI)
- Used by: All views (ChatItemView, MessageItemView, BotSettings, BotStatusPill, etc.)

**Data Models (Chat folder):**
- Purpose: Serializable message and chat data, API response models, delivery status, reactions
- Location: `Assets/Scripts/Chat/` — RawMessage, NormalizedMessage, ChatDialog, ChatsResponse, MessagesResponseRaw, MessageType, MessageReaction, DeliveryStatus, ReplyParser, ReactionStore, etc.
- Contains: Message type enums, JSON-deserializable response classes, delivery status tracking, reaction payloads
- Depends on: JsonUtility (Unity), JsonConvert (Newtonsoft)
- Used by: ChatManager (parse → normalize → emit events), MessageViewModel (UI binding), MessageItemView (render)

**UI ViewModels (UI folder):**
- Purpose: Lightweight binding models for chat lists and message bubbles (read: UI state, not business logic)
- Location: `Assets/Scripts/UI/ChatViewModel.cs`, `Assets/Scripts/UI/MessageViewModel.cs`
- Contains: Chat title, last message, unread count, sender name (groups); message text, type, media URLs, delivery status, reactions
- Depends on: Nothing — pure data containers (no Unity methods, no API calls)
- Used by: ChatItemView, MessageItemView (render per-item; also mutated in-place by ChatManager event handlers to refresh stale URLs)

**UI Views (UI folder list/item renderers):**
- Purpose: Spawn and manage lists of chat rows and message bubbles; handle per-item interactions
- Location: `Assets/Scripts/UI/ChatListView.cs`, `Assets/Scripts/UI/MessageListView.cs`, `Assets/Scripts/UI/ChatItemView.cs`, `Assets/Scripts/UI/MessageItemView.cs`
- Contains: Prefab spawning logic, event subscription, tap/swipe interaction handlers (swipe-to-reply, long-press, reactions)
- Depends on: ChatManager events, ChatViewModel/MessageViewModel binding, gestures (SwipeToReply, MessageBubbleLongPress, ReactionBarController)
- Used by: ChatManager (drives list/item creation via OnChatAdded, OnBatchMessagesLoaded)

**Pages & Dialogs (Main folder top-level):**
- Purpose: Full-screen experiences (bots list, bot creation wizard, bot settings, chat view, profile)
- Location: `Assets/Scripts/Main/BotsPage.cs`, `Assets/Scripts/Main/BotSettings.cs`, `Assets/Scripts/Main/BotSettings.Auth.cs`, `Assets/Scripts/Main/ProfilePage.cs`, `Assets/Scripts/Main/PopupUI.cs`
- Contains: Form state, button wiring, panel toggling, validation flows
- Depends on: Manager, ChatManager, Bot, Secrets, partial subcomponents (BotSettings/EditableField, etc.)
- Used by: Manager (page activation/deactivation), users (tap buttons)

**Bot Entity (Main folder):**
- Purpose: Per-bot singleton state holder + MonoBehaviour on each bot prefab (in BotsParent)
- Location: `Assets/Scripts/Main/Bot.cs`
- Contains: whatsappProfileId, telegramProfileId, workflow IDs, activation toggle, business icon
- Depends on: PlayerPrefs (entity persistence), BusinessTypesSO (icon/color lookup)
- Used by: Manager (bot enumeration), Bot.EnableBot (activation toggle), BotSettings (reads/writes via PlayerPrefs)

**Chat System (Chat folder core + Main folder manager):**
- Purpose: Message sync, pagination, caching, normalization, media handling, reactions, replies
- Location: `Assets/Scripts/Chat/*` (100+ files: message models, caches, gesture handlers, media processors, emoji/link/reaction resolvers), `Assets/Scripts/Main/ChatManager.cs` and partials
- Contains: ChatHistoryCache (local persistence), MediaCacheManager (disk cache), ReplyParser (quote resolution), ReactionStore/Parser (emoji reactions), VideoThumbnailExtractor, SwipeToReply/SwipeToBack gesture handlers, audio/video controllers, link scrapers
- Depends on: Wappi API (messages/get, message/send, message/reaction, chat/delete), local filesystem (persistentDataPath)
- Used by: ChatManager (message pipeline), MessageItemView (render media, reactions, replies, links)

**Bot Settings UI (Main/BotSettings/ subcomponents):**
- Purpose: Reusable form primitives for the 5-tab bot config (General, Business, Products, Services, Prompts)
- Location: `Assets/Scripts/Main/BotSettings/EditableField.cs`, `EditableTextArea.cs`, `ScrollableInputField.cs`, `ScrollableTextArea.cs`, `NumberDisplayField.cs`, `SectionHeader.cs`, `ToggleRow.cs`, `AddItemButton.cs`, `FocusScrim.cs`, `ItemEditSheet.cs`, `ProductCardView.cs`, `ServiceCardView.cs`
- Contains: Input field wrappers with validation, card displays for products/services, section headers, toggle UI
- Depends on: TMPro, DOTween (animations), RectLayout
- Used by: BotSettings main component (assemble the 5-tab interface)

**Editor Tooling (Editor folder builders):**
- Purpose: Programmatic UI construction via [MenuItem] commands; wires up serialized refs and animators
- Location: `Assets/Editor/*Builder.cs` (30+ builders: BotSettingsRebuilder, BotSettingsConfirmChangePopupBuilder, BotSwitcherSheetBuilder, ChatDeleteConfirmBuilder, etc.)
- Contains: SerializedObject rewires, RectTransform layout setups, animation state configuration, prefab mass-edits
- Depends on: SerializedObject, Animator state machine names
- Used by: Editor [MenuItem] menus during dev to rebuild UI without manual hierarchy edits

## Data Flow

**Chat Sync & Open (3-phase state machine):**

1. **Idle** — Chat list visible or chat fully loaded
2. **Prep (300ms timer)** — User taps a chat row
   - SelectChat() queues OpenChatRoutine
   - Loads cache from disk (ChatHistoryCache)
   - Fires OnChatAdded events for each message (UI spawns bubbles)
   - Stages first-screen batch in `_pendingFirstBatch` (max 50 messages)
   - Queues SyncLatestMessages to fetch live updates
   - Does NOT touch UI or fire messages event yet
3. **Slide** — Chat slides in over the list
   - SwipeToBack.SlideInFromLeft animates over 350ms
   - First-screen batch waits in `_pendingFirstBatch`
4. **Populate** — Animation complete
   - MessageListView renders bubbles from `_pendingFirstBatch` via OnBatchMessagesLoaded
   - Any pending live messages queued during Prep fire via OnLiveMessagesReceived
   - Scroll position set to bottom (most recent messages visible)
   - Goes back to Idle for steady state

**Message Lifecycle (4-layer pipeline):**

```
Wappi /messages/get response (JSON)
  ↓
JsonConvert.DeserializeObject<MessagesResponseRaw>
  ↓
RawMessage[] (direct from API response)
  ↓
ChatManager.Normalize(RawMessage) → NormalizedMessage
  ├─ ReplyParser resolves quoted message (id-by-id lookup in cache or server fetch)
  ├─ MessageType inferred from raw.type + raw.mediaType
  ├─ Emoji converted via UnicodeEmojiConverter
  └─ senderName enriched from group chat list
  ↓
ChatManager.CreateViewModel(NormalizedMessage) → MessageViewModel
  ├─ mediaUrl/videoUrl/thumbnailUrl resolved with caching
  ├─ Reactions merged from ReactionStore (keyed by message id)
  ├─ VideoThumbnailExtractor queued for thumbnail generation
  └─ Delivery status mapped from raw.status
  ↓
OnBatchMessagesLoaded / OnLiveMessagesReceived event
  ↓
MessageListView spawns MessageItemView prefabs
  ↓
MessageItemView.OnEnable() binds VM data to UI
  ├─ Text/media/reactions rendered
  ├─ Links parsed and made tappable via TMPLinkHandler
  ├─ SwipeToReply + long-press + reaction bar wired
  └─ GreenApiAvatarFetcher loads sender avatar
```

**Outgoing Message Send:**

1. User types + taps Send (or long-press for quick-reply)
2. ExpandableInput.BuildAndSend → ChatManager.SendMessage
3. OptimisticSend: new MessageViewModel created with tempId, OnLiveMessagesReceived fires immediately (bubble appears)
4. Coroutine: UnityWebRequest POST to `/message/send` with Wappi auth
5. Server Ack: response `{ id: real_wappi_id }` arrives
6. OnMessageStatusChanged fires with (tempId → real_id, status=Sent)
7. MessageItemView updates messageId in place, changes time label color from gray → blue

**Reaction Send & Resolve:**

1. User long-presses a message bubble → ReactionBarController shows emoji picker
2. User taps emoji → ChatManager.SendReaction (coroutine to POST `/message/reaction` with body `{ body: emoji_codepoint, message_id }`)
3. Server echo may arrive (webhook, if wired) or sync catches it next /messages/get
4. ReactionParser extracts { emoji, message_id, reactor_jid }
5. ReactionStore merges by reactor (one emoji per person)
6. OnMessageReactionsChanged fires (same MessageViewModel reference, .reactions list mutated)
7. ReactionPillView re-renders in place (no prefab respawn)

**Quote/Reply Resolution:**

1. Incoming reply: `reply_message` snapshot may be absent or echoed
2. ReplyParser.FromSnapshot detects (body == own raw body) and blanks it
3. If text still blank: `QuotedMessageCache` checks if quoted message id is in recent history
4. If not found: ChatManager.QuoteResolve → UnityWebRequest to `/messages/id/get`
5. Result: flat `quotedMessageId`, `quotedSenderName`, `quotedText`, `quotedType`, `quotedThumbnailUrl` fields added to MessageViewModel
6. MessageItemView.ResolveQuotedMessage handles async load; QuotedCardTap renders the quote as a card

**State Management:**

- **Chat List**: `ChatManager.Chats` (List<ChatViewModel>), `chatLookup` (Dict<chatId → ChatViewModel>), persisted in `persistentDataPath/all_chats_cache.json`
- **Open Chat History**: `ChatManager._activeChatCache` (List<MessageViewModel>, max 100); synced in real-time by SyncLatestMessages (live appends only, no backfill)
- **Page Queue**: `ChatManager._cachedQueue` (List<MessageViewModel>), drained by LoadNextPage one batch at a time
- **Bot Data**: `PlayerPrefs` keyed by `transform.name` (e.g., `Bot0Name`, `Bot0isOnWhatsapp`, `Bot0Products0`, `Bot0Product0Price`)
- **Reactions**: `ReactionStore._byMessageAndReactor` (Dict<messageId → Dict<reactor_jid → MessageReaction>>)

## Key Abstractions

**ChatManager State Machine:**
- Purpose: Sequence async operations (cache load, slide animation, UI population) without blocking
- Pattern: 4-phase enum (Idle, Prep, Slide, Populate) + boolean gates (`_chatFetchesInFlight`, `_activeSync`, `_activeOpen` coroutine refs)
- Examples: `Assets/Scripts/Main/ChatManager.cs` (Phase property, SelectChat, OpenChatRoutine)
- Usage: MessageListView, ScrollToBottomFab, FirstScreenBudget check Phase during render to gate heavy work

**ReplyParser:**
- Purpose: Normalize the raw `reply_message` snapshot, detect echoing, and queue fetches for missing quotes
- Pattern: Cache-then-fetch with fallback; snapshot → validator → cache key lookup → server fetch if needed
- Examples: `Assets/Scripts/Chat/ReplyParser.cs`
- Usage: ChatManager.Normalize (pre-event), MessageItemView.ResolveQuotedMessage (post-render)

**ReactionStore & ReactionParser:**
- Purpose: Deduplicate incoming emoji reactions (one per reactor) and render reaction pills
- Pattern: `_byMessageAndReactor` Dict[messageId][reactor_jid] = MessageReaction; thread-safe set semantics
- Examples: `Assets/Scripts/Chat/ReactionStore.cs`, `Assets/Scripts/Chat/ReactionParser.cs`
- Usage: ChatManager.Normalize (merge parser results), OnMessageReactionsChanged event

**MediaCacheManager:**
- Purpose: Persist media files to disk (textures, videos, audio) keyed by URL or `thumb://messageId`; survive app restart
- Pattern: First check `persistentDataPath/media/{hash}`, if miss download via Wappi, save, return path
- Examples: `Assets/Scripts/Chat/MediaCacheManager.cs`, `ThumbnailKeyResolver.cs`
- Usage: MessageItemView.GetMediaPath → Texture2D.LoadImage / VideoPlayer.url

**ChatHistoryCache:**
- Purpose: Persist per-chat message history (up to 100 messages) to `persistentDataPath/{botId}_{chatId}.json` using JsonUtility
- Pattern: On open, load cache → emit events → fetch server update quietly in background
- Examples: `Assets/Scripts/Chat/ChatHistoryCache.cs`
- Usage: OpenChatRoutine (Prep phase), SyncLatestMessages (background refresh)

**SwipeToBack & SwipeToClose (Gesture Handlers):**
- Purpose: Singletons managing swipe gestures and slide-in/slide-out animations for pages/chats
- Pattern: Input.touches + drag → check direction + momentum → tween RectTransform.anchoredPosition
- Examples: `Assets/Scripts/Chat/SwipeToBack.cs`, `Assets/Scripts/Chat/SwipeToClose.cs`
- Usage: ChatManager (chat open/close), BotSettings (settings swipe-back)

**PlayerPrefs Entity Model (Bot):**
- Purpose: Persistent per-bot configuration (name, description, products, services, business type, workflow IDs)
- Pattern: Keys = `{botGameObjectName}FieldName` (e.g., `Bot0Name`, `Bot0Product0`, `Bot0Product0Price`)
- Examples: `Assets/Scripts/Main/Bot.cs`, `Assets/Scripts/Main/BotSettings.cs`
- Usage: Manager.LoadBots (startup enumeration), BotSettings (read/write per-field), Bot.DeleteBot (cleanup)

**Secrets (Lazy Loader):**
- Purpose: Single-access point for API keys (wappiAuthToken, n8nAPIKey, greenApi instance/token, telegramBotToken)
- Pattern: Load `Assets/StreamingAssets/secrets.json` once, cache in Secrets.Data, expose as static properties
- Examples: `Assets/Scripts/Main/Secrets.cs`
- Usage: Manager.Start, ChatManager.SendMessage (Wappi auth header)

## Entry Points

**Application Start:**
- Location: `Assets/Scenes/Main.unity` (single scene, always loaded)
- Triggers: Unity runtime (Scene load → Awake → Start)
- Responsibilities:
  1. Manager.Start → LoadBots (enumerate Bot prefabs in BotsParent, restore PlayerPrefs state)
  2. ChatManager.Awake → activate MessageListPanel (pre-wire SwipeToBack singleton)
  3. ChatManager.Start → ShowChatList(true) (display bots list; load last-active bot's chat list)
  4. BottomTabManager.Start → set up WhatsApp/Telegram/Profile tabs

**User Taps a Chat Row:**
- Location: `ChatItemView.OnPointerClick` → ChatManager.SelectChat(chatId)
- Triggers: User touch on chat row in ChatListPanel
- Responsibilities:
  1. Cancel any in-flight open/sync from previous chat
  2. Start OpenChatRoutine (Prep → Slide → Populate)
  3. Fire slide-in animation
  4. Render message list from cache + live sync

**User Taps Send (Chat Composer):**
- Location: `ExpandableInput.BuildAndSend` → ChatManager.SendMessage
- Triggers: User long-press on ExpandableInput panel or tap Send icon
- Responsibilities:
  1. Validate text (non-empty)
  2. Create optimistic MessageViewModel + OnLiveMessagesReceived (bubble appears instantly)
  3. POST to Wappi `/message/send` with auth header
  4. On success: OnMessageStatusChanged (tempId → real id)
  5. On failure: OnMessageRemoved (bubble reverted)

**User Taps Edit Bot:**
- Location: `Bot.OpenSettings` → SlideInFromRight animation
- Triggers: User tap EditButton on bot card
- Responsibilities:
  1. Activate BotSettings prefab for this bot
  2. Slide in from right edge
  3. Show 5-tab UI (General | Business | Products | Services | Prompts)

**User Taps Create Bot:**
- Location: `Manager.ShowAddBotForm` → multi-panel wizard
- Triggers: User tap + button in bot list (or test [MenuItem])
- Responsibilities:
  1. Sequence panels: Platform selector → Name input → Business type → Description
  2. On completion: POST to n8n /webhook/CreateWhatsappWorkflow (or Telegram)
  3. Poll Wappi for profile confirmation
  4. Create Bot prefab + save to PlayerPrefs
  5. Switch to new bot's chat list

**External: Incoming Messages (Webhook / Pull):**
- Location: ChatManager.SyncLatestMessages (coroutine, repeated every 3 seconds when chat open)
- Triggers: Periodic timer OR OnChatSelected event
- Responsibilities:
  1. GET `/messages/get?chat_id={chatId}&page=0&limit=50`
  2. Parse MessagesResponseRaw
  3. Normalize RawMessage[] → NormalizedMessage[] (resolve replies, reactions, emoji)
  4. CreateViewModel for each (merge reactions, extract media URLs)
  5. Dedup by message id (seenMessageIds)
  6. Fire OnLiveMessagesReceived (only new ones appended)

## Error Handling

**Strategy:** Defensive + Observable

**Patterns:**
- **Network failures**: Log error with response code + URL. Retry with exponential backoff OR fall back to stale cache. Show "Connection failed" pill in UI.
- **Malformed JSON**: Log raw response. Fall back to empty state. Never crash.
- **Cross-chat response corruption**: Detect by comparing response chatId with request chatId (CrossChatResponseGuard). Discard + retry up to 3 times.
- **Quote/Reaction missing**: Cache miss is expected (old message outside Wappi window). Render placeholder ("Message" or "…"). Never block the UI.
- **Media download failure**: Set mediaUrl to null, render download-failed card. User can retry via tap.
- **Database/PlayerPrefs loss**: Treat missing keys as defaults (empty string, 0, false). Never crash.

**Examples:**
- `Assets/Scripts/Chat/CrossChatResponseGuard.cs` — validates response chatId
- `Assets/Scripts/Chat/ReplyParser.cs` — handles missing snapshots
- `Assets/Scripts/Main/ChatManager.cs` — logs all Wappi errors with status code + URL

## Cross-Cutting Concerns

**Logging:** `Debug.Log`, `Debug.LogError` with context (method name, URL, response code)

**Validation:**
- Form input: non-empty name, valid phone format (Telegram/WhatsApp code flow)
- Media: file size < 100MB, video duration < 1 hour, format (MP4, JPG, PNG, WebP, MP3)
- API response: check status field, validate response structure, guard against null

**Authentication:**
- Wappi: `Authorization` header with `Manager.wappiAuthToken`
- n8n: `X-N8N-API-KEY` header with `Manager.n8nAPIKey`
- GreenAPI: instance ID + token in URL path (from secrets.json)
- Telegram: bot token in URL path (webhook validation via HMAC if implemented)

**Caching:**
- Chat list: `all_chats_cache.json`, refreshed every ShowChatList
- Message history: `{botId}_{chatId}.json`, loaded on open, drained by pagination
- Media: `{persistentDataPath}/media/{hash}`, URL-keyed, survives app restart
- Reactions: in-memory ReactionStore, merged at message render time

**Concurrency:**
- `_chatFetchesInFlight` gate ensures row-resolve backfill drains don't race chat-open fetches
- `_activeSync` coroutine ref allows cancellation when chat changes
- `_activeOpen` coroutine ref allows user tap-away during Prep phase
- OutboxStore queue serializes outgoing sends (one at a time, no concurrent POSTs)

**Platform-Specific Code:**
- `AndroidBridge.cs` — file picker, gallery access, audio playback
- `IOSBridge.cs` — photo library, video export, safe area insets
- `IOSAudioFix.cs` — workaround for iOS audio interruption + route change
- Isolated in bridge classes; ChatManager checks `#if UNITY_ANDROID / UNITY_IOS` only at bridge call sites

---

*Architecture analysis: 2026-06-23*
