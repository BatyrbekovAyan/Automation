---
phase: 08-device-uat-milestone-closeout
plan: 04
subsystem: chat
tags: [live-poll, chat-pipeline, coroutine, telegram, whatsapp, suggestions, D5, gap-closure]

# Dependency graph
requires:
  - phase: 05-telegram-parity
    provides: "SyncLatestMessages channel-aware one-shot (WappiEndpoints.Sync), _chatFetchesInFlight serial gate, CrossChatResponseGuard, ActiveChannel"
  - phase: 07-suggestions-dashboard
    provides: "TryGetRecentMessages payload accessor (reads live _activeChatCache); SuggestionsController.HandleLive"
provides:
  - "Open-chat live poll: a message arriving while a chat stays open renders on its own within ~one refresh cycle, no re-entry, on BOTH WhatsApp and Telegram"
  - "OpenChatLivePollGate — a pure, EditMode-testable poll-cadence decision"
  - "«Вместе» cards refresh on an incoming + the suggestions payload includes the newest incoming (re-opens H2 relevance)"
affects: [08-10 device re-verify (I.1 #3, I.2 #6, H2), 01-VERIFICATION sign-off (I.3 #10)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure gate seam (OpenChatLivePollGate) + a single always-running, self-gating coroutine that REUSES an existing one-shot rather than hand-rolling a second network caller"
    - "Foreground-gated polling via OnApplicationFocus/OnApplicationPause"

key-files:
  created:
    - Assets/Scripts/Chat/OpenChatLivePollGate.cs
    - Assets/Scripts/Main/ChatManager.LivePoll.cs
    - Assets/Tests/Editor/Chat/OpenChatLivePollGateTests.cs
  modified:
    - Assets/Scripts/Main/ChatManager.cs
    - Assets/Scripts/Main/ChatManager.BotState.cs
    - Assets/Scripts/Main/ChatManager.Channel.cs

key-decisions:
  - "3s poll cadence (OpenChatLivePollGate.IntervalSeconds) = the tunable 'one refresh cycle'"
  - "Poll REUSES SyncLatestMessages (no new messages/get caller) so every crossing/serial invariant is inherited, not re-implemented"
  - "Single always-running self-gating coroutine, re-kicked after StopAllCoroutines() in SetActiveBot/SetActiveChannel (never stranded, guarded against duplicates)"
  - "chatIsOpen also requires MessageListPanel.activeSelf — currentChatId is sticky after ShowChatList, so the panel check stops the poll while the owner browses the chat list (battery, T-08-04-02)"

patterns-established:
  - "Poll-gate seam: keep the WHEN decision pure/testable; the coroutine only reads state and reuses the proven fetch"

requirements-completed: []

# Metrics
duration: ~22min
completed: 2026-07-16
---

# Phase 08 Plan 04: Open-Chat Live Poll (D5 gap-closure) Summary

**A 3s self-gating poll coroutine re-issues the existing SyncLatestMessages while a chat stays open, so an incoming message renders on its own — no re-entry — and cascades to «Вместе» card refresh + a fresh suggestions payload, on both WhatsApp and Telegram.**

## Performance

- **Duration:** ~22 min
- **Started:** 2026-07-16T10:31Z (approx, plan load)
- **Completed:** 2026-07-16T10:51Z
- **Tasks:** 2 (Task 1 TDD RED→GREEN)
- **Files modified:** 6 source (.cs): 3 created, 3 modified (+3 Editor-generated .meta)

## Root cause (D5) — confirmed

The open chat's messages are synced **exactly once**, by `SyncLatestMessages` — the only path that computes `brandNew` and fires `OnLiveMessagesReceived` (`ChatManager.cs:789`). It is started in a single place: `OpenChatRoutine` at `ChatManager.cs:943` (`_activeSync = StartCoroutine(SyncLatestMessages(chatId, cachedMessages))`), once per chat open. There is **no** `InvokeRepeating`/timed loop re-fetching the open chat (grep-confirmed: `SyncLatestMessages` is `StartCoroutine`d in exactly that one location). The chat LIST re-syncs only on navigation (`RefreshActiveBotChats`, wired to `SwipeToBack.OnSlideOutComplete` in `OnEnable`, `ChatManager.cs:235`) — never on a timer. Because the suggestions payload reads the live `_activeChatCache` via `TryGetRecentMessages`, and that cache is populated by the one-shot and then never refreshed, the newest incoming never reached the payload either (H2). Hypothesis matched investigation exactly.

## Cascade (verified — no wiring change needed)

A poll → `SyncLatestMessages` → (brand-new) `OnLiveMessagesReceived` fans out to three already-wired consumers:
1. `MessageListView.HandleLiveMessages` (`MessageListView.cs:107`) — the incoming bubble.
2. `SuggestionsController.HandleLive` (`SuggestionsController.cs:62/166`) — «Вместе» card refresh on an incoming.
3. `SyncLatestMessages` also refreshes `_activeChatCache` (`ChatManager.cs:778`), which `N8nSuggestionsProvider` reads via `TryGetRecentMessages` (`N8nSuggestionsProvider.cs:66`) — the payload now carries the newest incoming.

All three were confirmed present and subscribed; the fix required no changes to any of them.

## Accomplishments

- Closed D5 (high): live incoming render in the open chat, cross-channel, without re-entry.
- Pure `OpenChatLivePollGate.ShouldIssue(...)` (chat-open ∧ foreground ∧ no fetch in flight ∧ settled ∧ ≥ interval) — 7 EditMode tests.
- `ChatManager.LivePoll.cs`: one always-running self-gating poll reusing the one-shot; foreground-gated; re-kicked after bot/channel switch.
- Full EditMode suite **1043 passed / 0 failed** (1036 prior + 7 new), FRESH (assembly `2026-07-16T10:47:45Z`).

## Task Commits

1. **Task 1 (RED): failing OpenChatLivePollGate coverage** — `5990ab1` (test)
2. **Task 1 (GREEN): pure OpenChatLivePollGate seam** — `d45f4ed` (feat)
3. **Task 2: repeating open-chat live poll + lifecycle wiring** — `a6a708c` (feat)

**Plan metadata:** _(final docs commit — SUMMARY/STATE/ROADMAP)_

## Files Created/Modified

- `Assets/Scripts/Chat/OpenChatLivePollGate.cs` (created) — pure, UnityEngine-free poll-cadence gate; `IntervalSeconds = 3f`.
- `Assets/Scripts/Main/ChatManager.LivePoll.cs` (created) — `OpenChatLivePollRoutine` (self-gating, reuses `SyncLatestMessages`), `_appFocused`/`_lastLivePollTime`/`_livePollRoutine`, `OnApplicationFocus`/`OnApplicationPause`.
- `Assets/Tests/Editor/Chat/OpenChatLivePollGateTests.cs` (created) — 7 `[Test]` cases (all-conditions fire; each negated condition blocks; interval boundary fires).
- `Assets/Scripts/Main/ChatManager.cs` (modified) — start the poll in `Start()`; baseline `_lastLivePollTime` at chat-open in `SelectChat`.
- `Assets/Scripts/Main/ChatManager.BotState.cs` (modified) — re-kick the poll after `StopAllCoroutines()` in `SetActiveBot` (Task 2 action; beyond the frontmatter `files_modified` list).
- `Assets/Scripts/Main/ChatManager.Channel.cs` (modified) — re-kick the poll after `StopAllCoroutines()` in `SetActiveChannel` (Task 2 action; satisfies the Task-2 grep acceptance).

## Decisions Made

- **Cadence 3s.** Balances perceived liveness against battery/Wappi+tapi request pressure; documented as tunable in the gate.
- **Reuse, don't re-implement.** The poll calls `SyncLatestMessages(currentChatId, _activeChatCache)` — inheriting the post-await `currentChatId` re-check, `CrossChatResponseGuard`, the `_chatFetchesInFlight` serial gate, brand-new-only diff, and cache refresh. `grep -c UnityWebRequest ChatManager.LivePoll.cs` = 0.
- **Single self-gating coroutine.** Started in `Start()`, re-kicked after each `StopAllCoroutines()` (SetActiveBot/SetActiveChannel), handle-guarded so it can never double-run. Cross-channel by design (no `ChatChannel` parameter — D5 reproduces on both).
- **Throttle baselined at chat-open** (`_lastLivePollTime` reset in `SelectChat`) so the open's own sync gets a full interval before the first poll (no immediate redundant fetch).

## Deviations from Plan

The plan's Task-2 action gave a suggested call-site computation for two gate inputs; both were strengthened to exactly the plan's intent + threat model. No architectural changes.

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical (battery/correctness)] `chatIsOpen` also requires `MessageListPanel.activeSelf`**
- **Found during:** Task 2 (lifecycle wiring)
- **Issue:** The plan's literal `chatIsOpen: !string.IsNullOrEmpty(currentChatId)` would keep polling the last-opened chat while the owner browses the chat list — `currentChatId` is sticky after `ShowChatList` (grep-confirmed: cleared only in `SetActiveChannel`, `ChatManager.cs:60`). That wastes battery/requests (threat **T-08-04-02**, dispositioned `mitigate`).
- **Fix:** `chatIsOpen = !string.IsNullOrEmpty(currentChatId) && MessageListPanel != null && MessageListPanel.activeSelf` (mirrors the existing idiom at `ChatManager.cs:275`).
- **Files modified:** Assets/Scripts/Main/ChatManager.LivePoll.cs
- **Verification:** Full suite green; gate stays pure (the panel check is at the call site, not in the gate).
- **Committed in:** `a6a708c`

**2. [Rule 2 - Missing Critical (correctness)] `chatOpenSettled` also requires `!SwipeToBack.IsSliding`**
- **Found during:** Task 2
- **Issue:** `_phase == Idle` alone is true during the slide-out back to the list; a poll there would fire a network call `SyncLatestMessages` only queues anyway (its internal `isSettled` gate excludes `IsSliding`, `ChatManager.cs:785-786`).
- **Fix:** `chatOpenSettled = _phase == ChatOpenPhase.Idle && !SwipeToBack.IsSliding` — exactly the internal `isSettled` predicate (minus Populate, since the initial open already syncs during Populate).
- **Files modified:** Assets/Scripts/Main/ChatManager.LivePoll.cs
- **Verification:** Full suite green.
- **Committed in:** `a6a708c`

---

**Total deviations:** 2 auto-fixed (both Rule 2 — call-site strengthening within the plan's stated intent + threat model). Also: `ChatManager.BotState.cs` + `ChatManager.Channel.cs` were edited per the Task-2 action even though the frontmatter `files_modified` listed only 4 files — instructed work, not a deviation.
**Impact on plan:** No scope creep; no architectural change; every crossing/serial invariant preserved (poll adds no new fetch path).

## TDD Gate Compliance

Task 1 (`tdd="true"`) followed RED→GREEN: `test(...)` `5990ab1` precedes `feat(...)` `d45f4ed`. The RED *failure* could not be independently observed in-session (the Editor was open → the headless runner refuses, and a brand-new `.cs` is invisible to Unity's compile until an Assets/Refresh) — but the commit sequence is correct and the final suite is FRESH-green including the 7 new tests. No missing gates.

## Known Stubs

None — the poll wires real data end-to-end (reuses the live `SyncLatestMessages` path).

## Issues Encountered

- My background freshness-poller exited non-zero (code 4 = "stale") due to a bug in its inline ISO-date parser (it swept the `+00:00` offset digits into the fractional-seconds field). The underlying `test-summary.json` was unambiguous; a clean re-parse confirmed the run is genuinely FRESH (assembly `10:47:45Z` > watermark `10:46:44Z`), `1043/1043` passed. Tooling glitch only — no impact on the code or the verdict.

## Test Status

**FRESH GREEN.** In-Editor bridge run (owner's Editor was open; headless correctly refused): `status=completed overall=Passed passed=1043 failed=0 skipped=0 total=1043`, `editorAssemblyWrittenUtc=2026-07-16T10:47:45.173Z` — postdates all `.cs` edits, so not stale-green. 1036 prior + 7 new `OpenChatLivePollGateTests` = 1043.

## Next Phase Readiness

- Device re-verify rides **08-10**: I.1 #3 (draft protection now testable — incoming renders live), I.2 #6 («Вместе» refreshes on incoming), H2 (suggestion relevance to the newest incoming), on BOTH channels. Then re-aggregate I.3 #10 (01-VERIFICATION sign-off).
- OUT OF SCOPE (unchanged): push-based delivery (n8n → device push) stays a v2 design item in STATE Deferred Items. This plan fixed the client refresh path only; no server/n8n changes.

## Self-Check: PASSED

- Created files verified on disk: `OpenChatLivePollGate.cs`, `ChatManager.LivePoll.cs`, `OpenChatLivePollGateTests.cs`, `08-04-SUMMARY.md`.
- Task commits verified in git log: `5990ab1` (RED test), `d45f4ed` (GREEN gate), `a6a708c` (poll).

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-16*
