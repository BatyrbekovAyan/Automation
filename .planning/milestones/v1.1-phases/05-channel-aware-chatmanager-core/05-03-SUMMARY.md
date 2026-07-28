---
phase: 05-channel-aware-chatmanager-core
plan: 03
subsystem: api
tags: [unity, csharp, wappi, tapi, telegram, chat-pipeline, url-builder, parser, delivery-status, tdd]

# Dependency graph
requires:
  - phase: 05-01
    provides: "WappiEndpoints.Sync, ChatIdFormat (DisplayFallback/IsGroup), ChatChannel enum"
  - phase: 05-02
    provides: "ChatManager.ActiveChannel + channel-aware GetActiveProfileId/GetCacheRoot"
provides:
  - "8 non-send-path chat URLs (chats/filter, 3x messages/get, media/download, reaction-resolve, quote messages/id/get, chat/delete) built via WappiEndpoints.Sync(ActiveChannel, ...) — Telegram fetches tapi, WhatsApp api byte-identically"
  - "ActiveChannelSupportsChatDelete guard — DeleteChat is a hard no-op on Telegram (no server call, no optimistic removal)"
  - "ChatDialog.last_time + type fields (tapi chats/filter shape)"
  - "ParseMessageType maps \"text\" => MessageType.Chat (Telegram text renders instead of dropping as Unknown)"
  - "Chat-list time fallback last_timestamp -> last_time (both RFC3339)"
  - "ChatViewModel.IsGroup set at construction (Telegram numeric-id groups flag correctly; @g.us back-compat kept)"
  - "MessageTypeParser.From + ChatDialogTime.Resolve pure seams (unit-tested)"
  - "DeliveryTickFormatter maps pending=>Pending, undelivered/error=>Failed"
affects: [05-04, 05-05, 05-06, phase-6-channel-switcher]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Read-pipeline URLs routed through the single WappiEndpoints.Sync(ActiveChannel, ...) home (no hardcoded api/tapi base at call sites)"
    - "Pure static parser seams (MessageTypeParser, ChatDialogTime) extracted for unit testing — WhatsAppSyncGate/CrossChatResponseGuard precedent"
    - "Construction-time IsGroup value replacing a suffix-computed property, with a default that preserves @g.us back-compat when callers omit the flag"
    - "Capability guard (ActiveChannelSupportsChatDelete) short-circuits a destructive path where no endpoint exists"

key-files:
  created:
    - Assets/Scripts/Chat/MessageTypeParser.cs
    - Assets/Scripts/Chat/ChatDialogTime.cs
    - Assets/Tests/Editor/Chat/ParseMessageTypeTests.cs
    - Assets/Tests/Editor/Chat/ChatDialogTimeFallbackTests.cs
  modified:
    - Assets/Scripts/Main/ChatManager.cs
    - Assets/Scripts/Main/ChatManager.ReactionResolve.cs
    - Assets/Scripts/Main/ChatManager.QuoteResolve.cs
    - Assets/Scripts/Main/ChatManager.DeleteChat.cs
    - Assets/Scripts/Chat/ChatDialog.cs
    - Assets/Scripts/Chat/DeliveryTickFormatter.cs
    - Assets/Scripts/UI/ChatViewModel.cs
    - Assets/Scripts/UI/MessageListView.cs
    - Assets/Tests/Editor/Chat/DeliveryTickFormatterTests.cs

key-decisions:
  - "ParseMessageType switch + chat-list time selection extracted to pure seams (MessageTypeParser.From / ChatDialogTime.Resolve) in Task 3; ChatManager delegates. Task 2 implemented them inline first, Task 3 refactored — the tapi 'text' case and last_time fallback now live in the seams, not ChatManager.cs."
  - "ChatViewModel.IsGroup changed from a suffix-computed property to a readonly value set at construction: IsGroup = isGroup || ChatIdFormat.IsGroup(chatId). Preserves @g.us behavior for callers that omit the flag (existing reaction tests) and lets ParseChatsJson pass true for Telegram groups."
  - "DeleteChat.cs chat/delete URL routed through WappiEndpoints.Sync for consistency even though the guard makes it WhatsApp-only."

patterns-established:
  - "Pattern 1: read-pipeline URLs are channel-aware via WappiEndpoints.Sync(ActiveChannel, <same path+query>); WhatsApp resolves byte-identically to the old literal so the existing suite is the regression net."
  - "Pattern 2: tapi parser divergences (type mapping, time fallback, display fallback, groupness) route through pure seams / ChatIdFormat so the same code serves both channels and WhatsApp stays byte-identical."

requirements-completed: [CHAT-01, CHAT-02, CHAT-10]

# Metrics
duration: 27min
completed: 2026-07-12
---

# Phase 5 Plan 03: Channel-Aware Read Pipeline + tapi Parser Divergences Summary

**8 non-send chat URLs routed through WappiEndpoints.Sync(ActiveChannel), a hard Telegram delete guard, and every capture-free tapi parser divergence (text=>Chat, last_time fallback, DisplayFallback retiring chat.id[..^5], type-based groupness, pending/undelivered/error ticks) — WhatsApp byte-identical, full EditMode suite 878/878 green.**

