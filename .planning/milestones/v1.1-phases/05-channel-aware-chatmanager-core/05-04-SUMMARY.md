---
phase: 05-channel-aware-chatmanager-core
plan: 04
subsystem: api
tags: [unity, csharp, wappi, tapi, telegram, chat-pipeline, send-path, reply, reaction, mark-read, outbox, tdd]

# Dependency graph
requires:
  - phase: 05-01
    provides: "WappiEndpoints.Sync, ChatIdFormat.Recipient, ChatChannel enum, WappiMediaRequestFactory.EndpointFor(3-arg), OutboxEntry.channel"
  - phase: 05-02
    provides: "ChatManager.ActiveChannel + channel-aware GetActiveProfileId/GetCacheRoot"
  - phase: 05-03
    provides: "8 read-path URLs already routed through WappiEndpoints.Sync (send-path literals deliberately left for this plan)"
provides:
  - "PostTextMessageRoutine is channel-aware: Telegram reply => tapi message/reply {body, message_id} (no recipient); WhatsApp + Telegram non-reply => message/send via WappiEndpoints.Sync"
  - "WappiSendReplyRequest DTO (serializes to exactly {body, message_id})"
  - "MarkChatAsRead drops mark_all on Telegram, keeps mark_all=true on WhatsApp; body {message_id} identical"
  - "WappiSendReactionRequest gains optional recipient (NullValueHandling.Ignore); Telegram reaction carries ChatIdFormat.Recipient(chatId), WhatsApp byte-identical"
  - "PostMediaMessageRoutine resolves the media endpoint via 3-arg EndpointFor(kind, profileId, (ChatChannel)entry.channel)"
  - "Text + media outbox entries snapshot channel=(int)ActiveChannel at send time; text retry rebuilds the URL from (ChatChannel)entry.channel"
  - "Last two api/sync literals in ChatManager.cs (message/send, message/mark/read) + the reaction literal now channel-aware — zero hardcoded api/sync in the 4 send-path files"
affects: [05-06, phase-6-channel-switcher]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Send-path URLs routed through WappiEndpoints.Sync(channel, ...) — channel is ActiveChannel for fresh sends, (ChatChannel)entry.channel for retries"
    - "Additive TG-only body fields via [JsonProperty(NullValueHandling.Ignore)] keep the WhatsApp wire byte-identical (mirrors WappiSendTextRequest.quotedMessageId precedent)"
    - "Endpoint branch (message/reply vs message/send) selected inside the shared post routine so success/failure reconcile stays single-sourced"
    - "Optional defaulted channel param (ChatChannel channel = WhatsApp) keeps every caller compiling per-commit while the retry site is wired in a later task"

key-files:
  created:
    - Assets/Tests/Editor/Chat/TelegramReplyRequestTests.cs
    - Assets/Tests/Editor/Chat/TelegramReactionRequestTests.cs
    - Assets/Tests/Editor/Chat/OutboxRetryChannelTests.cs
  modified:
    - Assets/Scripts/Main/ChatManager.cs
    - Assets/Scripts/Main/ChatManager.ReactionSend.cs
    - Assets/Scripts/Main/ChatManager.MediaSend.cs
    - Assets/Scripts/Main/ChatManager.Outbox.cs

key-decisions:
  - "DTO test files were created in their behavior-task (RED-first) rather than all deferred to Task 3, honoring tdd=true and matching the Task 1/Task 2 verify filters (TelegramReply / TelegramReaction) exactly."
  - "PostTextMessageRoutine gained an optional ChatChannel param defaulting to WhatsApp so the Outbox retry call still compiled in Task 1; Task 3 then passed (ChatChannel)entry.channel explicitly."
  - "Task 3's retry wiring has no unit-level RED seam (private coroutine); OutboxRetryChannelTests locks the pure channel->base contract the retry relies on, and the full-suite green is the integration gate."

patterns-established:
  - "Pattern 1: send-path URLs are channel-aware via WappiEndpoints.Sync; WhatsApp resolves byte-identically to the retired literal so the existing suite is the regression net."
  - "Pattern 2: tapi-only request-body divergences (reply endpoint, reaction recipient) are additive NullValueHandling.Ignore fields, never a second DTO — WhatsApp serialization is unchanged."

requirements-completed: [CHAT-04, CHAT-05, CHAT-06, CHAT-08, CHAT-09]

# Metrics
duration: 9min
completed: 2026-07-12
---

# Phase 5 Plan 04: Send-Path Channel Branches Summary

**Telegram send parity: quoted replies route to tapi message/reply, reactions carry the required recipient, mark-read drops mark_all, media uses the channel-aware EndpointFor, and outbox entries snapshot the channel so cross-session retries rebuild the right base — WhatsApp byte-identical, full EditMode suite 888/888 green.**

