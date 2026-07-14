---
phase: 05-channel-aware-chatmanager-core
verified: 2026-07-14T07:03:41Z
status: human_needed
score: 45/45 must-haves verified (5 ROADMAP success criteria + 40 plan-level truths across all 6 plans)
overrides_applied: 0
human_verification:
  - test: "Owner media re-run — unobserved Telegram media types (SHAPES.md Q2)"
    expected: "Sticker/voice(ptt)/video-note/GIF either already map correctly via the shipped defensive audio/*→Voice, video/*→Video prefix rules, or reveal a new type string that needs an explicit case; no silent Unknown-drop for a type the owner actually receives"
    why_human: "Requires a live authorized Telegram account physically sending each media kind and a re-run of Tools/tapi/capture-shapes.sh — the 2026-07-13 capture observed 0 of these 4 types in 384 messages (SHAPES.md Q2), so the current handling is unverified defensive code, not a claimed shape"
  - test: "Telegram chat device UAT — send/receive parity checklist"
    expected: "Delivery tick transitions Pending→Sent on a TG reply; each media kind (image/video/document) sends and renders; opening an unread chat clears it (no mark_all body {message_id}); a reaction send/remove toggles correctly and survives the server echo; an incoming reply renders a quoted card"
    why_human: "Requires a live Wappi/Telegram round-trip against real tapi servers; several tapi response shapes are asserted only by construction (05-REVIEW IN-08) and cannot be exercised by EditMode tests"
  - test: "Telegram 2FA live round-trip (real cloud-password account)"
    expected: "detail:\"2fa\" switches both the code-entry and QR flows into the cloud-password prompt; a correct password authorizes via ShowAuthSuccess; a wrong password shows «Неверный пароль» and re-prompts; TelegramCodeInput is empty afterward on every path"
    why_human: "Requires a real Telegram account with Two-Step Verification enabled and a live tapi/sync/auth/2fa exchange; the 05-05 SUMMARY explicitly carries this as a Phase-8 device-UAT item — TelegramAuthResponseParser's classification logic is unit-tested but the server round-trip is not"
  - test: "vthumb cache-key id-ambiguity probe (05-06-REVIEW WR-02)"
    expected: "message/media/download and messages/id/get resolve the correct video bytes per message even when two TG dialogs on the same profile have overlapping short numeric message ids (e.g. a channel post and a private chat); the TG-namespaced vthumb://tg/{profileId}/{chatId}/{messageId} client cache key already prevents client-side cross-painting (unit-tested) but server-side by-id disambiguation is unverified"
    why_human: "Telegram message ids are 1-5 digit per-dialog counters (confirmed in the capture) so collisions are plausible; probing requires live tapi requests against two real dialogs with colliding ids, which a read-only capture script cannot do (mutating requests are forbidden by that script's design)"
---

# Phase 5: Channel-Aware ChatManager Core Verification Report

**Phase Goal:** A Telegram-authed bot has a fully working in-app chat client at WhatsApp parity — list, paginated history, media, send, quoted replies, reactions-send, mark-read, cache isolation — plus the Telegram 2FA auth fix, all on the new channel seam with every existing WhatsApp behavior unchanged.
**Verified:** 2026-07-14T07:03:41Z
**Status:** human_needed
**Re-verification:** No — initial verification (all six plans, final pass after 05-06 landed 2026-07-14)

## Method

