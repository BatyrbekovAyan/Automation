# tapi-shapes

## Summary
Wappi's Telegram API (tapi) is documented at https://wappi.pro/telegram-api-documentation (fetched raw, 82 methods enumerated) and diverges from WhatsApp in ways that break the app's parsers at specific known lines: text messages arrive as type "text" (not "chat") so ChatManager.ParseMessageType maps every Telegram text to Unknown and drops it; chat ids are bare numeric strings (no @c.us) so ChatManager.cs:288's `chat.id[..^5]` corrupts names and can throw; the dialog object has no isGroup and swaps last_timestamp/last_time types vs WhatsApp. Reactions require an extra `recipient` field, replies use a dedicated /tapi/sync/message/reply endpoint (no quoted_message_id on message/send), typing is `chats/typing/start` (plural), and chat/delete + messages/all/get do not exist on tapi. Avatars are natively available (dialog `thumbnail` fs.wappi.pro URL + contact/get), so GreenApiAvatarFetcher is not needed for Telegram. The n8n Telegram_Bot template is confirmed broken as-is (checks type=="chat", reads media_info.duration which Telegram replaces with length_seconds, and still posts to /api/sync/ WhatsApp URLs — CreateTelegramWorkflow's Set Fields node never rewrites them). The single biggest undocumented area is the sync-API shape of Telegram MEDIA messages (body/s3Info/attaches), which must be verified live.

## Open questions
- Sync-API (messages/get) JSON shape for ALL Telegram media types — body vs s3Info vs attaches, thumbnails, dimensions, durations — is completely undocumented; only webhook shapes are documented
- Telegram sticker / video-note / GIF type strings (no 'sticker' type appears anywhere in tapi docs)
- How incoming reactions are transported on tapi (no type:'reaction' message documented; messages/id/get shows a 'reactions' field on the target instead)
- Whether tapi message/send silently accepts quoted_message_id (undocumented) or replies strictly require /tapi/sync/message/reply
- Group/channel dialog 'type' values beyond 'user'/'chat' and group webhook payload shape (not documented)
- Whether dialog 'name' and 'thumbnail' are reliably populated in chats/filter on a live account (doc examples contradict each other across chats/get vs chats/days/get)
- Repeat auth/phone cooldown behavior on Telegram (FLOOD_WAIT) — whether the WhatsApp-style profile-recreate resend hack is needed
- isDeleted stickiness/revival semantics for Telegram chats (chat/delete endpoint does not exist on tapi)
- Whether the delivery_status webhook's 'messages' field is genuinely an object (not array) as the doc example shows

## Report
# Wappi tapi (Telegram) API surface vs the app's WhatsApp parsers

**Primary sources** (all fetched unauthenticated on 2026-07-12; no authenticated calls made, secrets.json untouched):
- TG docs: https://wappi.pro/telegram-api-documentation — raw HTML + extracted text archived at `/private/tmp/claude-501/-Users-ayan-Projects-Automation/923c29f5-49e9-4d01-aec9-c4a584b2c14c/scratchpad/tg-docs.{html,txt}` (594 KB page, 82 documented methods, full example JSON present)
- WA docs (for delta): https://wappi.pro/api-documentation — same scratchpad, `wa-docs.{html,txt}`
- Repo evidence: `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json`, `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`, `Assets/Scripts/Chat/*.cs`, `Assets/Scripts/Main/ChatManager*.cs`, `Assets/Scripts/Main/Manager.cs`
- Note: https://wappi.pro/api-docs returns HTTP 401 — the real public docs are the two `-documentation` pages above.

## 1. Endpoint map (tapi) vs WhatsApp (api) vs app call sites