## Performance

- **Duration:** ~9 min
- **Started:** 2026-07-12T18:38:29Z
- **Completed:** 2026-07-12T18:47:42Z
- **Tasks:** 3 (all TDD: RED test → GREEN feat)
- **Files modified:** 7 (3 created, 4 modified)

## Accomplishments
- The send path is channel-aware end to end. A Telegram quoted reply POSTs to the dedicated `tapi/sync/message/reply` endpoint with `{body, message_id}` (no recipient, no `quoted_message_id`); a Telegram non-reply and every WhatsApp send keep `message/send`.
- A Telegram reaction now includes the tapi-required `recipient` (via `ChatIdFormat.Recipient(chatId)`); the WhatsApp reaction body stays `{body, message_id}` byte-identical because `recipient` serializes only when set.
- Mark-read is channel-branched: Telegram drops the `mark_all` query, WhatsApp keeps `mark_all=true`; body `{message_id}` is identical on both.
- Media send resolves its endpoint via the 3-arg `WappiMediaRequestFactory.EndpointFor(kind, profileId, (ChatChannel)entry.channel)`, so a Telegram media outbox entry hits the tapi img/video/document endpoints.
- Outbox durability is channel-correct: both text and media entries snapshot `channel=(int)ActiveChannel` at send time; a text retry rebuilds the URL from `(ChatChannel)entry.channel` (legacy entries with `channel==0` default to WhatsApp).
- The last two `wappi.pro/api/sync` literals in `ChatManager.cs` (`message/send`, `message/mark/read`) and the `message/reaction` literal in `ChatManager.ReactionSend.cs` are retired — zero hardcoded api/sync in the four send-path files.

## Task Commits

TDD tasks carry a RED test commit → GREEN feat commit:

1. **Task 1: Text send + reply endpoint branch + mark-read body branch (channel-aware)**
   - `c530b8a` (test) — failing WappiSendReplyRequest serialization tests (compile-error RED)
   - `e6a0463` (feat) — reply endpoint branch, channel-aware send/mark-read, outbox channel snapshot; 5/5 targeted green
2. **Task 2: Reaction recipient (Telegram) + media EndpointFor channel + outbox channel snapshot**
   - `7fc6dde` (test) — failing Telegram reaction recipient tests (compile-error RED)
   - `fad0ea5` (feat) — reaction recipient + channel-aware reaction/media URLs + media outbox channel snapshot; 23/23 targeted green
3. **Task 3: Outbox retry channel rebuild + full suite green**
   - `29f9ae5` (feat) — RetryRoutine passes (ChatChannel)entry.channel + OutboxRetryChannelTests contract lock; full suite 888/888

_No REFACTOR commits needed — GREEN implementations were already clean._

## Files Created/Modified
- `Assets/Scripts/Main/ChatManager.cs` (modified) — `WappiSendReplyRequest` DTO; `PostTextMessageRoutine` channel param + reply/send branch via `WappiEndpoints.Sync` + `ChatIdFormat.Recipient`; `SendTextMessageRoutine` snapshots channel + passes `ActiveChannel`; `MarkChatAsRead` channel-aware URL (Telegram drops `mark_all`).
- `Assets/Scripts/Main/ChatManager.ReactionSend.cs` (modified) — `WappiSendReactionRequest.recipient` (NullValueHandling.Ignore); `PostReactionRoutine` URL via `WappiEndpoints.Sync(ActiveChannel, ...)` + Telegram recipient.
- `Assets/Scripts/Main/ChatManager.MediaSend.cs` (modified) — media outbox entry `channel=(int)ActiveChannel`; `PostMediaMessageRoutine` 3-arg `EndpointFor(..., (ChatChannel)entry.channel)`.
- `Assets/Scripts/Main/ChatManager.Outbox.cs` (modified) — `RetryRoutine` passes `(ChatChannel)entry.channel` into the text retry.
- `Assets/Tests/Editor/Chat/TelegramReplyRequestTests.cs` (created) — reply DTO serializes to `{body, message_id}`; no recipient/quoted_message_id.
- `Assets/Tests/Editor/Chat/TelegramReactionRequestTests.cs` (created) — reaction omits recipient (WhatsApp) / includes it (Telegram); empty body removal.
- `Assets/Tests/Editor/Chat/OutboxRetryChannelTests.cs` (created) — channel→base mapping the retry relies on (api for 0, tapi for 1, legacy-missing => api, tapi reply endpoint).