Structural verification only, per the launching agent's HARD RULE (no network calls, no secrets, no scene builders, no capture scripts run). Every must-have below was checked by reading the shipped source and grepping for the exact contracts declared in each plan's frontmatter, cross-referenced against the two code-review passes (`05-REVIEW.md`/`05-REVIEW-FIX.md` and `05-06-REVIEW.md`) to confirm every accepted fix actually landed in the code (not just documented as fixed).

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria — the contract)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Owner sees a Telegram bot's chat list (names, avatars, unread counts, correct timestamps via `last_time` fallback), opens any chat, reads paginated history — text renders via `type:"text"`, numeric ids never slice/crash | ✓ VERIFIED (structural) | `ChatDialogTime.Resolve` (last_timestamp→last_time fallback), `MessageTypeParser.From("text")→Chat`, `ChatIdFormat.DisplayFallback` retires `chat.id[..^5]` (grep confirms zero occurrences left), all 8 read-path URLs on `WappiEndpoints.Sync(ActiveChannel,...)`. Device-observable rendering is item 2 of Human Verification. |
| 2 | Telegram media (image/video/voice/document, plus sticker/GIF per capture verdicts) renders with thumbnails/durations/downloads; owner can send text, media, quoted reply (`message/reply`), send/remove reactions (recipient-required body) | ✓ VERIFIED (structural, scoped to capture verdicts) | `TelegramMediaShape.Resolve` + `ApplyTelegramMediaShape` (download-by-id, media_info dims/duration); `WappiSendReplyRequest`→`message/reply`; `WappiSendReactionRequest.recipient` (NullValueHandling.Ignore, TG-only). Sticker/GIF were 0-observed in the capture (SHAPES.md Q2) — the roadmap text itself scopes this "per the capture verdicts", and the verdict is `not-observed`; defensive-only handling shipped. See Human Verification item 1. |
| 3 | Opening an unread TG chat marks it read (no `mark_all`); incoming TG replies render quoted cards; swipe-to-delete hidden on TG, WA unchanged | ✓ VERIFIED | `MarkChatAsRead` channel branch drops `mark_all` on TG (`ChatManager.cs:2150-2155`); `ReplyParser` Q8-locked regression test (no echo bug on tapi); `ActiveChannelSupportsChatDelete => ActiveChannel==WhatsApp` early-returns `DeleteChat` with no server call. |
| 4 | WA/TG caches isolated (`BotCache/{botId}/` vs `.../telegram/`), each channel opens offline from its own cache, purge/privacy clears cover both, existing suite stays green | ✓ VERIFIED | `GetCacheRoot()` branches on `ChannelCachePath.SubDir(ActiveChannel)`; `PurgeCacheForBot` recurses `BotCache/{botId}` (covers both, verified by `ChannelCacheRootTests`). Suite: **966/966 green**, superset of the roadmap's 787 baseline. |
| 5 | 2FA-protected TG account authorizes via cloud-password step (`detail:"2fa"`→`tapi/sync/auth/2fa`) in both code and QR flows | ✓ VERIFIED (structural) | `TelegramAuthResponseParser.IsTwoFactor`/`IsAuthSuccess` (12 unit tests); `SendTelegramCode` + `OpenTelegramQRPanel` both divert to `EnterTelegram2faMode`; `SubmitTelegram2fa` POSTs `tapi/sync/auth/2fa` with `JsonConvert`-escaped body (WR-02 fix). Live server round-trip is Human Verification item 3. |

**Score:** 5/5 ROADMAP success criteria structurally verified.

### Must-Haves by Plan (all 6 plans — detailed)

<details>
<summary>40 plan-level truths, all VERIFIED — expand for full per-plan evidence</summary>

**05-01 (Foundations, 7/7 VERIFIED):** `ChatChannel{WhatsApp=0,Telegram=1}` (ChatChannel.cs) · `WappiEndpoints.Sync` api/tapi builder · `ChatIdFormat.DisplayFallback` never slices/throws (retires `chat.id[..^5]`) · `ChatIdFormat.IsGroup` both overloads · `OutboxEntry.channel` round-trips, default 0 (OutboxStore.cs:55) · `ChannelTabStateResolver` generalized, `WhatsAppTabStateResolver` wraps it (WhatsAppTabState.cs) · WhatsApp byte-identical / suite green.

**05-02 (Identity seam, 7/7 VERIFIED):** `ActiveChannel` public-read, persisted `{botId}ActiveChatChannel`, clamped on read (ChatManager.Channel.cs:18,34-38) · `SetActiveChannel` full reset choreography incl. WR-01/IN-01 post-review additions (`ClearResolveQueues()`, `currentChatId=null`, `_tgOwnUserId=null`) + `OnActiveChannelChanged` · `GetActiveProfileId` via `ProfileIdForChannel` + `IsValidProfileId` guard · `GetCacheRoot` WA legacy root vs `.../telegram/` (ChatManager.BotState.cs:22-31) · `ResolveChannelForBot`/`ChannelResolver` auto-selects the connected channel, persists correction · `BotHasNoTelegram` enum value + gated empty-state/sync-window (ChatManager.cs:2214; BotState.cs:206,233) · suite green.