## Performance

- **Duration:** ~27 min (continuation after a machine restart; Task 1 + delivery-tick RED were pre-committed)
- **Started (this session):** 2026-07-12T18:00Z (resume)
- **Completed:** 2026-07-12T18:26:22Z
- **Tasks:** 3 (Task 1 pre-completed; Tasks 2-3 this session, both TDD)
- **Files modified:** 13 (4 created, 9 modified)

## Accomplishments
- Read pipeline is channel-aware: the 8 non-send-path URL literals (chats/filter, 3× messages/get, media/download, reaction-resolve messages/get, quote messages/id/get, chat/delete) build via `WappiEndpoints.Sync(ActiveChannel, ...)`. WhatsApp resolves byte-identically; Telegram hits tapi.
- Swipe-to-delete is guarded off for Telegram: `ActiveChannelSupportsChatDelete => ActiveChannel == WhatsApp` and `DeleteChat` early-returns with no server call and no optimistic removal (T-0503-04).
- Telegram text renders: `ParseMessageType("text") => MessageType.Chat` (WhatsApp never sends "text").
- Numeric/empty Telegram chat ids never crash: the display-name fallback uses `ChatIdFormat.DisplayFallback`, retiring the crash-prone `chat.id[..^5]` slice (T-0503-01).
- Chat-list rows order/label on Telegram: time parse prefers `last_timestamp`, falls back to `last_time` (both RFC3339); neither parses => 0 (T-0503-02).
- Groupness routes through `ChatIdFormat.IsGroup` for both the chat list (dialog type/isGroup) and message bubbles; WhatsApp `@g.us` behavior unchanged.
- Delivery ticks extended: `pending => Pending`, `undelivered`/`error => Failed`.
- Parser divergences extracted to pure, unit-tested seams: `MessageTypeParser.From` and `ChatDialogTime.Resolve`.

## Task Commits

TDD tasks carry test (RED) → feat (GREEN) commits:

1. **Task 1: 8 chat URL literals via WappiEndpoints + Telegram delete guard** — `1fbdf1b` (feat) *(pre-completed before restart)*
2. **Task 2: tapi parser divergences (ChatDialog fields, last_time fallback, "text" type, DisplayFallback, groupness, delivery ticks)**
   - `8e42ba4` (test) — failing tapi delivery-tick cases (pending/undelivered/error) *(pre-committed RED)*
   - `a70dd76` (feat) — GREEN: all divergences wired; targeted filter 30/30
3. **Task 3: parser divergence tests + pure seams + full suite green**
   - `5b9b55a` (test) — failing MessageTypeParser.From / ChatDialogTime.Resolve tests (compile-error RED)
   - `b6cb6ee` (feat) — GREEN: extract both seams, delegate ParseMessageType/ParseChatsJson; suite 878/878
   - `26b9dac` (style) — reword a comment so the `! grep chat.id[..^5]` acceptance criterion passes

## Files Created/Modified
- `Assets/Scripts/Chat/MessageTypeParser.cs` (created) — pure `From(string)` type switch, incl. tapi "text" => Chat.
- `Assets/Scripts/Chat/ChatDialogTime.cs` (created) — pure `Resolve(lastTimestamp, lastTime)` RFC3339 fallback, TryParse-safe.
- `Assets/Scripts/Chat/ChatDialog.cs` (modified) — added `last_time` + `type` tapi fields.
- `Assets/Scripts/Chat/DeliveryTickFormatter.cs` (modified) — pending/undelivered/error cases.
- `Assets/Scripts/Main/ChatManager.cs` (modified) — 5 URLs via WappiEndpoints.Sync; ParseChatsJson time fallback + DisplayFallback + groupness; ParseMessageType delegates to seam.
- `Assets/Scripts/Main/ChatManager.ReactionResolve.cs`, `ChatManager.QuoteResolve.cs`, `ChatManager.DeleteChat.cs` (modified in Task 1) — URLs via WappiEndpoints.Sync; delete guard.
- `Assets/Scripts/UI/ChatViewModel.cs` (modified) — `IsGroup` construction-time value + trailing `isGroup` param.
- `Assets/Scripts/UI/MessageListView.cs` (modified) — both `@g.us` suffix checks route through `ChatIdFormat.IsGroup`.
- `Assets/Tests/Editor/Chat/DeliveryTickFormatterTests.cs` (modified) — pending/undelivered/error assertions.
- `Assets/Tests/Editor/Chat/ParseMessageTypeTests.cs` (created) — type-mapping coverage via the seam.
- `Assets/Tests/Editor/Chat/ChatDialogTimeFallbackTests.cs` (created) — time-fallback coverage via the seam.