## Decisions Made
- **RED-first per behavior-task.** The plan's `tdd=true` on all three tasks vs. all test files listed under Task 3 was reconciled by creating each DTO test in the task that implements its behavior (TelegramReply in Task 1, TelegramReaction in Task 2), which also matches the Task 1/Task 2 verify filters exactly. `OutboxRetryChannelTests` was created in Task 3.
- **Optional defaulted channel param.** `PostTextMessageRoutine(..., ChatChannel channel = ChatChannel.WhatsApp)` keeps the Outbox retry call site compiling in Task 1 (defaulting to WhatsApp, which is byte-identical) before Task 3 wires `(ChatChannel)entry.channel` explicitly. Minimal churn, per-commit compilable.
- **Reply branch inside the shared post routine.** The endpoint swap (message/reply vs message/send) and body swap live inside `PostTextMessageRoutine`, so the success/failure reconcile (id swap, cache update, `Outbox.RemoveAt`, `OnMessageStatusChanged`) stays single-sourced and identical for both branches.

## Deviations from Plan

### Filing-only (not behavioral): DTO test files created in their behavior-task, not all in Task 3

- **What:** The plan lists `TelegramReplyRequestTests.cs` and `TelegramReactionRequestTests.cs` under Task 3's `<action>`, but each was created RED-first in the task that implements its behavior (Task 1 / Task 2).
- **Why:** All three tasks are `tdd=true`; RED-first requires the failing test to exist in the same task as the implementation. This also matches the Task 1 verify filter (`TelegramReply|WappiSendText`) and Task 2 filter (`TelegramReaction|OutgoingReaction|WappiMediaRequestFactory`), which reference those files during those tasks.
- **Impact:** None on behavior or coverage. Task 3's acceptance (`test -f OutboxRetryChannelTests.cs && test -f TelegramReplyRequestTests.cs`) is satisfied — both exist by Task 3.

---

**Total deviations:** 1 filing-only. No Rule 1–4 auto-fixes were required; all three tasks landed on-contract.
**Impact on plan:** No scope creep; all `must_haves`, `artifacts`, and `key_links` satisfied. WhatsApp byte-identical.

## TDD Gate Compliance
- Task 1 and Task 2 each show a genuine compile-error RED (`test(...)` commit) → GREEN (`feat(...)` commit). RED evidence: `WappiSendReplyRequest` / `WappiSendReactionRequest.recipient` not found (headless exit 2).
- Task 3's retry wiring is a private coroutine with no unit-level seam, so its RED is not unit-reproducible. `OutboxRetryChannelTests` locks the pure `(ChatChannel)entry.channel → WappiEndpoints.Sync` contract the retry relies on (green-from-start contract lock), and the full-suite 888/888 green is the integration gate. Committed as a single `feat(...)`.

## Issues Encountered
None. TDD ran clean: both RED runs failed to compile on the missing type/field (exit 2); both GREEN runs and the full suite passed.

## Threat Register Coverage
- **T-0504-01 (Tampering, request bodies):** mitigated — reply/reaction/mark-read bodies serialized via `JsonConvert` (no string concatenation of untrusted values); recipient normalized through `ChatIdFormat.Recipient`; `message_id` is a server-issued stanza id.
- **T-0504-02 (Spoofing, base URL):** mitigated — every send URL built from the channel (`ActiveChannel` for fresh sends, `(ChatChannel)entry.channel` for retries) through `WappiEndpoints.Sync`; a Telegram profile_id never posts to the api base. `OutboxRetryChannelTests` + `WappiEndpointsTests` guard the mapping.
- **T-0504-03 (Information Disclosure, auth token):** accept — token stays in the `Authorization` header, never in the URL/query; no token logged.

## WhatsApp Regression Net
- WhatsApp send/reply/reaction/mark-read/media URLs and bodies are byte-identical: `WappiEndpoints.Sync(WhatsApp, path) == the retired literal`; `recipient`/`quoted_message_id` omitted via NullValueHandling.Ignore keeps WA bodies unchanged; `mark_all=true` preserved on the WhatsApp branch.
- `WappiSendTextRequestTests`, `OutgoingReactionTests`, `WappiMediaRequestFactoryTests` all still green.
- Full EditMode suite: **888/888 green** via `Tools/run-tests-headless.sh` (Editor closed), up from the 878 wave-3 baseline (+10: 3 reply + 3 reaction + 4 retry-channel).

## Known Stubs
None — every send-path branch is fully wired to a live tapi endpoint. Incoming-side Telegram media Normalize / reactions-receive remain the capture-gated final plan (05-06), out of scope here.

## User Setup Required
None — no external service configuration required.

## Next Phase Readiness
- Send parity is complete: a Telegram-authed bot can send text, media, quoted replies, reactions, and mark chats read at WhatsApp parity. Phase 6 (channel switcher UI) can drive `SetActiveChannel` and rely on the full send path.
- The capture-gated final plan (05-06: media Normalize, sticker/GIF strings, reactions-receive) remains BLOCKED on the owner's Phase-3 tapi capture run.

## Self-Check: PASSED

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-12*
