# tapi Live-Shape Verdicts

**Verdict record for the 13 open Wappi tapi (Telegram) shape questions** from
`.planning/research/telegram-parity/tapi-shapes.md` §11. These verdicts ground
all downstream Telegram parser / media work in **Phase 5** (and template work in
Phase 4) in real observed JSON instead of undocumented guesses.

**How to complete this file** (see `Tools/tapi/README.md`):
1. Run `Tools/tapi/capture-shapes.sh` against an authorized dev Telegram profile.
2. Raw samples land in the **gitignored** `Tools/tapi/samples/` (they may hold
   phone numbers / names — never committed). `samples/INDEX.json` maps each
   sample file to the question number it answers.
3. Open each sample, then set the **VERDICT** below to one of
   `confirmed shape` / `divergence` / `not-observed`, and (optionally) paste a
   fully-redacted mini-excerpt. This committed file must carry **structural
   verdicts + redacted excerpts only — NO raw PII**.

**Verdict vocabulary**
- `PENDING CAPTURE` — ships here; the owner's capture run resolves it.
- `confirmed shape` — the response matches the assumed/documented shape.
- `divergence` — the response differs; the downstream code must change.
- `not-observed` — no sample exercised this (e.g. no sticker in the account).
- `DEFERRED — ...` — cannot be settled by a read-only capture; resolved in a
  later phase. A recorded disposition, **not** a scope cut (VER-02).

---

### 1. messages/get media message JSON per type

- **Question:** For each media `type` (image, video, voice/ptt, audio, document,
  sticker, video-note, GIF): does `body` hold base64 / a JObject / a plain
  string? Is there `s3Info.url` (text shows `s3Info:{}`)? Where do thumbnail,
  dimensions, duration, file name and size live — `attaches`? flat fields?
