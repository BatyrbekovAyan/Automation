# External Integrations

**Analysis Date:** 2026-06-23

## APIs & External Services

**Wappi.pro (WhatsApp channel):**
- **What it's used for:** Chat synchronization, message history, media download, QR code login, phone code auth, chat deletion, message sending, reactions
- **SDK/Client:** Custom via `UnityWebRequest` (no SDK)
- **Auth:** `Authorization` header with `Manager.wappiAuthToken`
- **Base URLs:**
  - REST: `https://wappi.pro/api/sync/` (sync endpoints), `https://wappi.pro/api/` (profile endpoints)
  - Endpoints:
    - `chats/filter` — filtered chat list with pagination and deleted-chat metadata
    - `messages/get` — paginated message history (limit 50 per page, requires `chat_id` + `profile_id`)
    - `messages/all/get` — all messages for a profile (bulk, no chat filter)
    - `messages/id/get` — fetch SINGLE message by id to resolve quoted replies (requires `profile_id` + `message_id`)
    - `message/send` — outgoing message (supports `quoted_message_id` for threaded replies)
    - `message/reaction` — emoji reactions (POST body `{ body, message_id }`, empty `body` removes)
    - `message/media/download` — download media by `message_id` (IMPORTANT: serialized to prevent crossing, see memory)
    - `chat/delete` — soft-delete chat (POST body `{ recipient }` where recipient = chatId minus `@c.us`)
    - `qr/get` — WhatsApp QR code for web login
    - `auth/code` — request phone auth code
    - `get/status` — connection status (active/inactive/needs_auth)
    - `contact/info` — contact details
    - `profile/add`, `profile/delete`, `profile/logout` — profile lifecycle

**Wappi.pro (Telegram channel):**
- **What it's used for:** Chat synchronization, message history, QR code login, phone code auth, profile management
- **SDK/Client:** Custom via `UnityWebRequest`
- **Auth:** `Authorization` header (same token as WhatsApp)
- **Base URLs:**
  - REST: `https://wappi.pro/tapi/sync/` (sync endpoints), `https://wappi.pro/tapi/` (profile endpoints)
  - Endpoints:
    - `auth/qr` — Telegram QR code for login
    - `auth/phone` — request phone auth (POST)
    - `auth/code` — submit phone code (POST)
    - `get/status` — connection status
    - `profile/add`, `profile/delete`, `profile/logout` — profile lifecycle
    - NOTE: Telegram shares the core chat/messages API structure with WhatsApp (same endpoints, different base URL prefix)

**n8n (Workflow Automation):**
- **What it's used for:** Bot creation, bot configuration editing, workflow enable/disable, workflow deletion
- **SDK/Client:** Custom via `UnityWebRequest`
- **Auth:** `X-N8N-API-KEY` header with `Manager.n8nAPIKey`
- **Base URL:** `https://bagkz.app.n8n.cloud/`
- **Endpoints:**
  - Webhooks (POST): `/webhook/CreateWhatsappWorkflow`, `/webhook/CreateTelegramWorkflow` — trigger workflow creation via form POST
  - Webhooks (POST): `/webhook/EditWhatsappWorkflow`, `/webhook/EditTelegramWorkflow` — modify bot settings (products, services, prompts)
  - API (POST): `/api/v1/workflows/{id}/activate` — enable a workflow
  - API (POST): `/api/v1/workflows/{id}/deactivate` — disable a workflow
  - API (DELETE): `/api/v1/workflows/{id}` — delete a workflow
  - Webhook (POST): `/webhook-test/UploadFile` — file upload for document processing (business documents, products, services)
- **Request pattern:** FormData POST with multipart/form-data encoding (file uploads) or JSON body

**Green API (WhatsApp Auth & Avatars):**
- **What it's used for:** Avatar fetching, WhatsApp QR code, authorization code generation, connection status
- **SDK/Client:** Custom via `UnityWebRequest`
- **Auth:** Instance ID and token embedded in URL path (`/waInstance{id}/{method}/{token}`)
- **Credentials:** Loaded from `secrets.json` under `greenApi` (auth) and `greenApiAvatar` (avatars)
- **Base URLs:**
  - Auth: `https://4100.api.green-api.com/` (WhatsApp authentication flows)
  - Avatars: `https://7103.api.greenapi.com/` (profile picture download)
