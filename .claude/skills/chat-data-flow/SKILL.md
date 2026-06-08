---
name: chat-data-flow
description: Work with this Unity app's chat message pipeline — the path from Wappi JSON to rendered bubbles. Use whenever you add or change a message field, a message type, media/thumbnail handling, delivery status, the chat list, or anything touching RawMessage / NormalizedMessage / MessageViewModel / ChatViewModel or ChatManager's events. The pipeline has four data layers and two different JSON parsers; add a field to one layer and forget the next and it silently never reaches the UI.
allowed-tools: Bash(find *) Read(*) Edit(*) Write(*) Glob(*) Grep(*)
---

# Chat Data Flow — Wappi JSON → Rendered Bubbles

Messages travel through four distinct data shapes before they become UI. Each layer copies a subset of fields forward, by hand. There's no reflection or auto-mapping, so the failure mode is silent: add a field to the API model, forget to carry it through `Normalize` or `CreateViewModel`, and it simply never appears — no error. This skill exists so a field change touches every layer it must.

Canonical source: `Assets/Scripts/Main/ChatManager.cs` (`Normalize`, `CreateViewModel`, `ParseMessageType`, the events block) and the models in `Assets/Scripts/Chat/` + `Assets/Scripts/UI/`.

## The pipeline

```
wappi.pro JSON
  │
  ├─ CHAT LIST:  chats/filter ──JsonUtility──► ChatsResponse ──► new ChatViewModel(...) ──► OnChatAdded ──► ChatListView
  │
  └─ MESSAGES:   messages/get ──JsonConvert──► MessagesResponseRaw ──► RawMessage[]
        └─ Normalize(RawMessage)      ► NormalizedMessage   (parse type, extract media URLs, captions, dims)
              └─ CreateViewModel(norm) ► MessageViewModel    (flatten for UI; isIncoming = !fromMe)
                    └─ OnBatchMessagesLoaded / OnLiveMessagesReceived ► MessageListView ► MessageItemView (bubbles)
```

The four message layers, in order: **`RawMessage`** (raw API, JToken bodies) → **`NormalizedMessage`** (typed, media resolved) → **`MessageViewModel`** (flat UI binding model) → the **`MessageItemView`** bubble that reads it.

## Two parsers — use the right one

| Path | Parser | Why |
|------|--------|-----|
| Chat **list** (`ChatsResponse`) | `JsonUtility.FromJson<>` (line ~175) | Flat, fast; no nested dynamic JSON |
| **Messages** (`MessagesResponseRaw`) | `JsonConvert.DeserializeObject<>` (Newtonsoft) | `RawMessage` has `JToken body/s3Info/mediaInfo` — `JsonUtility` can't parse those |

If you add a new message model with nested/dynamic fields, it must go through `JsonConvert`. `JsonUtility` silently yields nulls for `JToken` and dictionary-shaped fields.

## Adding or changing a message field = a four-layer change

This is the #1 reason a field "doesn't show up." To surface a new piece of data from the API in a bubble, change **all four**:

1. **`RawMessage`** (`Assets/Scripts/Chat/RawMessage.cs`) — add the field. If the JSON key is snake_case, annotate it: `[JsonProperty("delivery_status")]`. Use `JToken` for nested/uncertain shapes.
2. **`Normalize`** (in `ChatManager.cs`) — read it off `raw` (often from `raw.body`, `raw.s3Info`, or `raw.mediaInfo` as a `JObject`) and set it on the `NormalizedMessage`. Add the storage field to `NormalizedMessage.cs`.
3. **`CreateViewModel`** (in `ChatManager.cs`) — copy `msg.yourField` into the `MessageViewModel`. Add the field to `MessageViewModel.cs`.
4. **`MessageItemView`** — consume it when binding the bubble.

Miss any one and the value is dropped between layers with no warning. The self-check below is the guard.

## Message type mapping

`ParseMessageType(string)` maps the Wappi `type` string to the `MessageType` enum (`Chat, Image, Video, Audio, Voice, Sticker, Document, Unknown`). Watch the non-obvious one: **voice notes arrive as `"ptt"` → `MessageType.Voice`**. Anything unrecognized → `MessageType.Unknown` (never throw). When handling a new type, add it here first, then branch on it in `Normalize`.

## Media handling rules

`Normalize` resolves media per type with a consistent priority: **thumbnail first (instant render), then the HD S3 URL overwrites it**:

- The server `JPEGThumbnail` base64 goes through `StageServerThumbnail(id, payload)`, which caches it and returns a `thumb://{id}` key — or **`""` on failure** (never a key to a file that wasn't written, so the bubble falls back to loading/black instead of a permanent black card).
- The real media URL comes from `s3Info["url"]`; its `s3Info["expire"]` populates `expireTime`. **S3 URLs expire** — that's why the refresh machinery exists (see in-place mutation below). Images/videos prefer the S3 HD url over the inline `body["url"]`.
- `aspectRatio = width/height` from `mediaInfo ?? body`, default `1.0`. Video also extracts a native device thumbnail later (`EnqueueIncomingVideoThumb`).

## Refresh media by mutating in place — don't recreate the ViewModel

`_activeChatCache` holds the exact `MessageViewModel` references that the rendered `MessageItemView`s are bound to. To refresh an expired media URL, **mutate the existing VM's `mediaUrl`/`videoUrl`/`thumbnailUrl`/`expireTime` in place and fire `OnMessageMediaRefreshed`** — the listener re-binds and picks up the new URL. Creating a fresh VM instead detaches it from the rendered bubble and the update is invisible. See `RefreshCachedMessageMedia`.

## ChatManager events (the output surface)

Views subscribe to these — they're the contract, don't poll:

| Event | Payload | Fires when |
|-------|---------|-----------|
| `OnChatAdded` | `ChatViewModel` | A chat enters the list |
| `OnChatListCleared` | — | List reset before a rebuild |
| `OnChatSelected` | `chatId` | Chat opened; `MessageListView` clears bubbles synchronously |
| `OnBatchMessagesLoaded` | `List<MessageViewModel>, bool isOlderPage, bool hasMore` | First screen + paginated history |
| `OnLiveMessagesReceived` | `List<MessageViewModel>` | Brand-new messages appended (only ever adds) |
| `OnMessageStatusChanged` | `oldId, newId, DeliveryStatus` | Delivery tick changes / optimistic→ack id swap |
| `OnMessageMediaRefreshed` | `MessageViewModel` | An in-place media URL refresh (re-bind) |
| `OnMediaSendProgress` | `tempId, float 0..1` | Outgoing media send progress (video radial ring) |
| `OnMessageRemoved` | `messageId` | A bubble must be removed (cancelled send) |
| `OnActiveBotChanged` / `OnEmptyState` | `string` / `EmptyStateReason` | Bot switch / empty-state UI |

## Phase gating & other gotchas

- **Chat open is a state machine** (`ChatManager.Phase`: `Idle/Prep/Slide/Populate`). Heavy main-thread work (decode, instantiate) is gated on the phase to keep the slide-in smooth. New heavy work in the chat-open path must check `Phase` like `MessageListView` and `MessageItemView.AcquireDecodeSlot` do — don't decode during `Prep`/`Slide`.
- **Cache-first, then diff.** On open, render from `ChatHistoryCache` instantly, then quietly reconcile against the server; outgoing-only delivery status; `seenMessageIds` dedups.
- **Delivery status is outgoing-only.** `Normalize` sets it only when `raw.fromMe`; incoming messages stay `DeliveryStatus.None` (no tick). Empty/unknown for `fromMe` falls back to `Sent`.
- **Text runs through `UnicodeEmojiConverter.ConvertRealEmojisToSprites`, which prepends a zero-width space.** A plain `.Trim()` won't strip it — use ZWS-aware trim chars when comparing/trimming message text (see the Document caption-dedup in `Normalize`).
- **`isIncoming = !fromMe`** — the inversion happens in `CreateViewModel`. Keep that polarity.

Full per-layer field tables, the per-type `Normalize` breakdown, and a worked end-to-end field example are in **`references/pipeline.md`**.

## Self-check before you hand off

- [ ] New/changed field carried through **all four layers** (`RawMessage` → `Normalize`/`NormalizedMessage` → `CreateViewModel`/`MessageViewModel` → `MessageItemView`)
- [ ] snake_case JSON keys annotated with `[JsonProperty("...")]`; nested shapes typed as `JToken`
- [ ] New message models with nested fields parsed via `JsonConvert`, not `JsonUtility`
- [ ] New message type added to `ParseMessageType` before branching on it
- [ ] Media refresh **mutates the existing VM in place** + fires `OnMessageMediaRefreshed` (no recreated VM)
- [ ] `StageServerThumbnail`-style failures return `""`, not a key to an unwritten file
- [ ] Heavy chat-open work gated on `ChatManager.Phase`
- [ ] Message text compared/trimmed with ZWS-aware logic, not plain `Trim()`