- **Evidence:** `INDEX.json` key `"1"` → `message_type_*.json`, `messages_*.json`.
- **VERDICT:** `divergence` — **REVISED 2026-07-14 by the media re-run** (the
  first capture's `s3Info:{}` reading was an artifact of stale media; see below).
  - **`s3Info.url` IS populated for Wappi-hosted media** — a signed Yandex S3 URL
    (`https://wapi-uploads….storage.yandexcloud.net/…?X-Amz-…`) + `s3Info.expire`
    (unix; `X-Amz-Expires=172800` = 48h). Observed on 37/74 media messages
    (image/video/ptt/sticker/document all carry it when fresh). `s3Info` is `{}`
    **only after Wappi evicts the object** — presence tracks Wappi's S3 retention,
    NOT strictly message age (fresh URLs minted on demand, `X-Amz-Date` = capture
    time). `body` is the empty string `""` for media (not `null`; a JValue, not a
    JObject → the WA `body is JObject` branches correctly skip).
  - **`media_info` EXISTS on the tapi sync API**: `{width,height,size,duration,
    is_round,is_group,grouped_id}`; image = w/h/size, video/voice = `duration`.
  - Flat `mimetype` + `file_name` carry media identity; `caption` flat (`""` absent).
  - **Media kind = `type` refined by `mimetype`** (a phone video came as
    `type:"document"` + `video/mp4`).
  - Redacted image excerpt: `{"type":"image","body":"","attaches":null,
    "s3Info":{"url":"https://wapi-uploads….yandexcloud.net/….jpg?X-Amz-…","expire":1784296043},
    "media_info":{"width":320,"height":320,"size":18809,"duration":0,"is_round":false},
    "mimetype":"image/jpeg","file_name":""}`
- **Downstream impact:** Normalize port (**CHAT-03**) — the shipped 05-06 code is
  CORRECT against this: the **channel-agnostic `s3Info["url"]` reads**
  (ChatManager.cs:1476/1502/1510/1556, which run BEFORE the `ActiveChannel==Telegram`
  block) pick up the direct URL + `expire` for fresh media, and the serial
  `message/media/download`-by-id queue is the fallback for evicted (`s3Info:{}`)
  media; `expire` drives the existing refetch-by-id path (channel-aware via
  `WappiEndpoints.Sync`). `ApplyTelegramMediaShape` supplies dims/duration/name/mime
  from the flat fields. No inline `JPEGThumbnail` on TG (placeholder-first for
  evicted media only). NO code change needed — the "download-only" framing was
  over-stated from stale evidence.

### 2. Sticker + video-note + animated-emoji `type` strings

- **Question:** What `type` strings do stickers, video-notes (кружки), and
  animated emoji use (none documented)? Does `isGif` mark GIFs that arrive as
  `type:"video"`?
- **Evidence:** `INDEX.json` key `"2"` → `message_type_*.json`.
- **VERDICT:** **RESOLVED 2026-07-14 media re-run** — the owner sent the missing
  types; all now `confirmed shape` EXCEPT video-note (still not-observed). The type
  strings map correctly through the shipped `MessageTypeParser.From`:
  - **voice = `type:"ptt"`** + `mimetype:"audio/ogg"`, `media_info.duration` (sec)
    → `MessageType.Voice`. Same string as WhatsApp.
  - **video = `type:"video"`** + `mimetype:"video/mp4"` + `file_name:"video.mp4"`,
    `media_info` w/h/duration → `MessageType.Video`. (Video also still arrives as
    `type:"document"`+`video/mp4` when sent as a file — both handled.)
  - **sticker = `type:"sticker"`**: a GIF / video-sticker came as
    `type:"sticker"` + `mimetype:"video/mp4"` + `file_name:"mp4.mp4"` +
    **`isGif:true`** + `media_info` 320×180 → `MessageType.Sticker` (`isSticker=true`).
  - **animated `.tgs` sticker = `type:"document"`** + `mimetype:"application/x-tgsticker"`
    + `file_name:"AnimatedSticker.tgs"` → `MessageType.Document`.
  - **`type:"system"`** (service messages, `body` short text, no media) → Unknown →
    dropped (like `poll`). Acceptable v1.
  - **`is_round` never observed true** — no video-note (кружок) landed in the
    scanned window. Video-notes remain THE ONE unconfirmed shape (expected:
    `type:"video"` + `media_info.is_round:true`).
- **Downstream impact:** `MessageTypeParser.From` already maps
  `ptt→Voice / video→Video / sticker→Sticker / document→Document` (05-03/05-06) —
  **all observed types render correctly, no code change.** Accepted v1 cosmetic
  limits (not blocking): a video/mp4 sticker or GIF routes through the still-image
  sticker loader (may show a frame, not animate); a `.tgs` animated sticker renders
  as a document card. Still-open: send a **video-note (кружок)** + re-run to confirm
  the `is_round` bubble on device (**Phase 8 UAT**, minor).

### 3. Incoming reactions transport

- **Question:** Does an incoming reaction appear as a live message in
  `messages/get` (which `type`?), only via a `reactions` field on the target in
  `messages/id/get`, or via webhook only? What does `stanzaId` carry on TG?
- **Evidence:** `INDEX.json` key `"3"` → `message_id_reactions.json`,
  `message_id_full.json`, `messages_*.json`.
- **VERDICT:** `divergence` (26 reacted messages captured) — reactions are **state
  on the target message**, not live events: every `messages/get` message carries a
  `reactions` field, `null` when unreacted, else an array of
  `{"reaction":"👍","count":0,"user_id":"<numeric>","contact_name":"","type":"emoji"}`.
  **No `type:"reaction"` message rows exist** on tapi (0 in 384), and `stanzaId`
  is `""` throughout — the whole WA stanzaId/ReactionResolve transport does not
  apply. Refreshing the open chat's window via `messages/get` refreshes reaction
  state for free.
- **Downstream impact:** receive-side reactions on TG = map `RawMessage.reactions[]`
  → the existing per-message reaction state during Normalize (05-06); the WA
  ReactionStore live-event path and resolver queue stay WhatsApp-only.

### 4. Group / channel dialogs

- **Question:** Actual `type` values (`chat` vs `channel` vs `group`), group
  `senderName`/`from` semantics, whether the n8n `If from == chatId` guard holds
  for TG groups, and whether `messages/get` `chat_id` accepts a group numeric id.
- **Evidence:** `INDEX.json` key `"4"` → `chats_get.json`, `chats_filter.json`,
  `chats_days_get.json`, `messages_*.json`.
- **VERDICT:** `divergence` — THREE dialog types observed: `"user"` ×10,
  `"chat"` ×1 (group, has nested `chat` obj + `participants[]`), and
  **`"channel"` ×1** (a real third type the docs never showed; carries
  `participants[]`, no `user`/`chat` obj in days/get). `messages/get` accepts the
  group/channel numeric ids (both captured fine). Group message `from` = sender's
  numeric id ≠ `chatId`; private incoming `from == chatId`; **own outgoing
  (`fromMe:true`) messages carry `from` = own profile-user id ≠ `chatId` even in
  private chats** — the n8n `If from == chatId` guard holds for *incoming private*
  (the only case webhooks feed the bot) but e2e should confirm channels/groups
  never reach the agent.
- **Downstream impact:** `ChatIdFormat.IsGroup` currently treats only
  `type=="chat"` as group — **`"channel"` must also classify as group-ish**
  (sender headers, no per-chat suggestions) → 05-06 item. Template guard: verify
  in the TPL-06 e2e (**Phase 4/8**).

### 5. Dialog `name` / `thumbnail` presence in chats/filter

- **Question:** On a real account, are `name` and `thumbnail` populated in
  `chats/filter` (docs show empty `name` in chats/get+filter but populated in
  days/get; `thumbnail` only in the days/get example)? Is the documented
  `"id": ""` in the chats/filter example real or a doc bug?
- **Evidence:** `INDEX.json` key `"5"` → `chats_filter.json`, `chats_get.json`,
  `chats_days_get.json`, `contact.json`.
- **VERDICT:** `confirmed shape` for names / `divergence` for avatars —
  `name` **is populated in ALL three list endpoints** (the docs' empty-name and
  `"id":""` examples were doc bugs; all 12 dialogs carry non-empty numeric ids,
  shortest observed `777000` = 6 chars). BUT avatars are effectively absent:
  `thumbnail: null` + `picture: ""` on every dialog in every endpoint, and
  `contact/get` returned `picture: null`, `thumbnail: ""` too (`username` +
  `pushname`/`firstName` populated).
- **Downstream impact:** the `[..^5]` slice is already retired (05-03
  `DisplayFallback`) — names make the fallback rare anyway (**CHAT-02**). Avatars:
  do NOT rely on TG avatar URLs in v1 — the colored-initial default is the norm
  (**CHAT-01**); revisit in device UAT with photo-having contacts.

### 6. `last_timestamp` runtime type under JsonUtility

- **Question:** Confirm the number→string binding of `last_timestamp` yields an
  empty string (not an exception) under JsonUtility, and decide between
  `last_time` (RFC3339) or a long-typed field for ordering.
- **Evidence:** `INDEX.json` key `"6"` → `chats_get.json`, `chats_filter.json`.
- **VERDICT:** `confirmed shape` — `last_timestamp` is a **unix-seconds NUMBER**
  (e.g. `1783527365`) and `last_time` is the RFC3339 string
  (`"2026-07-08T16:16:05Z"`), the exact mirror of WhatsApp. JsonUtility leaves the
  string-typed `last_timestamp` field empty on the number → the shipped 05-03
  `ChatDialogTime.Resolve` fallback (try `last_timestamp`, else `last_time`) is
  the correct design, no change needed.
- **Downstream impact:** none further — already implemented + unit-tested
  (**CHAT-01** shipped).

### 7. `isDeleted` semantics on TG

- **Question:** Does deleting a chat in the Telegram app set `isDeleted`? Is it
  sticky? Does new activity revive the row?
- **Evidence:** `INDEX.json` key `"7"` → `chats_get.json`, `chats_days_get.json`.
  (Semantics also need a manual before/after observation — delete a test chat in
  the TG app, re-run, compare.)
- **VERDICT:** `not-observed` (field confirmed, semantics untested) — every
  dialog carries `isDeleted: false` / `isArchived: false`; no deleted chat existed
  in the account to observe stickiness/revival. The existing `ParseChatsJson`
  skip is safe either way (worst case: a TG-app-deleted chat keeps showing until
  Wappi flags it). Optional follow-up: delete a junk chat in the TG app, re-run
  the capture, diff.
- **Downstream impact:** `ParseChatsJson` `isDeleted` skip already channel-neutral;
  swipe-to-delete already hidden on TG (**CHAT-10** shipped).

### 8. Reply snapshot echo bug

- **Question:** Does tapi `reply_message.body` ever echo the replying message's
  own text (the WhatsApp server quirk `ReplyParser` guards against)?
- **Evidence:** `INDEX.json` key `"8"` → `message_id_reply.json`, `messages_*.json`.
- **VERDICT:** `confirmed shape`, no echo bug (28 reply messages captured) —
  `reply_message` = `{id, type, body, caption, chatId, contact_name, username,
  phone, timestamp, attaches}`; `body` is the QUOTED original's text (own-body ==
  snapshot-body was FALSE on every sampled reply), `contact_name` populated,
  `file_name` absent (as researched). `messages/id/get` works on tapi with the
  same `{status, message:{...}}` envelope the WA recovery parser expects.