| Capability | WhatsApp (`/api/sync/`) — app call site | Telegram (`/tapi/sync/`) | Divergence |
|---|---|---|---|
| Chat list (filtered) | `chats/filter` — ChatManager.cs:391 | **EXISTS**: `GET /tapi/sync/chats/filter?profile_id&client_name` | Same name/params (client_name filters number/name/last msg). Response dialog shape differs heavily (see §3) |
| Chat list (plain) | `chats/get` | `GET /tapi/sync/chats/get?profile_id&limit(200)&show_all&offset&order` | Same |
| Chat list by days | `chats/days/get` | `GET /tapi/sync/chats/days/get?...&is_archived&is_deleted` | Same; TG adds is_archived/is_deleted filters |
| Message history | `messages/get` — ChatManager.cs:525,1102,1175 | `GET /tapi/sync/messages/get?profile_id&chat_id&limit(100 max)&date&offset&mark_all&order` | Same name & params incl. `mark_all` (docs: mark_all works only for private chats, not groups) |
| All messages | `messages/all/get` — WappiUnitySync.cs:31 | **DOES NOT EXIST** on tapi | Dev-sync tool has no TG equivalent |
| Single message by id | `messages/id/get` — ChatManager.QuoteResolve.cs:96 | **EXISTS**: `GET /tapi/sync/messages/id/get?profile_id&message_id` | Same; TG response has extra fields (reactions, isEdited, isDeleted, attaches, location, isGif…) |
| Media download | `message/media/download` — ChatManager.cs:1812 | **EXISTS**: `GET /tapi/sync/message/media/download?profile_id&message_id` | Same; returns `{file_name, media_type, mime_type, file_link (S3, 7-day), file_link_expire}` |
| Send text | `message/send` — ChatManager.cs:1933 | **EXISTS**: `POST /tapi/sync/message/send` body `{recipient, body[, manager{}]}` + optional `bot_id` query | `recipient` accepts phone / @username / username / numeric user-id / chat-id. **`quoted_message_id` is NOT documented on tapi** (it is the app's WA reply mechanism, ChatManager.cs:2052) |
| Reply | (WA also has `message/reply`, app doesn't use it) | **Dedicated endpoint**: `POST /tapi/sync/message/reply` body `{body, message_id[, url, manager]}` | TG replies must switch endpoints; no recipient needed (message_id implies the chat) |
| Reaction | `message/reaction` — ChatManager.ReactionSend.cs:66 | **EXISTS**: `POST /tapi/sync/message/reaction` body `{body, recipient, message_id}` | **TG requires `recipient` (Да/required)**; WA body is only `{body, message_id}` (wa-docs.txt:671ff). App's WappiSendReactionRequest (ReactionSend.cs:67) would be rejected as-is. Empty body removes reaction on both |
| Mark read | `message/mark/read?mark_all=true` — ChatManager.cs:2023 | **EXISTS**: `POST /tapi/sync/message/mark/read?profile_id` body `{message_id}` | tapi mark/read documents **no `mark_all` query param** (WA has it). TG's bulk-read lever is `mark_all=true` on `messages/get` instead. Response detail: `"markRead success for chatID=98727543"` |
| Typing | `chat/typing/start` (WA, singular; also typing/stop) | **`POST /tapi/sync/chats/typing/start`** (plural "chats") body `{recipient}` | Name differs; **no typing/stop on tapi** |
| Chat delete | `chat/delete` — ChatManager.DeleteChat.cs:52 | **DOES NOT EXIST** on tapi (no chat/delete, archive, or unarchive anywhere in the 82-method list) | Swipe-to-delete has no server op for TG |
| Contact info | `contact/info` (WA) — Manager.cs:3614 (commented) | `GET /tapi/sync/contact/get?profile_id&recipient` → `{contact:{id,type,number,pushname,firstName,lastName,picture,thumbnail,username,isMe}}`; also `contacts/get` (all), `contact/add` | Different name; docs say contact/get returns "аватар и его превью" (picture/thumbnail) |
| Send media | `message/file/url/send`, base64 img/document/audio/video/send (deprecated) | All exist under tapi with same names; `file/url/send` body `{recipient, url[, file_name, caption, manager]}` | Parity |
| Forward / delete / edit msg | exist (app unused) | exist: `message/forward {message_id, recipient, drop_author}`, `message/delete {message_id}`, `message/edit {body, message_id[, url]}` | Parity |
| Auth QR | `qr/get` (WA) — Manager.cs uses `api/sync/qr/get` | **`GET /tapi/sync/auth/qr`** (already used, Manager.cs:2110) | Returns base64 PNG in `detail`; can instead return `detail:"2fa"` if the account has a cloud password |
| Auth phone/code | WA `auth/code` = Wappi GIVES a pairing code to type INTO WhatsApp | TG: `POST auth/phone {phone}` → `detail:"waiting for code"`; `POST auth/code {auth_code}` → `detail:"auth_success"` **or `detail:"2fa"`**; then `POST auth/2fa {pwd_code}` → `detail:"auth_success"` | Semantics inverted vs WA (Telegram sends the code to the user; user types it into OUR app). Already implemented (Manager.cs:2225,2314) except the 2FA branch (see §9) |
| Status / profile | `get/status`, `profile/add|delete|logout|restart|all/get` | All exist, same shapes as the orphan-sweep already relies on (`authorized` bool, `authorized_at`, `logouted_at`, `deleted_at`, `platform:"tg"`) | Parity — Manager.cs:2428/2654/2698 already call these |
| Webhooks | `webhook/url/set`, `webhook/types/set` | `POST /tapi/webhook/url/set?profile_id&url[&auth]`, `POST /tapi/webhook/types/set` (JSON array body), url/get, url/delete | Parity — CreateTelegramWorkflow.json already calls both (nodes "Set Wappi Webhook"/"Set Wappi Webhook Types") |
| TG-only extras | — | `message/edit`, `auth/2fa`, `stories/send|getViews`, `gifts/send|get`, groups CRUD, proxy set/get/delete, `chats/days/get` is_deleted filter | Available if ever needed |
| WA-only (no tapi twin) | `messages/all/get`, `messages/search`, `chat/delete|archive|unarchive`, `chat/typing/stop`, `contact/info`, `contact/check`, `poll/create`, buttons/list send, `status/set` | — | Features built on these can't port |

## 2. Chat id format, groups, fromMe

- **Private chat id = bare numeric Telegram user id as a string** — e.g. `"id": "89323786"`, `chatId: "89323786"` in messages. **No `@c.us` suffix, not a phone number.** `messages/get?chat_id=` also accepts a phone number or username (docs: "1347563 - id чата, 79115556677 - номер телефона, username - юзернейм").
- **Group id** = numeric too (`"id": "4127433587"` with `type: "chat"`); channels presumably `type: "channel"` (not shown in examples — verify).
- **No `isGroup` field anywhere in tapi docs (0 occurrences)**. Group-ness is signaled by dialog `type: "user" | "chat"` plus which nested object is present: `user {…}` (Telegram user obj, capitalized keys: ID, AccessHash, FirstName, LastName, Username, Phone, Photo{PhotoID, StrippedThumb, DCID}, Premium…) vs `chat {…}` (Title, ParticipantsCount, Date…) + `participants: [{user_id, is_admin, is_creator, name, username, phone, is_bot, is_me, inviter_id, invite_timestamp}]`.
- **fromMe**: present as bool in sync `messages/get` / `messages/id/get` (same as WA). In **webhooks** the equivalent is `is_me` (snake_case) — no fromMe key there.
- `from`/`to` in messages are loose: they can be a phone-like string ("996507585526") or a **username** ("FL00D") — not guaranteed to equal chatId.

## 3. Chat-list dialog shape: tapi vs `ChatDialog.cs`

tapi dialog (from `chats/get`/`chats/filter`/`chats/days/get` examples, tg-docs.txt:2100-2482): `{ id, user{...}|chat{...}+participants[], unread_count, isDeleted, isArchived, isPinned, name, picture, thumbnail?, last_message_id, last_message_data, last_timestamp, last_message_sender{id,type,number,pushname,firstName,lastName,picture,thumbnail,username,isMe}, last_message_type, last_time, type, last_message_delivery_status, total_messages, is_wappi_pinned, is_wappi_archived, wappi_tags, wapi_unread_count }` + envelope `{status:"done", timestamp, time, detail:"Chats fetched", dialogs:[], total_count}`.

Vs `Assets/Scripts/Chat/ChatDialog.cs:1-19` (WA-shaped):
- `id` — WA `"79115576367@c.us"`; TG bare numeric, and **empty string `""` in the documented chats/filter example** (doc bug or real — verify).
- `isGroup` — **absent on tapi**; always deserializes false. Replace with `type=="chat"`.
- `name` — populated in WA and in TG days/get example; **empty `""` in TG chats/get + chats/filter user-dialog examples** (real name only in `user.FirstName/LastName/Username`, which JsonUtility can't reach without a new nested model with capitalized keys). Fallback also possible via `last_message_sender.pushname`.
- `thumbnail` — **exists on tapi** with the same `https://fs.wappi.pro/fs/downloadFile/{profile}/avatars/tumb_{id}.jpg` URL style as WA (tg-docs.txt:2457) — but only appears in the days/get example; absent from the chats/get and chats/filter example objects (verify presence).
- `last_timestamp` — **type swapped vs WA**: WA = RFC3339 string ("2023-03-22T14:02:32Z", wa-docs shows `0` when absent); TG = **unix-seconds number or null** (1779095802). TG puts the RFC3339 string in `last_time` (WA `last_time` is the unix number — mirror image). `ChatDialog.last_timestamp` is `string` and ChatManager.cs:286 does `DateTimeOffset.TryParse` on it.
- `last_message_type` — TG uses `"text"` (not `"chat"`); ChatManager.cs:314 compares `== "reaction"` (TG reaction-as-last-message representation undocumented).
- `last_message_sender` — shape is a superset of `ChatSender.cs` ({isMe, pushname} both present) — **compatible as-is**.
- `unread_count`, `isDeleted`, `isArchived`, `last_message_id`, `last_message_delivery_status` — same names; delivery_status empty string in all TG examples.

## 4. Message object shape: tapi vs `RawMessage.cs`

tapi `messages/get` message (tg-docs.txt:1102-1140): `{ id:"2910", type:"text", from, to, fromMe, senderName, time:1709456814 (unix sec), body:"Нрн" (plain string), stanzaId:"", chatId:"89323786", isForwarded, isReply, caption:"", delivery_status:"delivered", s3Info:{}, poll_votes, poll_options, poll_select_count }`.

tapi `messages/id/get` message adds: `contact_name, username, phone, file_name, isRead, mimetype, from_where("api"/"phone"), reply_message{...}, isEdited, isGif, isFromAPI, isDeleted, location, isPinned, reactions:null, wappi_bot_id, task_id, template_id, manager{}, crm_entities{}, is_blacklist, billable, attaches:null`.

Vs `Assets/Scripts/Chat/RawMessage.cs:1-32` — field names id/type/chatId/senderName/fromMe/time/caption/stanzaId/from/isReply/reply_message/delivery_status/body/s3Info all match. Divergences:
- **`type` enum**: TG text = `"text"`; WA text = `"chat"`. Documented TG types across sync+webhook examples: `text, image, video, document, ptt, audio, location, vcard`. **No `sticker` type documented for TG** (the only sticker-ish hint is `isGif`); video-notes (кружки) undocumented. `ChatManager.ParseMessageType` (ChatManager.cs:1613-1622) has no `"text"` case → **every Telegram text message → MessageType.Unknown → dropped** at ChatManager.cs:613/1253 (`if (norm.messageType == MessageType.Unknown) continue;`).
- **`media_info` does not exist on tapi** (0 occurrences in tg-docs; RawMessage.cs:30 binds it). Voice duration in TG webhooks is a flat `length_seconds`; sync-API voice duration source is undocumented. ChatManager.cs:1485-1486 reads `media_info.duration` → TG voice bubbles get duration 0.
- **Media body shape undocumented on the sync API**: WA media `body` is a JObject `{JPEGThumbnail, url, fileName, mimetype, fileLength, pageCount, caption, width, height}` (consumed at ChatManager.cs:1414-1530); the only TG media examples are webhook-shaped (body = base64 of the file! plus flat `file_link`, `file_name`, `mimetype`) and `messages/id/get` has flat `file_name`/`mimetype` plus an `attaches` field (null in example). **Whether tapi `messages/get` media messages carry s3Info.url / JPEGThumbnail / attaches is the #1 live-verification item.**
- `delivery_status` values: sync examples show `delivered`/`read`; delivery webhook enumerates `pending, delivered, read, undelivered, temporary ban, error`. `DeliveryTickFormatter.ParseWappiString` (DeliveryTickFormatter.cs:48-50) handles sent/delivered/read; unknown strings fall back to Sent via ChatManager.cs:1391 — benign but `pending`/`undelivered` would misrender as Sent.
- `time` unix seconds — same as WA. `poll_*` fields are TG-only additions (ignored by JsonConvert, harmless).

## 5. Replies, reactions, messages/id/get

- **Incoming reply**: `isReply: true` + `reply_message` snapshot exists on tapi (`messages/id/get` example, tg-docs.txt:1160-1190): `{id, body, type, chatId, timestamp, caption, contact_name, username, phone, attaches}`. Differences vs WA snapshot: uses **`timestamp`** (WA uses none/time), adds username/phone/attaches, **no `file_name`** key. `ReplyParser.FromSnapshot` (ReplyParser.cs:103-116) reads caption/body/contact_name — all present → largely compatible; the snapshot-echo recovery path can port because `messages/id/get` exists with identical params (QuotedMessageCache + ChatManager.QuoteResolve.cs:96).
- **Whether the echo bug (snapshot body == replying message's own body) exists on tapi is unknown** — WA-specific server quirk.
- **Sending a reply**: tapi `message/send` has **no documented `quoted_message_id`**; use `POST /tapi/sync/message/reply {body, message_id}`. App impact: ChatManager.cs:1935 + WappiSendTextRequest (ChatManager.cs:2052) need a channel branch.
- **Reactions**: send exists but body must include `recipient` (§1). **Receiving** reactions: WA delivers live `type:"reaction"` messages with `stanzaId` pointing at the target (ReactionStore pipeline, ChatManager.cs:1532); tapi has **no documented `reaction` message type** — instead `messages/id/get` shows a `reactions` field on the target message itself. The entire live-reaction ingestion model may differ (poll target messages vs reaction events). MUST VERIFY.

## 6. chat/delete + isDeleted on tapi

- **No `chat/delete` endpoint exists on tapi** (confirmed against the full 82-method list). Swipe-to-delete (`ChatManager.DeleteChat.cs:52`) cannot be ported as-is; TG-side options: hide locally only, or none.
- `isDeleted` **is present** in tapi dialogs, and `chats/days/get` even has an `is_deleted=true` filter, so Telegram-side deletions (made in the Telegram app) apparently DO surface — the semantic is the mirror image of WhatsApp (where only API deletions set the flag). ParseChatsJson's isDeleted skip (ChatManager.cs:274) would honor it automatically. Verify stickiness/revival behavior live.

## 7. Avatars

- **tapi provides avatars natively — GreenApiAvatarFetcher is unnecessary for Telegram** (and unusable: it posts WhatsApp `chatId` to Green API `getAvatar`, GreenApiAvatarFetcher.cs:22-25; TG numeric ids mean nothing there).
- Sources: (a) dialog `thumbnail` — same fs.wappi.pro URL scheme WA dialogs use and the same field `ChatViewModel` already consumes (ChatManager.cs:326); (b) `GET /tapi/sync/contact/get?recipient=` returns `contact.thumbnail`/`picture` ("в том числе аватара и его превью"); (c) webhook messages carry a `thumbnail` avatar URL per message (e.g. `https://fs.wappi.pro/t_6056.jpg`); (d) `user.Photo.StrippedThumb` (tiny base64) in the dialog user object.

## 8. Webhook payload (what n8n receives) — tg-docs.txt:3980-4360

Envelope identical to WA: `{ "messages": [ { ... } ] }` (note: the **delivery_status webhook example shows `messages` as an OBJECT, not array** — quirk). Incoming message fields: `id, profile_id, wh_type:"incoming_message", timestamp (RFC3339 string), time (unix sec), body, type, from, to, senderName, chatId, caption (nullable), from_where:"phone", contact_name, contact_phone, contact_username, username, is_forwarded, isReply, is_edited, stanza_id, is_me, chat_type:"user", thumbnail (avatar URL), picture, wappi_bot_id, is_deleted, is_bot` + media extras `file_link, file_link_expire, file_name, mimetype, length_seconds (voice), location{}, contact{}`. Note the naming mix: webhook uses `is_me/is_forwarded/is_edited/stanza_id` (snake) while sync API uses `fromMe/isForwarded/isEdited/stanzaId`.

**Confirmed breaks in `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json`** (it is an unpatched WhatsApp_Bot clone):
1. Both "Input type" Switch nodes match `type == "chat"` (lines 169-171, 433) — TG sends `"text"` → every text message falls to fallback "Ask to Send Text". Media/ptt/document values ("ptt","image","document") match TG.
2. "Listening Pause" reads `messages[0].media_info.duration` (line 577) — TG has `length_seconds`, no media_info → expression resolves undefined.
3. All 4 Wappi HTTP nodes still point at WhatsApp: `api/sync/message/send` (line 23), `api/sync/message/mark/read` (line 268, with `mark_all` query param tapi doesn't document on that endpoint), `api/sync/chat/typing/start` (line 313, tapi name is `chats/typing/start`). `Download Audio` uses webhook `file_link` (line 555) — fine on TG.
4. Group guard `If from == chatId` (lines 622-623): holds for TG private incoming (`from:"60227586" == chatId:"60227586"` in docs example) — but `from` can also be a username in sync payloads; webhook group examples aren't documented. Verify.
5. `CreateTelegramWorkflow.json` "Set Fields" node patches ONLY `name`, `nodes[0].parameters.path`, `nodes[5].parameters.options.systemMessage` — **the /api/→/tapi/ URLs are never rewritten**, so the fixes must land in the template itself. Mark-read body uses `messages[0].id` (short numeric TG id — fine).
6. Chat Memory session key `profile_id + ':' + from` (line 592): TG `from` may be username vs numeric id inconsistently → possible memory fragmentation; prefer `chatId`.

## 9. Auth / pairing quirks

- Flow (documented): `profile/add` → `auth/phone {phone}` (→ `detail:"waiting for code"`; Telegram delivers the code via the user's other TG session/SMS) → `auth/code {auth_code}` → `detail:"auth_success"` **or `detail:"2fa"`** → `auth/2fa {pwd_code}` → `auth_success`. QR alternative: `auth/qr` returns base64 PNG in `detail`, or `detail:"2fa"` when a cloud password is set.
- **App gap**: `Manager.SendTelegramCode` (Manager.cs:2352-2369) only recognizes `auth_success`; a `detail:"2fa"` 200 response shows "Authorization Failed". No `/tapi/sync/auth/2fa` call exists anywhere in the repo — 2FA-protected Telegram accounts (very common) cannot auth today.
- **No repeat-code cooldown is documented for tapi** (the WA ~2min pairing-code cooldown that forced the delete+recreate resend hack, Manager.GetWhatsappCode → RecreateWhatsappProfileForNewCode, is a WhatsApp-platform behavior). Telegram-side FLOOD_WAIT throttles on repeated code requests are a known platform behavior but not in Wappi docs — do NOT assume the recreate hack is needed; verify plain re-request first.
- `get/status` response includes `platform:"tg"`, `phone`, `authorized` bool, `webhook_types` — same shape the orphan sweep already consumes (Tools/n8n/workflows/2islisFH7jjLoPQM-Delete_Orphan_Profiles.json:92).

## 10. Parser impact — what breaks, by file:line

1. **ChatManager.cs:1613-1622 `ParseMessageType`** — no `"text"` case → Unknown → dropped at :613/:1253. THE headline break: Telegram chats would render empty. Also no TG mapping decision for `location`/`vcard` (currently → Unknown/dropped, acceptable) and sticker/`isGif` unknowns.
2. **ChatManager.cs:288** — `chat.id[..^5]` assumes 5-char `@c.us` suffix; on TG numeric ids it amputates the last 5 digits for any dialog with empty `name` (which per docs is the common user-dialog case), and **throws ArgumentOutOfRangeException on ids shorter than 5 chars / the documented empty-string id**.
3. **ChatManager.cs:286 + ChatDialog.last_timestamp (string)** — TG sends a unix number or null; JsonUtility won't bind it → unixTime stays 0 → chat ordering/time labels break. TG's RFC3339 equivalent is in `last_time`, a field ChatDialog doesn't declare.
4. **ChatDialog.isGroup** — absent on tapi; group chats undetectable. Need `type` field (+ optionally nested `chat`/`user` models; note nested Telegram objects use Capitalized keys and JsonUtility handles only exact-name public fields).
5. **ChatManager.cs:1414-1530 Normalize media branches** — body-as-JObject (`JPEGThumbnail`/`url`/`fileName`/`mimetype`/`fileLength`/`pageCount`), `s3Info.url`, `media_info.duration` are all WA-specific; TG sync media shape undocumented (webhook evidence suggests flat `file_name`/`mimetype`/`length_seconds` + possibly `attaches`). Every media type (image/video/voice/audio/document/sticker) needs live shape capture.
6. **ChatManager.cs:1932 `PostTextMessageRoutine`** — URL is hardcoded `/api/sync/`; recipient logic (`chatId.EndsWith("@c.us")`) passes TG numeric ids through unchanged (OK per docs — numeric id works when a dialog exists); `quoted_message_id` (ChatManager.cs:2052) silently ignored or rejected on tapi → replies must branch to `/tapi/sync/message/reply`.
7. **ChatManager.ReactionSend.cs:66-67** — must add `recipient` to the body and switch base URL; otherwise tapi rejects (recipient is required).
8. **ChatManager.cs:2023 mark-read** — `mark_all=true` query undocumented on tapi's mark/read; use body `{message_id}` (and/or `mark_all` on messages/get).
9. **ChatManager.DeleteChat.cs:52** — no tapi endpoint; disable swipe-to-delete for TG bots or make it local-only.
10. **ChatManager.cs:1532 + ReactionStore pipeline** — TG has no documented incoming `type:"reaction"` message; live reaction ingestion model unknown (target message `reactions` field instead). WA transport (messages/get live-only) may have no TG equivalent.
11. **GreenApiAvatarFetcher.cs** — WhatsApp-only by construction; TG avatars come free via dialog `thumbnail` (already wired through ChatManager.cs:326 → ChatViewModel) and `contact/get`.
12. **Manager.cs:2352 (auth code)** — add `detail:"2fa"` branch + new `auth/2fa` call; QR flow should also handle `detail:"2fa"`.
13. **WappiUnitySync.cs:31** — `messages/all/get` has no tapi twin (dev tool; low priority).
14. **DeliveryTickFormatter.cs:48-50** — optionally map `pending`→Pending, `undelivered`/`error`→Failed for TG.
15. **ReplyParser.cs** — snapshot fields compatible (caption/body/contact_name present on tapi); `reply_message.timestamp` (vs nothing on WA) ignored — fine.
16. **Templates** (`Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json`) — 6 concrete fixes listed in §8; `TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json` likely inherits the same systemMessage-only patching (unexamined in depth).

## 11. MUST VERIFY LIVE (docs could not settle — user-assisted verification step)

1. **tapi `messages/get` media message JSON** for each type — image, video, voice (ptt), audio, document, sticker, video-note (кружок), GIF: does `body` hold base64/JObject/string? Is there `s3Info.url` (example shows `s3Info:{}` on text)? Where do thumbnail, dimensions, duration, file name/size live (`attaches`? flat fields?)? This decides the whole Normalize port.
2. **Sticker + video-note + animated-emoji `type` strings** (not documented at all) and whether `isGif` marks GIFs sent as `video`.
3. **Incoming reactions transport**: does a reaction appear as a live message in `messages/get` (any `type` value?), only via `reactions` on the target in `messages/id/get`, or via webhook only? What does `stanzaId` carry on TG?
4. **Group/channel dialogs**: actual `type` values ("chat" vs "channel" vs "group"), group message `senderName`/`from` semantics, whether the `If from == chatId` n8n guard holds for TG groups, and whether `chat_id` for messages/get accepts the group numeric id.
5. **Dialog `name` and `thumbnail` presence in `chats/filter`** on a real account (docs examples show empty `name` for user dialogs in chats/get/filter but populated in days/get; `thumbnail` appears only in the days/get example). Also the documented `"id": ""` in the chats/filter example — real or doc bug?
6. **`last_timestamp` runtime type under JsonUtility** — confirm number→string binding yields empty (not an exception) and decide between `last_time` (RFC3339) or a long-typed field.
7. **`isDeleted` semantics on TG**: does deleting a chat in the Telegram app set it? Is it sticky? Does new activity revive the row?
8. **Reply snapshot echo bug** — does tapi `reply_message.body` ever echo the replying message's own text (the WA quirk ReplyParser guards against)?
9. **Resend-code behavior**: repeat `auth/phone` on the same profile — plain re-request OK, or FLOOD_WAIT/cooldown requiring the WA-style profile recreate? Also what error shape a wrong `auth_code` returns.
10. **Webhook payloads for TG groups and outgoing reply/reaction** (undocumented) + whether `delivery_status` webhook really sends `messages` as an object (breaks n8n `messages[0]` expressions if those types are enabled).
11. **`quoted_message_id` on tapi `message/send`** — try it once; if silently accepted the send path needs no branch (docs say nothing).
12. **`chats/typing/start` effect duration** and whether the missing `typing/stop` matters for the bot's humanization flow.
13. **`mark_all=true` on tapi `messages/get`** — confirm it marks the chat read (docs say private-chats-only) as the replacement for WA's mark/read?mark_all.
