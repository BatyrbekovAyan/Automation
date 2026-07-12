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
- **VERDICT:** `PENDING CAPTURE`
- **Downstream impact:** Decides the whole Normalize port —
  `ChatManager.cs:1414-1530` media branches (`body`-as-JObject, `s3Info.url`,
  `media_info.duration`) (**CHAT-03**). Voice duration source feeds `TPL-03`.

### 2. Sticker + video-note + animated-emoji `type` strings

- **Question:** What `type` strings do stickers, video-notes (кружки), and
  animated emoji use (none documented)? Does `isGif` mark GIFs that arrive as
  `type:"video"`?
- **Evidence:** `INDEX.json` key `"2"` → `message_type_*.json`.
- **VERDICT:** `PENDING CAPTURE`
- **Downstream impact:** `ChatManager.cs:1613-1622` `ParseMessageType` needs a
  `"text"` case + any new media `type` cases, else those messages map to
  `MessageType.Unknown` and are dropped (**CHAT-03**).

### 3. Incoming reactions transport

- **Question:** Does an incoming reaction appear as a live message in
  `messages/get` (which `type`?), only via a `reactions` field on the target in
  `messages/id/get`, or via webhook only? What does `stanzaId` carry on TG?
- **Evidence:** `INDEX.json` key `"3"` → `message_id_reactions.json`,
  `message_id_full.json`, `messages_*.json`.
- **VERDICT:** `PENDING CAPTURE`
- **Downstream impact:** `ChatManager.cs:1532` + the ReactionStore pipeline —
  determines whether receive-side reactions are buildable on TG. Feeds the
  **Reactions-receive go/no-go** section below and the v2 **TG-REACT-RECV**
  requirement (send-side is **CHAT-08**, separate).

### 4. Group / channel dialogs

- **Question:** Actual `type` values (`chat` vs `channel` vs `group`), group
  `senderName`/`from` semantics, whether the n8n `If from == chatId` guard holds
  for TG groups, and whether `messages/get` `chat_id` accepts a group numeric id.
- **Evidence:** `INDEX.json` key `"4"` → `chats_get.json`, `chats_filter.json`,
  `chats_days_get.json`, `messages_*.json`.
- **VERDICT:** `PENDING CAPTURE`
- **Downstream impact:** `ChatDialog.isGroup` is absent on tapi → groupness via
  `type=="chat"` (**CHAT-01**); group guard affects the template (Phase 4).

### 5. Dialog `name` / `thumbnail` presence in chats/filter

- **Question:** On a real account, are `name` and `thumbnail` populated in
  `chats/filter` (docs show empty `name` in chats/get+filter but populated in
  days/get; `thumbnail` only in the days/get example)? Is the documented
  `"id": ""` in the chats/filter example real or a doc bug?
- **Evidence:** `INDEX.json` key `"5"` → `chats_filter.json`, `chats_get.json`,
  `chats_days_get.json`, `contact.json`.
- **VERDICT:** `PENDING CAPTURE`
- **Downstream impact:** `ChatManager.cs:288` numeric-id slice
  (`chat.id[..^5]`) throws / corrupts on empty or short ids (**CHAT-02**); name
  and avatar fallbacks feed the chat list (**CHAT-01**).

### 6. `last_timestamp` runtime type under JsonUtility

- **Question:** Confirm the number→string binding of `last_timestamp` yields an
  empty string (not an exception) under JsonUtility, and decide between
  `last_time` (RFC3339) or a long-typed field for ordering.
- **Evidence:** `INDEX.json` key `"6"` → `chats_get.json`, `chats_filter.json`.
- **VERDICT:** `PENDING CAPTURE`
- **Downstream impact:** `ChatDialog.last_timestamp` (string) + `ChatManager.cs:286`
  `DateTimeOffset.TryParse` — wrong type → time labels/ordering break
  (**CHAT-01**).

### 7. `isDeleted` semantics on TG

- **Question:** Does deleting a chat in the Telegram app set `isDeleted`? Is it
  sticky? Does new activity revive the row?
- **Evidence:** `INDEX.json` key `"7"` → `chats_get.json`, `chats_days_get.json`.
  (Semantics also need a manual before/after observation — delete a test chat in
  the TG app, re-run, compare.)
- **VERDICT:** `PENDING CAPTURE`
- **Downstream impact:** `ChatManager.cs:274` `ParseChatsJson` `isDeleted` skip;
  no tapi `chat/delete` → swipe-to-delete is hidden on TG (**CHAT-10**).

### 8. Reply snapshot echo bug

- **Question:** Does tapi `reply_message.body` ever echo the replying message's
  own text (the WhatsApp server quirk `ReplyParser` guards against)?
- **Evidence:** `INDEX.json` key `"8"` → `message_id_reply.json`, `messages_*.json`.
- **VERDICT:** `PENDING CAPTURE`
- **Downstream impact:** `ReplyParser.cs` + `ChatManager.QuoteResolve.cs:96`
  recovery path (fetch original via `messages/id/get`) (**CHAT-07**).

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

**VERDICT:** `PENDING CAPTURE`

**Evidence:** `samples/message_id_reactions.json`, `samples/message_id_full.json`
(the `reactions` field shape), and any reaction-typed rows in `messages_*.json`.

---

*Questions verbatim from `.planning/research/telegram-parity/tapi-shapes.md` §11.*
*Ships pre-filled; the owner's capture run + verdict pass closes Phase 3.*