**05-03 (URL wiring + parser divergences, 7/7 VERIFIED):** All 8 non-send URLs on `WappiEndpoints.Sync(ActiveChannel,...)` (grep: 8 in ChatManager.cs + 1 each in ReactionResolve/QuoteResolve/DeleteChat partials; 0 raw `wappi.pro/api/sync` literals remain in ChatManager.cs) · `MessageTypeParser.From("text")→Chat` · `chat.id[..^5]` retired (0 occurrences), `ChatIdFormat.DisplayFallback` wired at ChatManager.cs:299 · `ChatDialogTime.Resolve` last_time fallback, `ChatDialog.last_time`/`type` fields present · groupness via `ChatIdFormat.IsGroup` in both `ChatViewModel.cs:62` and `MessageListView.cs:522,789` (WR-03 suffix-guard fix present) · `ActiveChannelSupportsChatDelete` guard + early-return (DeleteChat.cs:18,29) · suite green.

**05-04 (Send-path branches, 6/6 VERIFIED):** `WappiSendReplyRequest{body,message_id}`→`message/reply` on TG reply, `message/send` otherwise (ChatManager.cs:2043-2196) · `WappiSendReactionRequest.recipient` NullValueHandling.Ignore, set only on TG (ReactionSend.cs:66-78,165-166) · mark-read channel branch drops `mark_all` on TG, keeps `mark_all=true` on WA (ChatManager.cs:2150-2155) · `WappiMediaRequestFactory.EndpointFor(kind,profileId,channel)` 3-arg + 2-arg WA back-compat (MediaSend.cs:263) · outbox snapshots `channel=(int)ActiveChannel` at send (MediaSend.cs:205), `RetryRoutine` rebuilds via `(ChatChannel)entry.channel` (Outbox.cs:85) · suite green.

**05-05 (Telegram 2FA fix, 6/6 VERIFIED):** `detail:"2fa"` → `EnterTelegram2faMode()` in `SendTelegramCode` (Manager.cs:2409-2413) · `SubmitTelegram2fa` POSTs `auth/2fa` `{pwd_code}` via `JsonConvert.SerializeObject` (WR-02 fix, Manager.cs:2479) + `UploadHandlerRaw`/`Content-Type`/`Authorization` headers · QR flow diverts via `ShowTelegram2faFromQr`→`EnterTelegram2faMode` before the base64 decode (Manager.cs:2129) · fail-closed re-prompt (`IsAuthSuccess` startswith-only) · zero `Debug.Log` of pwd_code/password (grep confirmed), `TelegramCodeInput.text=""` unconditional post-request (Manager.cs:2492), `EnterTelegram2faMode` early-returns on re-entry (IN-05 fix, Manager.cs:2445) · no new scene objects (panel reuse only), suite green.

