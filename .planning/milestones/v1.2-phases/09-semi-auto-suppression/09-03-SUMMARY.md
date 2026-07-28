---
phase: 09-semi-auto-suppression
plan: 03
subsystem: api
tags: [unity, csharp, webhook, n8n, reply-mode, suppression, fire-and-forget, tdd]

# Dependency graph
requires:
  - phase: 09-01 (persistence + write path)
    provides: /webhook/SetReplyMode contract { profileIds:[...], chatId, suppressed } + sentinel-drop / one-row-per-profile semantics the client mirrors
  - phase: v1.1 05-02/06 (channel identity)
    provides: ChatManager.ActiveChannel + private GetActiveProfileId/ProfileIdForChannel channel seam this wraps (C3)
  - phase: v1.0 live-suggestions
    provides: SuggestionsController HandleToggle/RestoreForActiveChat write sites + N8nSuggestionsProvider fire-and-forget/pure-payload precedent
provides:
  - Manager.BuildReplyModePayload + AuthedProfileIds (pure static, EditMode-tested) + SyncReplyMode/SyncReplyModeRoutine fire-and-forget POST to /webhook/SetReplyMode
  - Manager -> partial (C2) with an owned OnEnable/OnDisable subscribing the built-but-unconsumed ReplyModeToggleBinder.OnReplyModeChanged
  - ChatManager.ActiveChannelProfileId() public accessor (C3)
  - SuggestionsController.PushReplyModeForActiveChat wiring the per-chat override + re-assert-on-open heal
affects: [09-04-live-apply]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Fire-and-forget webhook coroutine on the Manager singleton (copy of DeleteBotFilesRoutine): no auth header, Content-Type json, timeout 30, using-block, log-only on failure"
    - "Pure static payload builder returning a JSON string so EditMode can JObject.Parse the wire contract (N8nSuggestionsProvider.BuildPayloadJson idiom)"
    - "Second Manager partial declares its OWN OnEnable/OnDisable to subscribe a static event leak-free (singleton + static event)"

key-files:
  created:
    - Assets/Scripts/Main/Manager.ReplyModeSync.cs
    - Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs
  modified:
    - Assets/Scripts/Main/Manager.cs
    - Assets/Scripts/Main/ChatManager.Channel.cs
    - Assets/Scripts/Chat/SuggestionsController.cs

key-decisions:
  - "Manager made partial (C2) so a second partial extends the god-object; the ONLY edit to Manager.cs"
  - "Lifecycle via the partial's own OnEnable/OnDisable (C4 discretion) — Manager had none; static event + singleton makes subscribe/unsubscribe leak-free"
  - "Sentinel constant is Bot.UnauthedProfileSentinel (C1), never Bot.ProfileSentinel"
  - "Per-chat write mirrors BOTH explicit ON and OFF (both are explicit overrides); re-assert heal is ON-state only and lives ONLY in RestoreForActiveChat, never HandleLive (Pitfall 3)"

patterns-established:
  - "SUP-02 client write path: three intents (bot-default '*', per-chat override, re-assert-on-open heal) funnel to one Manager.SyncReplyMode fire-and-forget POST"

requirements-completed: [SUP-02]

# Metrics
duration: 9min
completed: 2026-07-19
---

# Phase 9 Plan 03: Client Reply-Mode Sync Summary

**Wires the client-side «Авто/Вместе» toggle through to the server: pure `Manager.BuildReplyModePayload`/`AuthedProfileIds` builders + a fire-and-forget `SyncReplyMode` POST to `/webhook/SetReplyMode`, connected at all three write sites (bot-default flip, per-chat toggle, re-assert-on-open heal) — SUP-02 client half, all EditMode-verifiable.**

## Performance

- **Duration:** 9 min
- **Started:** 2026-07-19T12:30:43Z
- **Completed:** 2026-07-19T12:39:29Z
- **Tasks:** 2 (Task 1 TDD: RED + GREEN)
- **Files modified:** 5 (2 created, 3 modified)

## Accomplishments
- `Manager.ReplyModeSync.cs`: pure static `BuildReplyModePayload(profileIds, chatId, suppressed)` (serializes the exact `{ profileIds, chatId, suppressed }` body the 09-01 Set_Reply_Mode Validate node expects) + `AuthedProfileIds(bot)` (drops the `"-1"` sentinel and blank ids, C1) + `SyncReplyMode`/`SyncReplyModeRoutine` fire-and-forget POST (copies `DeleteBotFilesRoutine` verbatim: no auth header, `Content-Type: application/json`, `timeout = 30`, `using` block, log-only on failure).
- `Manager` promoted to `partial` (C2 — the only edit to `Manager.cs`); the new partial declares its own `OnEnable`/`OnDisable` (C4) to subscribe `OnBotReplyModeChanged` to the built-but-unconsumed `ReplyModeToggleBinder.OnReplyModeChanged`, writing the `"*"` bot-wide row for every authed profile on a bot-default flip.
- `ChatManager.ActiveChannelProfileId()` public accessor (C3) wrapping the private `GetActiveProfileId()` so `SuggestionsController` can resolve the active channel's profile id without reaching private channel state.
- `SuggestionsController.PushReplyModeForActiveChat(suppressed)` helper wired at two sites: `HandleToggle` (after `SemiAutoStore.Set`, mirrors explicit ON and OFF) and inside `RestoreForActiveChat`'s `if (_semiAutoOn)` block (ON-state heal). `HandleLive` deliberately untouched (Pitfall 3 — no POST-every-3s storm).
- EditMode suite green at **1170/1170** (1165 baseline + 5 new ReplyModeSync tests), fresh cold-launch recompile.

