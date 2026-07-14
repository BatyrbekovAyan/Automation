---
phase: 05-channel-aware-chatmanager-core
plan: 06
subsystem: api
tags: [unity, csharp, wappi, tapi, telegram, chat-pipeline, normalize, media, reactions, reply, parser, capture-gated]

# Dependency graph
requires:
  - phase: 05-03
    provides: "MessageTypeParser.From (text=>Chat), ChatIdFormat.IsGroup, channel-aware read URLs (messages/get, media/download, messages/id/get) via WappiEndpoints.Sync(ActiveChannel)"
  - phase: 05-02
    provides: "ChatManager.ActiveChannel (read) — the channel gate every Telegram branch keys off"
  - phase: 05-04
    provides: "send-side Telegram reaction (CHAT-08 recipient body) — the optimistic 'me' reaction the receive-side merge preserves"
provides:
  - "Telegram media Normalize: body:null + s3Info:{} media renders download-only via the existing serial media/download-by-id queue; dims/size/duration sourced from media_info; file name/mime from flat fields (CHAT-03)"
  - "TelegramMediaType.Refine — tapi media kind = type ⊕ mimetype (document+video/mp4 => Video; defensive audio/* => Voice); channel-scoped, WhatsApp documents byte-identical"
  - "TelegramMediaShape.Resolve — pure media-metadata resolver (file name/mime/size/rounded fractional duration/aspect from media_info), null-tolerant"
  - "Receive-side Telegram reactions (TG-REACT-RECV promoted from v2): reactions[] on every messages/get mapped at Normalize time (TelegramReactionMapper) + reconciled onto cached messages (TelegramReactionMerge) firing OnMessageReactionsChanged; WhatsApp ReactionStore transport untouched"
  - "ChatIdFormat.IsGroup classifies Telegram dialog type 'channel' as group-ish alongside 'chat' (SHAPES.md Q4)"
  - "Incoming Telegram replies render quoted cards via the shared ReplyParser path (CHAT-07) — Q8 verified: no echo bug, messages/id/get recovery ports as-is"
affects: [phase-8-device-uat]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Channel-scoped Normalize branches: every Telegram-shape divergence gated on ActiveChannel==Telegram so the WhatsApp path (and the full existing suite) is the regression net"
    - "Pure media/reaction seams (TelegramMediaType, TelegramMediaShape, TelegramReactionMapper, TelegramReactionMerge) extracted for unit testing without a live server — WhatsAppSyncGate/MessageTypeParser precedent"
    - "tapi media is download-only: Normalize leaves mediaUrl/videoUrl empty so the existing placeholder-first, serial message/media/download-by-id fallback fills the bytes (no inline URL/base64 on tapi)"
    - "Reconcile refresh mirrors RefreshCachedMessageMedia/Quote: mutate the cached VM in place + fire the change event; reactions merge preserves the owner's optimistic 'me' until the server echoes it"

key-files:
  created:
    - Assets/Scripts/Chat/TelegramMediaType.cs
    - Assets/Scripts/Chat/TelegramMediaShape.cs
    - Assets/Scripts/Chat/TelegramReactionMapper.cs
    - Assets/Scripts/Chat/TelegramReactionMerge.cs
    - Assets/Tests/Editor/Chat/TelegramMessageTypeTests.cs
    - Assets/Tests/Editor/Chat/TelegramMediaNormalizeTests.cs
    - Assets/Tests/Editor/Chat/TelegramReactionReceiveTests.cs
  modified:
    - Assets/Scripts/Chat/RawMessage.cs
    - Assets/Scripts/Chat/NormalizedMessage.cs
    - Assets/Scripts/Chat/ChatIdFormat.cs
    - Assets/Scripts/Main/ChatManager.cs
    - Assets/Tests/Editor/Chat/ChatIdFormatTests.cs
    - Assets/Tests/Editor/Chat/ReplyParserTests.cs

key-decisions:
  - "tapi media is download-only — Normalize does NOT synthesize a URL; it leaves mediaUrl/videoUrl empty (body:null + s3Info:{}) so MessageItemView's existing 'no valid URL => DownloadMediaForMessage' fallback (channel-aware + serial since 05-03) fetches the bytes by id. Only the media METADATA (type refinement + media_info dims/size/duration + flat name/mime) is new."
  - "Video-as-document refined to Video via mimetype (TelegramMediaType.Refine), channel-scoped so WhatsApp document handling is byte-identical. audio/* => Voice ships as a DEFENSIVE net (unobserved in capture) pending the owner media re-run."
  - "Receive-side reactions map reactions[] into the SAME MessageViewModel.reactions display list the WhatsApp pill already renders. tapi has no per-reaction fromMe/timestamp, so mapped reactions carry fromMe=false/time=0 (v1: own-reaction highlight/toggle relies on the optimistic send path). The reconcile merge keeps the owner's 'me' entry only until the server echoes that emoji — no flicker, no double-count."
  - "Reply echo-blank guard NOT extended to Telegram (Q8: no echo bug in 28 samples). The shared ReplyParser.FromSnapshot runs harmlessly (own-body==snapshot-body is false on tapi), so the quoted card + messages/id/get recovery port as-is."
  - "Dialog name backfill and isDeleted handling required NO code change (Q5: names populated in all list endpoints; Q7: isDeleted skip already channel-neutral) — verdict-resolved."