## Decisions Made
- **Inline-then-extract for the two seams.** Task 2 added the "text" case and the `last_timestamp`/`last_time` fallback inline in `ChatManager.cs` (self-contained, compilable). Task 3 then extracted both into pure static seams (`MessageTypeParser.From`, `ChatDialogTime.Resolve`) with delegating call sites — following the plan's explicit Task 3 instruction and the WhatsAppSyncGate precedent. Consequence: the exact literal `"text" => MessageType.Chat` and the time-fallback logic now live in the seams, not `ChatManager.cs` (see Deviations).
- **IsGroup is now a construction-time value, not a property.** Set as `isGroup || ChatIdFormat.IsGroup(chatId)` so a Telegram group (numeric id, no `@g.us`) flags correctly while callers that omit the flag keep the exact `@g.us` behavior — preserving `ChatViewModelReactionTests`.

## Deviations from Plan

### Filing-only (not behavioral): "text" mapping + time-fallback live in the pure seams, not ChatManager.cs

- **What:** Task 2's `<acceptance_criteria>` includes `grep -q '"text" => MessageType.Chat' Assets/Scripts/Main/ChatManager.cs`. Because Task 3 extracted the switch into `MessageTypeParser.From` (per Task 3's own instruction), that literal now resolves against `MessageTypeParser.cs`. Likewise the time-fallback is in `ChatDialogTime.Resolve`. `ChatManager.cs` delegates to both.
- **Why:** The plan explicitly directs Task 3 to extract these into pure seams; keeping a duplicate inline switch would defeat the extraction.
- **Impact:** None on behavior. The frontmatter `must_haves` (text=>Chat, last_time fallback) are met and unit-tested via `ParseMessageTypeTests` / `ChatDialogTimeFallbackTests`. WhatsApp behavior byte-identical.

### [Rule 1 - Bug] Comment defeated a negative-grep acceptance criterion
- **Found during:** Task 2 verification.
- **Issue:** A code comment I added contained the literal `chat.id[..^5]`, which made the plan's `! grep -q "chat.id[..^5]"` T-0503-01 acceptance criterion report a false FAIL even though the crash-prone slice was gone from executable code.
- **Fix:** Reworded the comment to "the crash-prone tail slice"; the negative grep now passes.
- **Files modified:** `Assets/Scripts/Main/ChatManager.cs`
- **Verification:** `! grep -q "chat.id\[..\^5\]" ChatManager.cs` passes; full suite still 878/878.
- **Committed in:** `26b9dac`

---

**Total deviations:** 1 filing-only + 1 Rule-1 comment fix.
**Impact on plan:** No scope creep; all `must_haves`, `artifacts`, and `key_links` satisfied. WhatsApp byte-identical.

## Issues Encountered
None beyond the deviations above. TDD ran clean: delivery-tick RED (`8e42ba4`) went GREEN in `a70dd76`; the seam-extraction RED (`5b9b55a`, compile-error red on the missing `MessageTypeParser`/`ChatDialogTime`) went GREEN in `b6cb6ee`.

## Threat Register Coverage
- **T-0503-01 (DoS, display-name fallback):** mitigated — `ChatIdFormat.DisplayFallback` replaces `chat.id[..^5]`; never slices numeric/short/empty ids. Covered by `ChatIdFormatTests` (05-01) + retirement verified by negative grep.
- **T-0503-02 (Tampering, timestamp parse):** mitigated — `ChatDialogTime.Resolve` parses both fields via `DateTimeOffset.TryParse`; garbage/absent yields 0, never throws. Covered by `ChatDialogTimeFallbackTests` (unparseable/null/garbage => 0).
- **T-0503-03 (Spoofing, channel base):** mitigated — all 8 read URLs built from `ActiveChannel` through the single `WappiEndpoints.Sync` home; 05-01 both-channel tests guard cross-API leakage.
- **T-0503-04 (EoP, Telegram chat/delete):** mitigated — `ActiveChannelSupportsChatDelete` gates `DeleteChat` to a no-op on Telegram; no destructive call is attempted where no endpoint exists.

## WhatsApp Regression Net
- The 8 read URLs resolve byte-identically on WhatsApp (WappiEndpoints.Sync(WhatsApp, path) == old literal).
- Only 2 `wappi.pro/api/sync` literals remain in `ChatManager.cs` — `message/send` (1928) and `message/mark/read` (2018) — both owned by 05-04 (send path).
- `@g.us` groupness unchanged; `ChatViewModelReactionTests` + `DeliveryTickFormatterTests` (existing cases) still green.
- Full EditMode suite: **878/878 green** via `Tools/run-tests-headless.sh` (Editor closed), up from the 854 wave-2 baseline (+24).

## User Setup Required
None — no external service configuration required.

## Next Phase Readiness
- 05-04 (send-path branches) owns the remaining 2 `ChatManager.cs` literals (message/send, message/mark/read) plus the reaction-send recipient, reply branch, and media EndpointFor channel — all read-path URLs are now channel-aware.
- The capture-gated final plan (media Normalize, sticker/GIF strings, reactions-receive) remains BLOCKED on the owner's Phase-3 tapi capture run.

## Self-Check: PASSED

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-12*