## Task Commits

Each task was committed atomically:

1. **Task 1 (TDD RED): failing ReplyModeSync payload + sentinel-filter tests** - `64499ac` (test)
2. **Task 1 (TDD GREEN): payload builders + fire-and-forget sync coroutine** - `423b835` (feat)
3. **Task 2: wire the 3 write sites + channel-profile accessor** - `181d602` (feat)

**Plan metadata:** _(final docs commit — see below)_

## Files Created/Modified
- `Assets/Scripts/Main/Manager.ReplyModeSync.cs` (new) - payload builders, `SyncReplyMode`/routine, `OnBotReplyModeChanged` hook + `OnEnable`/`OnDisable`
- `Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs` (new) - 5 pure EditMode tests (2 payload, 3 sentinel-filter)
- `Assets/Scripts/Main/Manager.cs` - `class` -> `partial class` (C2), one-word edit
- `Assets/Scripts/Main/ChatManager.Channel.cs` - `public string ActiveChannelProfileId()` accessor (C3)
- `Assets/Scripts/Chat/SuggestionsController.cs` - `PushReplyModeForActiveChat` helper + 2 call sites (HandleToggle, RestoreForActiveChat)

## Decisions Made
- Followed the plan/PATTERNS/RESEARCH C1–C4 corrections verbatim (sentinel name, `partial`, public accessor, lifecycle-in-partial).
- Payload keys verified against the live `Set_Reply_Mode.json` Validate node (`profileIds`/`chatId`/`suppressed`, no `v` version field) before writing the tests, so the client body matches the server contract exactly.
- TDD structured as two commits (test `64499ac` before feat `423b835`) to preserve the RED→GREEN gate order in git log.

## Deviations from Plan

None - plan executed exactly as written. No Rule 1–4 deviations were needed. `HandleLive` left untouched per the anti-pattern guard; all three STRIDE mitigations (T-09-10 sentinel filtering, T-09-11 re-assert-only-in-RestoreForActiveChat) are present.

## Issues Encountered
- The RED run surfaced as an Assembly-CSharp-Editor **compile failure** (5× `CS0117` — `Manager` had no `BuildReplyModePayload`/`AuthedProfileIds`) rather than a clean NUnit red, which is the expected pre-implementation RED for a compile-checked builder (the test file itself was syntactically valid; the symbols simply did not exist yet). GREEN run after implementation: 5/5 filtered, then 1170/1170 full.
- A stale `Temp/UnityLockfile` (from the prior headless launch) triggered the script's stale-lock warning on the GREEN run; the script correctly detected no running Editor and proceeded. No effect on results.

## TDD Gate Compliance
Git log shows the mandatory sequence: `test(09-03)` (`64499ac`, RED) → `feat(09-03)` (`423b835`, GREEN). No unexpected pass during RED (compile failure is unambiguously non-passing). No REFACTOR commit needed.

## User Setup Required
None for this plan. Live application (deploy/activate `Set_Reply_Mode` on dev n8n, apply the DDL, run the curl matrix, gate branch verify on live runData) is the **09-04 owner gate** — this plan is fully EditMode-verifiable and touched no live n8n/DB.

## Next Phase Readiness
- **09-04 (live apply)** now has the complete client write path: all three intents POST `{ profileIds, chatId, suppressed }` to `/webhook/SetReplyMode`. Once 09-04 deploys the workflow and the gate, an on-device flip should land a row the gate reads.
- Identifier equivalence note (RESEARCH Pitfall 1, verified): `ChatManager.CurrentChatId` is the same string the gate reads at `messages[0].from` for 1:1 chats — no normalization added. Cross-channel chatId format equality per channel remains the highest-risk integration detail to confirm on device (09-04 UAT).
- No blockers introduced. The unauthenticated webhook is accepted-risk (R-02-01 / T-09-09), consistent with every other app `/webhook/*`.

## Self-Check: PASSED

- Files: all present (`Manager.ReplyModeSync.cs` + `.meta`, `ReplyModeSyncPayloadTests.cs` + `.meta`, `09-03-SUMMARY.md`).
- Commits: `64499ac` (RED test), `423b835` (GREEN feat), `181d602` (Task 2 feat) all in git log.
- Tests: 1170/1170 EditMode green (headless cold-launch, fresh recompile, exit 0) — real NUnit output, not a code read.

---
*Phase: 09-semi-auto-suppression*
*Completed: 2026-07-19*