- **Endpoints:**
  - `getAvatar` — fetch contact avatar image
  - `qr` — WhatsApp QR code for web login
  - `getAuthorizationCode` — request authorization code
  - `getStateInstance` — connection state
  - `startAuthorization` — begin auth flow
  - `sendAuthorizationCode` — submit auth code
- **URL format:** `{baseUrl}/waInstance/{instanceId}/{method}/{token}`

## Data Storage

**Databases:**
- None (no SQL/NoSQL database)
- All persistent data stored locally via `PlayerPrefs` or filesystem

**File Storage:**
- **Local filesystem** — iOS/Android `persistentDataPath`
  - Chat history cache: `{persistentDataPath}/all_chats_cache.json` (single file, all chats)
  - Per-bot caches: `{persistentDataPath}/BotCache/{botId}/` (directory per active bot)
    - Message history: `{persistentDataPath}/BotCache/{botId}/all_chats_cache.json` (cached messages for that bot's chats)
  - Media downloads: `{persistentDataPath}/media/` (images, videos, documents)
  - Link metadata: `{persistentDataPath}/link_metadata.json` (OG tags from shared links)
  - Emoji patches: `{persistentDataPath}/emoji_patch/` (downloaded emoji sprite atlases)
- **Application.streamingAssets** — shipped with app
  - `Assets/StreamingAssets/secrets.json` (template: `secrets.json.example`, NOT checked in)
- **Wappi.pro server** — authoritative source for all chat/message data; client caches are derivatives

**Caching:**
- **MediaCacheManager** (`Assets/Scripts/Chat/MediaCacheManager.cs`) — in-memory + disk cache for images/videos/documents
  - Key: URL hash or `thumb://messageId` for thumbnails
  - Lifecycle: loaded on chat open, persisted to disk, validated on next open
- **ChatHistoryCache** (`Assets/Scripts/Chat/ChatHistoryCache.cs`) — per-chat message buffer (max 100 messages)
  - Keyed by `chatId`
  - Used for pagination backfill and quota-aware scroll-to-top
- **QuotedMessageCache** (`Assets/Scripts/Chat/`) — resolves quoted reply text when `reply_message` snapshot is missing/echoed
  - Fetched from Wappi via `messages/id/get` on first encounter
  - Stored per-chat; lookup by `stanzaId`
- **ReactionTargetCache** — maps reaction `stanzaId` to target message text
  - Populated from the same `messages/get` 50-window response
  - Persistent; prevents refetch on scroll

## Authentication & Identity

**Auth Provider:**
- Wappi.pro (third-party multi-channel platform) — OAuth-equivalent token-based auth
  - User provides WhatsApp/Telegram phone number or QR code
  - Wappi issues long-lived `wappiAuthToken` (stored in `secrets.json`)
  - All subsequent Wappi API calls use this token in `Authorization` header

**Implementation:**
- `Manager.cs` — orchestrates bot creation wizard (MainPage → Channel → Name → Auth → Business → Summary → Confirmation)
- `BotSettings.Auth.cs` — partial class handling WhatsApp/Telegram QR code display and phone code flows
- `WhatsappCodeTimer.cs`, `TelegramCodeTimer.cs` — manage code request cooldown (PlayerPrefs persistence)
- Green API optional: alternate auth path for WhatsApp via `qr` / `sendAuthorizationCode`

**Per-Bot Storage:**
- Wappi profile IDs (`whatsappProfileId`, `telegramProfileId`) stored per-bot in `PlayerPrefs` (key: `Bot{N}isOnWhatsapp`, `Bot{N}WhatsappProfileId`, etc.)
- Workflow IDs (`whatsappWorkflowId`, `telegramWorkflowId`) stored in `Bot.cs` + `PlayerPrefs`
- Activation state (`isOnWhatsapp`, `isOnTelegram`) toggled via `Bot.cs` activation switch

## Monitoring & Observability

**Error Tracking:**
- None (no external error reporting service like Sentry, Bugsnag)
- Errors logged locally via `Debug.LogError()` with status code and URL

**Logs:**
- Console: `UnityEngine.Debug.LogError`, `.LogWarning`, `.Log`
- File dump (debug): `{persistentDataPath}/response.txt` — last API response saved for diagnostics
- Test output: `Tools/test-output/` (headless) or `Temp/claude/test-summary.json` (Editor)

## CI/CD & Deployment

**Hosting:**
- Apple App Store (iOS) — iOS builds compiled with IL2CPP
- Google Play Store (Android) — Android builds compiled with IL2CPP or Mono
- No server hosting (client-side only)

**CI Pipeline:**
- None automated (manual builds via Unity Editor or command-line)
  - Command-line build: `Unity -batchmode -nographics -projectPath . -buildTarget Android -quit`
  - Test headless: `Tools/run-tests-headless.sh` (launches Editor in batch mode, runs EditMode suite)

## Environment Configuration

**Required env vars:**
- None (app uses `secrets.json` file-based config, not environment variables)

**Secrets location:**
- `Assets/StreamingAssets/secrets.json` (file-based, loaded at runtime via `Secrets.cs`)
- Structure:
  ```json
  {
    "wappiAuthToken": "...",
    "n8nAPIKey": "...",
    "telegramBotToken": "...",
    "greenApi": {
      "apiUrl": "https://4100.api.green-api.com",
      "idInstance": "...",
      "apiTokenInstance": "..."
    },
    "greenApiAvatar": {
      "apiUrl": "https://7103.api.greenapi.com",
      "idInstance": "...",
      "apiTokenInstance": "..."
    }
  }
  ```
- **NOT checked into git** — copied from `secrets.json.example` at setup

## Webhooks & Callbacks

**Incoming (n8n → App):**
- None (app does not expose HTTP webhooks)
- n8n webhooks are outbound only (app triggers them, n8n executes workflows)

**Outgoing (App → n8n):**
- `/webhook/CreateWhatsappWorkflow` — POST when user creates a new WhatsApp bot
- `/webhook/CreateTelegramWorkflow` — POST when user creates a new Telegram bot
- `/webhook/EditWhatsappWorkflow` — POST when user edits bot settings (products, services, prompts)
- `/webhook/EditTelegramWorkflow` — POST when user edits bot settings
- `/webhook-test/UploadFile` — POST multipart/form-data with file attachment (documents, images)

**Webhook Request Format:**
- Method: POST
- Content-Type: `multipart/form-data` (file uploads) or `application/x-www-form-urlencoded` (form fields)
- Body: form fields (botName, products, services, prompts, etc.) + optional file attachment
- No authentication header (webhooks are public)

## Critical Integration Notes

**Wappi API Constraints (Server Bugs / Platform Limits):**
1. **Chat deletion per-profile**: `chat/delete` only affects the current Wappi profile; does NOT propagate to other bots on the same phone
2. **Soft-delete watermark**: `isDeleted: true` is sticky even after new activity; no reliable revival signal; thus `ChatManager` hides deleted chats permanently
3. **HTTP 400 on re-delete**: Wappi returns error if trying to delete an already-deleted chat; `ChatManager.DeleteChat` handles gracefully
4. **Concurrent media/download crossing** (CONFIRMED SERVER BUG): multiple `/message/media/download` requests can receive each other's files; FIX = strict serial queue (see `Memory.md`)
5. **Concurrent messages/get crossing** (CONFIRMED SERVER BUG): multiple `/messages/get` requests get interleaved; FIX = `CrossChatResponseGuard` + `_chatFetchesInFlight` gate (see `Memory.md`)

**Message Pipeline:**
1. Raw Wappi response (`RawMessage[]`) → normalize → deduplicate by `id` → create view models
2. Quoted replies: snapshot in `reply_message` may be missing or echo the own message text
   - Detected by `ReplyParser.FromSnapshot` (snapshot.body == own.body) → blank it
   - Fetch real text via `messages/id/get` (QuotedMessageCache) → re-bind view
3. Reactions: stanza-based; `stanzaId` is immutable ID of the target message
   - Resolved by matching `stanzaId` in the same `messages/get` response (not via separate fetch)
4. Media URLs: encrypted in `body` (bad), hosted in `s3Info.url` (good)
   - `s3Info` URLs are ephemeral; re-fetched on chat re-open via `message/media/download`

---

*Integration audit: 2026-06-23*