- **Downstream impact:** `ReplyParser.FromSnapshot` maps cleanly; the WA
  echo-blank guard simply never fires on TG (harmless); QuoteResolve recovery
  path ports as-is (**CHAT-07** → 05-06 wiring only).

### 9. Resend-code behavior

- **Question:** Repeat `auth/phone` on the same profile — plain re-request OK, or
  FLOOD_WAIT / cooldown requiring the WhatsApp-style profile-recreate hack? What
  error shape does a wrong `auth_code` return?
- **Evidence:** `INDEX.json` deferred (not read-only). Resolve via device UAT.
- **VERDICT:** `DEFERRED — not observable via read-only capture; resolve in Phase 8 device UAT` — reason: requires live auth calls this script forbids.
- **Downstream impact:** `Manager.cs:2352` auth-code branch + the resend flow
  (**TGAUTH-01**).

### 10. Webhook payloads for TG groups + outgoing reply/reaction

- **Question:** Group webhook payload shape + outgoing reply/reaction webhooks;
  and whether the `delivery_status` webhook really sends `messages` as an object
  (not array), which would break n8n `messages[0]` expressions.
- **Evidence:** `INDEX.json` deferred (needs a live n8n tunnel + traffic).
- **VERDICT:** `DEFERRED — not observable via read-only capture; resolve in Phase 4 e2e (webhook observation)` — reason: webhook shapes need live inbound traffic.
- **Downstream impact:** `Telegram_Bot` template Switch / expressions
  (**TPL-01/TPL-02**).