**05-06 (Capture-gated media/reactions/reply, 7/7 VERIFIED):** SHAPES.md checkpoint genuinely resolved before implementation (only the vocabulary-legend line mentions "PENDING CAPTURE"; every Q1-Q8 + reactions-receive go/no-go line is a resolved verdict) · `TelegramMediaShape.Resolve` supplies file name/mime/size/duration/aspect from `media_info` + flat fields, download-by-id fallback for the missing inline URL (ApplyTelegramMediaShape, ChatManager.cs:1677) · `TelegramMediaType.Refine` (document+video/mp4→Video; defensive audio/*→Voice), wired via `ResolveMessageType` (ChatManager.cs:1664) · `ReplyParser` Q8-locked test (`TelegramSnapshot_RealQuotedBody_ResolvesAndDoesNotBlank`) confirms no code change needed (no echo bug observed) · reactions-receive built per the GO verdict: `TelegramReactionMapper.Map` + `TelegramReactionMerge.Merge` (identity-keyed via learned `_tgOwnUserId`, WR-01 fix) + `RefreshCachedMessageReactions` wired at both reconcile call sites, gated `ActiveChannel==Telegram` (ChatManager.cs:699,1268) · name/thumbnail + isDeleted: verdict-resolved as "no code change needed" (Q5/Q7 confirmed correct, not a stub) · WhatsApp byte-identical (every new branch gated on `ActiveChannel==Telegram`, confirmed at the Normalize call site ChatManager.cs:1566), suite green.

</details>

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Assets/Scripts/Chat/ChatChannel.cs` | `enum ChatChannel {WhatsApp=0,Telegram=1}` | ✓ VERIFIED | Exact match, load-bearing ordinal comment present |
| `Assets/Scripts/Chat/WappiEndpoints.cs` | `Sync(ChatChannel,path)` api/tapi builder | ✓ VERIFIED | Pure static, single home |
| `Assets/Scripts/Chat/ChatIdFormat.cs` | Recipient/DisplayFallback/IsGroup pure helpers | ✓ VERIFIED | Includes WR-03 suffix-guard + Q4 "channel" classification |
| `Assets/Scripts/Chat/OutboxStore.cs` | `OutboxEntry.channel` field | ✓ VERIFIED | `public int channel;` append-only region |
| `Assets/Scripts/Main/ChatManager.Channel.cs` | ActiveChannel + SetActiveChannel + OnActiveChannelChanged | ✓ VERIFIED | 152 lines incl. `ChannelResolver`/`ChannelCachePath` pure seams |
| `Assets/Scripts/Main/ChatManager.cs` | `BotHasNoTelegram`, WappiEndpoints wiring, ResolveMessageType/ApplyTelegramMediaShape | ✓ VERIFIED | All confirmed by line-anchored grep |
| `Assets/Scripts/UI/EmptyStateView.cs` | Telegram empty-state copy | ✓ VERIFIED | "Telegram not connected" / "Connect Telegram" present (English copy — IN-09 deferred, tracked pre-existing debt, not this phase's regression) |
| `Assets/Scripts/Main/ChatManager.DeleteChat.cs` | `ActiveChannelSupportsChatDelete` guard | ✓ VERIFIED | Early-return before any server call |
| `Assets/Scripts/Main/ChatManager.ReactionSend.cs` | TG `recipient` in reaction body | ✓ VERIFIED | `NullValueHandling.Ignore`, TG-only |
| `Assets/Scripts/Main/ChatManager.MediaSend.cs` | channel-aware `EndpointFor` + outbox snapshot | ✓ VERIFIED | 3-arg overload + `channel=(int)ActiveChannel` |
| `Assets/Scripts/Main/TelegramAuthResponseParser.cs` | pure detail classifier | ✓ VERIFIED | `ExtractDetail`/`IsTwoFactor`/`IsAuthSuccess`, never throws |
| `Assets/Scripts/Chat/TelegramMediaType.cs` | `Refine(baseType,mimetype)` | ✓ VERIFIED | Channel-scoped, never reclassifies Chat/Reaction |
| `Assets/Scripts/Chat/TelegramMediaShape.cs` | `Resolve(...)` media metadata | ✓ VERIFIED | AwayFromZero rounding (IN-01 fix confirmed) |
| `Assets/Scripts/Chat/TelegramReactionMapper.cs` | `Map(reactions,ownUserId)` | ✓ VERIFIED | Identity-aware (WR-01 fix confirmed) |
| `Assets/Scripts/Chat/TelegramReactionMerge.cs` | `Merge`/`SameReactions` | ✓ VERIFIED | Identity-keyed + 90s optimistic grace (WR-01 fix confirmed) |
| `VideoThumbKey` (in `ChatManager.VideoThumbs.cs`) | TG-namespaced vthumb cache key | ✓ VERIFIED | `vthumb://tg/{profileId}/{chatId}/{messageId}`; WA byte-identical legacy key; 4 tests (`ThumbnailKeyResolverTests.cs`) incl. both collision cases |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `WappiRecipient.cs` | `ChatIdFormat.Recipient` | delegation | ✓ WIRED | `FromChatId` body delegates |
| `WappiMediaRequestFactory.cs` | `WappiEndpoints.Sync` + `ChatIdFormat.Recipient` | 3-arg `EndpointFor` | ✓ WIRED | 2-arg back-compat delegates to 3-arg(WhatsApp) |
| `ChatManager.Channel.cs` | PlayerPrefs `{botId}ActiveChatChannel` | persist + defensive clamp read | ✓ WIRED | `ReadPersistedChannel` clamps non-{0,1} to WhatsApp |
| `ChatManager.BotState.cs` | `ActiveChannel` | `ProfileIdForChannel` switch + `ChannelCachePath.SubDir` | ✓ WIRED | Confirmed at GetActiveProfileId/GetCacheRoot/BeginLoadForActiveBot |
| `ChatManager.cs ParseChatsJson` | `ChatIdFormat.DisplayFallback` + `IsGroup` | display-name fallback + groupness at VM construction | ✓ WIRED | Line 299 + ChatViewModel constructor arg |
| `ChatManager.cs` (8 call sites) | `WappiEndpoints.Sync` | channel-aware chat/message/media/quote/resolve URLs | ✓ WIRED | 0 raw literals remain |
| `ChatManager.Outbox.cs RetryRoutine` | `entry.channel` | retry rebuilds URL from persisted channel | ✓ WIRED | `(ChatChannel)entry.channel` passed explicitly |
| `Manager.cs SendTelegramCode`/QR | `tapi/sync/auth/2fa` | `detail==2fa` → password mode → POST `pwd_code` | ✓ WIRED | Both flows confirmed |
| `Manager.cs` | `TelegramAuthResponseParser` | classify detail strings | ✓ WIRED | No fragile inline substring parsing remains |
| `ChatManager.cs Normalize` | captured tapi media/reaction shapes | `ResolveMessageType` + `ApplyTelegramMediaShape` + `TelegramReactionMapper` | ✓ WIRED | All TG-gated at the same `if (ActiveChannel==Telegram)` block |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|---------------------|--------|
| `MessageItemView` media rendering | `vm.fileName`/`mimeType`/`duration`/`aspectRatio` | `TelegramMediaShape.Resolve` → `NormalizedMessage` → `CreateViewModel` copy (ChatManager.cs:1372-1379) | Yes — real `media_info`/flat-field values, not hardcoded | ✓ FLOWING |
| Reaction pill (`MessageItemView`) | `vm.reactions` | `TelegramReactionMapper.Map(raw.reactions,_tgOwnUserId)` → `msg.reactions` (ChatManager.cs:1578) → `CreateViewModel` copy (line 1386, `null` for WhatsApp by design) | Yes — mapped from the live `reactions[]` array, gated TG-only | ✓ FLOWING |
| Chat list rows | `ChatViewModel.IsGroup`, display name, timestamp | `ChatIdFormat.IsGroup`/`DisplayFallback`, `ChatDialogTime.Resolve` at `ParseChatsJson` construction | Yes — computed per-dialog from the live `chats/filter` response | ✓ FLOWING |

No hollow props or disconnected data sources found — every TG-only computed field traced from its tapi JSON source through Normalize/CreateViewModel to the same rendering call sites WhatsApp already uses.

### Behavioral Spot-Checks

Step 7b: **SKIPPED** — no runnable entry points without the Unity Editor/a device, and the task's HARD RULE prohibits network calls and running capture scripts. The existing headless EditMode result (see below) is the closest available automated proxy and was checked for freshness instead.

**Headless test freshness check:**
- Last source-code commit: `442107d` (`fix(05): IN-02 …`) at `2026-07-14T11:49:47+05:00`.
- `Tools/test-output/results.xml` run: `start-time="2026-07-14 06:51:09Z"` = `11:51:09+05:00` — **after** the last source commit.
- `Tools/test-output/headless-summary.json`: `{"total":966,"passed":966,"failed":0,"inconclusive":0,"green":true}` — matches the expected 966/966.
- The one later commit (`d44d07e`, `11:53:53+05:00`) is confirmed docs-only (`git show --stat` touches only `.planning/STATE.md` + `05-06-REVIEW.md`).
- `git status --porcelain` on all phase-relevant paths is clean except this verification's own `.planning/REQUIREMENTS.md` edit (traceability fix, see below) — the shipped code is fully committed and the 966/966 result is current.

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|--------------|--------|----------|
| CHAT-01 | 05-01, 05-02, 05-03 | Chat list names/avatars/unread/timestamps | ✓ SATISFIED | `ChatDialogTime.Resolve`, `ChatIdFormat`, `ActiveChannel` wiring |
| CHAT-02 | 05-01, 05-03 | Paginated history, text renders, no crash on numeric ids | ✓ SATISFIED | `MessageTypeParser.From("text")`, `DisplayFallback` |
| CHAT-03 | 05-06 | Media renders with thumbnails/durations/downloads | ✓ SATISFIED (scoped to capture verdicts) | `TelegramMediaShape`/`TelegramMediaType`; sticker/GIF gated on the owner media re-run (Human Verification #1) |
| CHAT-04 | 05-01, 05-04 | Send text, outbox retry rebuilds URL | ✓ SATISFIED | `PostTextMessageRoutine` channel param, `RetryRoutine` |
| CHAT-05 | 05-01, 05-04 | Send media, channel-aware factory | ✓ SATISFIED | `WappiMediaRequestFactory.EndpointFor` 3-arg |
| CHAT-06 | 05-04 | Quoted reply via dedicated `message/reply` | ✓ SATISFIED | `WappiSendReplyRequest` |
| CHAT-07 | 05-06 | Incoming replies render quoted cards | ✓ SATISFIED | `ReplyParser` Q8 lock test |
| CHAT-08 | 05-01, 05-04 | Send/remove reactions, recipient-required body | ✓ SATISFIED | `WappiSendReactionRequest.recipient` |
| CHAT-09 | 05-04 | Mark-read, no `mark_all` on TG | ✓ SATISFIED | `MarkChatAsRead` channel branch |
| CHAT-10 | 05-03 | Swipe-to-delete hidden on TG | ✓ SATISFIED | `ActiveChannelSupportsChatDelete` — **traceability row was stale ("Pending"); fixed to "Complete" this pass** |
| CHAT-11 | 05-02 | WA/TG cache isolation | ✓ SATISFIED | `GetCacheRoot` channel subdir |
| TGAUTH-01 | 05-05 | 2FA cloud-password step, both flows | ✓ SATISFIED (client-side; live round-trip is Human Verification #3) | `TelegramAuthResponseParser`, `SubmitTelegram2fa` — **traceability row was stale ("Pending"); fixed to "Complete" this pass** |

No orphaned requirements: every ID declared across the six plans' frontmatter (`CHAT-01..11`, `TGAUTH-01`) matches exactly the set REQUIREMENTS.md maps to Phase 5 — 12/12, no extras on either side.

**REQUIREMENTS.md correction applied:** the Traceability table (lines 100-115) had `CHAT-10` and `TGAUTH-01` marked `Pending` while their checkbox items were already `[x]` and the code proves both are shipped and unit-tested (CHAT-10 since 05-03, TGAUTH-01 since 05-05). Reconciled both rows to `Complete` and updated the "Last updated" footer. `CHAT-03`/`CHAT-07` were already correctly marked `Complete` (no change needed there).

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| — | — | None found | — | Scanned all 29 production files touched across the 6 plans (TODO/FIXME/HACK/PLACEHOLDER/"not implemented"/"coming soon"/`NotImplementedException`) — zero hits. |

## Human Verification Required

The four items below are the only remaining work for this phase's goal — all are device/live-server dependent and were explicitly carried forward as such by the plans' own SUMMARYs and the two code-review passes (not newly discovered gaps).

### 1. Owner media re-run — unobserved Telegram media types (SHAPES.md Q2)

**Test:** On the dev Telegram profile, send a sticker, a voice message (ptt), a video-note (кружок), and a GIF to the bot, then re-run `Tools/tapi/capture-shapes.sh` and compare the recorded `type` strings / `isGif` / `is_round` fields against `TelegramMediaType.Refine` and `MessageTypeParser`.
**Expected:** Each type either already resolves correctly via the shipped defensive `audio/*`→Voice / `video/*`→Video mimetype-prefix rules, or the re-run reveals the real `type` string so an explicit case can be added — no silently-dropped-to-Unknown bubble for a type the owner's account actually receives.
**Why human:** The 2026-07-13 capture observed **zero** of these 4 types across 384 messages (SHAPES.md Q2, `not-observed`), so current handling is an unverified safety net, not a confirmed shape — resolving it requires physically sending each media kind through a live authorized account.

### 2. Telegram chat device UAT — send/receive parity checklist

**Test:** On a device with a Telegram-authed bot: (a) send text and a quoted reply, confirm the delivery tick transitions Pending→Sent; (b) send one media message of each kind (image/video/document); (c) open an unread chat and confirm it clears (no `mark_all`); (d) send and then remove an emoji reaction, confirming the pill toggles correctly through the server echo; (e) have someone else send a reply into the chat and confirm the quoted card renders.
**Expected:** All actions succeed with the tapi shapes the code assumes: `message/reply`'s response parses as `{status,message_id}` (tempId→realId swap), media endpoints exist symmetrically under `tapi/sync/`, mark-read's `{message_id}`-only body actually marks read. Also observe (not a bug if seen): the chat-list preview row does not show "X reacted…" for Telegram (poll transport has no per-reaction timestamp, WA-only feature), and removing your own reaction may flicker back for one refresh cycle before self-healing.
**Why human:** These are live-server response shapes asserted only by construction (05-REVIEW IN-08) — no mock/staging tapi exists, and the project's coroutine-based networking pattern is not unit-testable without a real round-trip.

### 3. Telegram 2FA live round-trip (real cloud-password account)

**Test:** Authorize a Telegram account with Two-Step Verification (cloud password) enabled, via both the code-entry flow and the QR flow. Enter the correct password once, and deliberately enter a wrong password once to confirm the re-prompt path.
**Expected:** `detail:"2fa"` switches both flows into the cloud-password prompt (RU copy: «Облачный пароль» / «Введите пароль от Telegram»); a correct password authorizes via the existing `ShowAuthSuccess` path; a wrong password shows «Неверный пароль» and re-prompts without crashing or looping; `TelegramCodeInput` is empty afterward on every path.
**Why human:** Requires a real 2FA-protected Telegram account and a live `tapi/sync/auth/2fa` exchange — the 05-05 SUMMARY explicitly carries this as a Phase-8 device-UAT item. `TelegramAuthResponseParser`'s classification is unit-tested (12 cases); the actual server exchange is not.

### 4. vthumb cache-key id-ambiguity probe (05-06-REVIEW WR-02)

**Test:** On the dev profile, send/receive videos with overlapping short numeric message ids across two different Telegram dialogs on the SAME profile (e.g. a channel post and a private chat — Telegram message ids are 1-5 digit per-dialog counters, so collisions are plausible). Confirm each video's downloaded bytes and thumbnail match its own source dialog, not the other one.
**Expected:** `message/media/download`/`messages/id/get` resolve the correct bytes per message even without a chat-id parameter on the request. The client-side collision (two videos painting the same cached thumbnail) is already prevented by the TG-namespaced `vthumb://tg/{profileId}/{chatId}/{messageId}` key (unit-tested, 4 cases) — this test is specifically about whether the **server** disambiguates the download-by-id call correctly; if it doesn't, the fix note suggests checking for a `chat_id` parameter on the download endpoint.
**Why human:** This is server-side tapi behavior that a read-only capture script cannot probe (the capture script forbids mutating requests) — it requires live sends into two real dialogs with colliding ids.

## Gaps Summary

No structural gaps found. All 45 must-haves across the ROADMAP's 5 success criteria and the six plans' frontmatter (40 plan-level truths) are verified present, substantive, and wired in the shipped code — including every fix accepted in both code-review passes (`05-REVIEW-FIX.md`: WR-01/WR-02/WR-03/IN-01/IN-05; `05-06-REVIEW.md`: WR-01/WR-02/IN-01/IN-02), each independently re-confirmed by reading the current source rather than trusting the review's own "Status: FIXED" claim. The 966/966 headless EditMode result is current (postdates the last source commit by ~90 seconds; the one later commit is docs-only). No anti-patterns, no orphaned requirements, no hollow data flow.

The phase's own design deliberately scoped four items to live-device/server verification rather than static analysis: three tapi-only response shapes that can't be exercised without a real round-trip (send-path parity, 2FA), one capture verdict that came back `not-observed` for four rarely-used media types, and one server-side id-disambiguation question the capture script is architecturally forbidden from probing (it never mutates state). None of these are code gaps — they're the correctly-identified boundary between what static/unit verification can prove and what only a live device pass can confirm, and all four were already flagged as such by the plans' own SUMMARYs and the code-review passes before this verification ran.

One documentation staleness was found and corrected: `REQUIREMENTS.md`'s Traceability table had `CHAT-10` and `TGAUTH-01` marked `Pending` despite their requirement checkboxes already being `[x]` and the code proving both complete. Fixed to `Complete` in this pass (see Requirements Coverage above); `CHAT-03`/`CHAT-07` were already correct.

---

_Verified: 2026-07-14T07:03:41Z_
_Verifier: Claude (gsd-verifier)_