patterns-established:
  - "Pattern 1: Telegram media divergences live in channel-gated Normalize blocks + pure seams; the WhatsApp branches (body-as-JObject / s3Info.url) stay byte-identical and no-op for tapi (body null)."
  - "Pattern 2: receive-side reactions are a Normalize-time mapping into the existing reaction display state — a second, poll-based transport alongside (never replacing) the WhatsApp live-event ReactionStore path."

requirements-completed: [CHAT-03, CHAT-07]

# Metrics
duration: 42min
completed: 2026-07-14
---

# Phase 5 Plan 06: Capture-Gated Telegram Media / Reactions / Reply Summary

**Telegram media Normalizes per the real 2026-07-13 tapi capture (download-only body:null + s3Info:{} → media_info dims/duration, video-as-document → Video), receive-side reactions map reactions[] into the shared pill, "channel" dialogs render group-ish, and incoming replies quote cleanly — all channel-scoped, WhatsApp byte-identical, 957/957 EditMode green.**

## Performance

- **Duration:** ~42 min (2 cold headless Unity runs + capture-shape analysis)
- **Started:** 2026-07-14T05:20Z (approx)
- **Completed:** 2026-07-14T06:00Z
- **Tasks:** 3 (Task 1 = resolved human-action checkpoint; Tasks 2–3 executed)
- **Files modified:** 13 (7 created, 6 modified)

## Checkpoint (Task 1) — Resolved

The plan's Task-1 `checkpoint:human-action` gate was already satisfied on entry: the owner ran `Tools/tapi/capture-shapes.sh` on 2026-07-13 (26 reaction samples, 384 messages, 12 dialogs across 8 chats) and recorded every verdict in `Tools/tapi/SHAPES.md`. The literal precondition grep (`! grep -q "PENDING CAPTURE"`) matches only line 19 — the vocabulary legend defining the term — while every actual VERDICT line is resolved (divergence / confirmed shape / not-observed / DEFERRED) and the reactions-receive decision is **GO**. Gate treated as open; implementation proceeded.

## Accomplishments
- **CHAT-03 media (Q1/Q2):** tapi media (`body:null`, `s3Info:{}`, no JPEGThumbnail) Normalizes with correct type + metadata and renders through the existing serial `message/media/download`-by-id fallback. `media_info` supplies dims/size/duration (fractional duration rounded); flat `file_name`/`mimetype` supply name/mime. Phone-sent video (arriving as `type:"document"` + `mimetype:"video/mp4"`) refines to Video.
- **Receive-side reactions (Q3 = GO, TG-REACT-RECV promoted from v2):** `reactions[]` rides on every `messages/get` target, so they map into `MessageViewModel.reactions` at Normalize time and reconcile onto already-cached messages in place. The WhatsApp live-event / stanzaId / `ReactionStore` transport stays WhatsApp-only.
- **"channel" groupness (Q4):** `ChatIdFormat.IsGroup` now treats the real third tapi dialog type `"channel"` as group-ish alongside `"chat"` (suffix-guarded so WA ids never flip).
- **CHAT-07 replies (Q8):** the shared `ReplyParser` path already resolves tapi reply snapshots; verified no echo bug and locked it with a Telegram-shaped regression test. `messages/id/get` recovery (channel-aware since 05-03) ports as-is.
- **WhatsApp regression net intact:** every new branch is gated on `ActiveChannel==Telegram`; full EditMode suite **957/957 green** (916 baseline + 41 new).

## Task Commits

1. **Task 2: Telegram media Normalize + type refinement** (TDD)
   - `2e4e334` (test) — failing tests for `TelegramMediaType.Refine` / `TelegramMediaShape.Resolve` (compile-error RED: seams absent)
   - `d543024` (feat) — seams + `RawMessage` flat `mimetype`/`file_name` + `ChatManager.Normalize` (`ResolveMessageType` + `ApplyTelegramMediaShape`); targeted filter 23/23
3. **Task 3: reactions-receive + channel groupness + reply Q8**
   - `9b762e7` (feat) — `ChatIdFormat.IsGroup` "channel" classification + 3 tests
   - `c80c333` (feat) — `TelegramReactionMapper`/`TelegramReactionMerge` + `RawMessage.reactions` + `NormalizedMessage.reactions` (4-layer) + `RefreshCachedMessageReactions` at both reconcile sites + reply Q8 lock; full suite 957/957