### 11. `quoted_message_id` on tapi message/send

- **Question:** Is `quoted_message_id` silently accepted on tapi `message/send`,
  or must replies strictly branch to the dedicated `message/reply` endpoint?
- **Evidence:** `INDEX.json` deferred (send-side — this script never sends).
- **VERDICT:** `DEFERRED — not observable via read-only capture; resolve in Phase 5 send-path work` — reason: settling it requires a live send, which is mutating.
- **Downstream impact:** `ChatManager.cs:2052` reply send path — branch to the
  tapi `message/reply` endpoint (**CHAT-06**).

### 12. `chats/typing/start` effect duration

- **Question:** How long does `chats/typing/start` keep the indicator up, and does
  the missing `typing/stop` matter for the bot's humanizer flow?
- **Evidence:** `INDEX.json` deferred (send-side / stateful mutation).
- **VERDICT:** `DEFERRED — not observable via read-only capture; resolve in Phase 5 / Phase 4 template work` — reason: typing is a mutating call this script forbids.
- **Downstream impact:** Telegram_Bot template humanizer pause (**TPL** family).

### 13. `mark_all=true` on tapi messages/get

- **Question:** Confirm `mark_all=true` on `messages/get` marks the chat read
  (docs say private-chats-only) as the WhatsApp `mark/read?mark_all` replacement.
- **Evidence:** `INDEX.json` deferred — the capture script deliberately **never**
  passes `mark_all` (it would mutate the owner's real read state).
- **VERDICT:** `DEFERRED — not observable via read-only capture; resolve in Phase 5 / Phase 8 device UAT` — reason: confirming it requires performing the mutation.
- **Downstream impact:** `ChatManager.cs:2023` mark-read path (**CHAT-09**).

---

## Reactions-receive go/no-go

**Decision (feeds Phase 5 scope + the v2 `TG-REACT-RECV` requirement):** from the
Q3 evidence, was a viable tapi **receive-side** reaction transport observed?

- **GO** — a usable transport exists (live `messages/get` reaction message, or a
  pollable `reactions` field on the target) → build receive-side reactions in
  Phase 5.
- **NO-GO** — no viable transport (tapi documents no `type:"reaction"` message) →
  defer to the v2 requirement **TG-REACT-RECV**; incoming reactions stay out of
  v1.1. (Send-side reactions **CHAT-08** are unaffected — they ship regardless.)

**VERDICT:** **GO** (2026-07-13) — a pollable transport exists and is BETTER than
expected: `reactions[]` rides on every target message in the normal
`messages/get` window (26 live examples), so the open chat's existing refresh
loop delivers reaction updates with zero extra requests. Build receive-side
reactions in **05-06** as a Normalize mapping (`RawMessage.reactions[]` → the
per-message reaction state); the WA live-event/stanzaId transport stays
WhatsApp-only. v2 requirement **TG-REACT-RECV** is superseded (promoted into
05-06 scope).

**Evidence:** reaction-bearing rows in `samples/messages_*.json` (shape:
`[{"reaction":"<emoji>","count":0,"user_id":"<numeric>","contact_name":"","type":"emoji"}]`);
`samples/message_id_full.json` (`message.reactions` field on the by-id envelope).

---

*Questions verbatim from `.planning/research/telegram-parity/tapi-shapes.md` §11.*
*Ships pre-filled; the owner's capture run + verdict pass closes Phase 3.*