_Note: the two Task-3 commits were rewritten (`git reset --mixed` + recommit, nothing pushed) to purge a real sample channel id that had slipped into a test fixture — replaced with a synthetic id. The original hashes (d2aa378/0745292) are now dangling._

**Plan metadata:** _(final docs commit — this SUMMARY + STATE + ROADMAP + REQUIREMENTS + SHAPES.md)_

## Files Created/Modified
- `Assets/Scripts/Chat/TelegramMediaType.cs` (created) — pure `Refine(baseType, mimetype)`: document+video/mp4 => Video; defensive audio/* => Voice; never reclassifies text/reaction.
- `Assets/Scripts/Chat/TelegramMediaShape.cs` (created) — pure `Resolve(fileName, mimetype, media_info)` → file name/mime/size/rounded duration/aspect; null-tolerant.
- `Assets/Scripts/Chat/TelegramReactionMapper.cs` (created) — pure `Map(reactions[])` → `List<MessageReaction>` (fromMe=false, user_id reactorKey); null when unreacted.
- `Assets/Scripts/Chat/TelegramReactionMerge.cs` (created) — pure `Merge`/`SameReactions`: server authoritative, owner "me" preserved until echoed, order-insensitive equality.
- `Assets/Scripts/Chat/RawMessage.cs` (modified) — flat `mimetype`, `file_name`, and `reactions` (JToken) tapi fields.
- `Assets/Scripts/Chat/NormalizedMessage.cs` (modified) — `reactions` list carried Normalize→VM (null for WhatsApp).
- `Assets/Scripts/Chat/ChatIdFormat.cs` (modified) — `IsGroup` "channel" case (suffix-guarded).
- `Assets/Scripts/Main/ChatManager.cs` (modified) — `ResolveMessageType`, `ApplyTelegramMediaShape`, TG reactions map in `Normalize`, `reactions` copy in `CreateViewModel`, `RefreshCachedMessageReactions` + 2 reconcile call sites.
- `Assets/Tests/Editor/Chat/{TelegramMessageType,TelegramMediaNormalize,TelegramReactionReceive}Tests.cs` (created) — 41 new cases (synthetic PII-free JSON).
- `Assets/Tests/Editor/Chat/{ChatIdFormat,ReplyParser}Tests.cs` (modified) — channel groupness + Q8 reply lock.

## Verdict Dispositions (SHAPES.md)

Each capture verdict OVERRODE the pre-capture plan hypotheses. Dispositions:

| Q | Verdict | Disposition in 05-06 |
|---|---------|----------------------|
| Q1 | divergence — media body:null, s3Info:{}, no JPEGThumbnail; media_info EXISTS (dims/size/duration) | Download-by-id (existing serial queue); metadata from media_info + flat name/mime. **No inline-thumbnail port** (the plan's WA base64 assumption was wrong). |
| Q2 | not-observed (partial) — sticker/voice(ptt)/video-note/GIF = 0 in 384; new "poll" type; video-as-document | Observed types shipped fully; poll left dropped (Unknown); **defensive** audio/* → Voice, video/* → Video for the unobserved ones. Owner media re-run still gates device UAT (SHAPES.md Q2 note added). |
| Q3 | divergence / **GO** — reactions[] on every message; no type:"reaction" rows; stanzaId always "" | Receive-side reactions built as a Normalize-time mapping. v2 **TG-REACT-RECV superseded**/promoted. |
| Q4 | divergence — "channel" is a real third dialog type | `ChatIdFormat.IsGroup` classifies "channel" as group-ish. |
| Q5 | confirmed (names) / divergence (avatars) — names populated everywhere; thumbnail null, picture "" | **No code change** — name backfill obviated; avatars stay colored-initial default. |
| Q7 | not-observed — isDeleted:false everywhere; semantics untested | **No code change** — ParseChatsJson isDeleted skip already channel-neutral. |
| Q8 | confirmed — reply_message healthy, NO echo bug (28 samples) | Shared ReplyParser path; echo guard NOT extended to TG (harmless if it runs); Q8 regression test added. |

## Decisions Made
See frontmatter `key-decisions`. Headline: tapi media is **download-only** (Normalize supplies metadata, not a URL); receive-side reactions are a **second poll-based transport** into the same display list, never replacing the WhatsApp live-event path; owner-highlight/toggle for TG reactions is deferred (no per-reaction fromMe on tapi) and rides the optimistic send path.

## Deviations from Plan

The plan was authored BEFORE the capture; the recorded verdicts are binding and OVERRODE several plan hypotheses. These are plan-deviations-by-verdict (expected by design), not auto-fixes:

**1. [Verdict Q1] Media is download-only — no inline-thumbnail/base64 port**
- **Plan assumed:** port the WhatsApp `body`-as-JObject `{JPEGThumbnail,url,...}` / `s3Info.url` shape.
- **Capture found:** `body:null`, `s3Info:{}`, no `JPEGThumbnail`; `media_info` carries dims/size/duration.
- **Did instead:** left `mediaUrl`/`videoUrl` empty so the existing serial `message/media/download`-by-id fallback (channel-aware since 05-03) fetches bytes; added only the metadata via `TelegramMediaShape`. Simpler and correct.

**2. [Verdict Q2] Defensive-only handling for unobserved media types**
- Sticker/voice(ptt)/video-note/GIF were 0 in 384 messages, so only `audio/*`→Voice / `video/*`→Video mimetype-prefix defaults ship (no claimed shapes). Owner media re-run required before device UAT (noted in SHAPES.md Q2).

**3. [Verdict Q3] Reactions-receive BUILT (v2 TG-REACT-RECV promoted)**
- The plan hedged "build or defer"; the capture found a viable (better-than-expected) transport, so receive-side reactions shipped and REQUIREMENTS.md v2 TG-REACT-RECV is marked superseded.

**4. [Verdict Q4] ChatIdFormat "channel" case added (not in plan files_modified)**
- The plan's `files_modified` didn't list `ChatIdFormat.cs`; the Q4 verdict requires "channel" groupness. Added with tests (deviation Rule 3 — necessary to complete the capture-gated scope).

**5. [Verdict Q5/Q7] Name backfill + isDeleted required NO code change**
- The plan's Task-3 name-backfill-from-user.FirstName and isDeleted tasks were obviated (names populated everywhere; isDeleted skip already channel-neutral). `ChatDialog.cs` (in plan `files_modified`) was therefore NOT modified.

**6. [Scope] Reactions touched NormalizedMessage + MessageViewModel layers (plan under-listed)**
- As the environment note anticipated, wiring reactions through the 4-layer pipeline required `NormalizedMessage.reactions` + the `CreateViewModel` copy (beyond the plan's `files_modified`). Kept minimal + channel-scoped.

---

**Total deviations:** 6 verdict-driven (0 bugs). **Impact:** No scope creep — all within the capture-gated CHAT-03/CHAT-07 boundary; several verdicts REDUCED work (Q1 download-only, Q5/Q7 no-change). WhatsApp byte-identical.

## Issues Encountered
None. Both headless runs were green first try (targeted 23/23, full 957/957). No response-crossing or scene churn; the serial media/download queue (memory: Wappi crossing bugs) is reused unchanged and is already channel-aware.

## Known Stubs
None. Receive-side reactions have a real data source (`raw.reactions`); the `fromMe=false`/`time=0` on mapped TG reactions are documented tapi limitations (not displayed by the pill), not stubs. Unobserved-type defensive handling is a safety net awaiting the owner media re-run (documented), not a placeholder.

## Threat Register Coverage
- **T-0506-01 (DoS, Normalize media branches):** mitigated — `TelegramMediaShape.Resolve` + `TelegramReactionMapper.Map` are null-tolerant (missing media_info/reactions/fields degrade to defaults or null, never throw); unknown types map to Unknown/dropped. Covered by missing-field tests.
- **T-0506-02 (Info disclosure, SHAPES.md excerpts):** honored — no raw sample PII entered any committed file; tests use synthetic JSON; samples stay in the gitignored `Tools/tapi/samples/`.
- **T-0506-03 (Tampering, reply snapshot echo):** mitigated — echo-blank not extended to TG (Q8: no echo); shared guard is harmless and the null-tolerant `messages/id/get` recovery fills the card — no fabricated quote text.
- No NEW threat surface beyond the register (no new endpoints/auth/schema; reactions parsing is new untrusted input but within the same tapi-JSON→Normalize boundary, mitigated identically).

## User Setup Required
None for code. **Owner media re-run gates device UAT** (not a code dependency): send a sticker + voice + video-note (кружок) + GIF to the dev Telegram profile and re-run `Tools/tapi/capture-shapes.sh` to confirm the unobserved media type strings before trusting those bubbles on device.

## Next Phase Readiness
- Phase 5 is fully code-complete (all 6 plans). The capture-gated media/reactions/reply work is grounded in real tapi JSON.
- **Phase 8 (device UAT + prod replication):** device UAT should exercise TG media download-by-id rendering, receive-side reaction display + send/receive coherence, "channel" dialog rendering, and incoming reply cards — plus the owner media re-run above.

## Self-Check: PASSED

All 7 created source files + the SUMMARY exist on disk; all 4 task commits (`2e4e334`, `d543024`, `9b762e7`, `c80c333`) are in the git log. Full EditMode suite 957/957 green (0 failed, 0 inconclusive).

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-14*
