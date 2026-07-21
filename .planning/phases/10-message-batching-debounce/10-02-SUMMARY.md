---
phase: 10-message-batching-debounce
plan: 02
subsystem: ui
tags: [debounce, coalesce, suggestions, semi-auto, batching, unity, csharp, editmode, pure-gate, tdd]

# Dependency graph
requires:
  - phase: live-suggestions (v1.0)
    provides: "SuggestionsController.HandleLive/IssueRequest + the _requestSeq / SuggestionSequenceGuard render guard (discards superseded/chat-switched RENDERS) this debounce complements"
  - phase: 08-telegram-parity
    provides: "OpenChatLivePollGate pure injectable-clock gate + ChatManager.LivePoll self-gating coroutine idiom (the analogs copied); the 3s open-chat poll that fires OnLiveMessagesReceived -> HandleLive"
provides:
  - "IncomingDebounceGate — pure, STATEFUL, injectable-clock debounce (Poke/Cancel/ShouldFire; WindowSeconds=2.5f) coalescing rapid incoming batches into ONE «Вместе» request (BATCH-03)"
  - "SuggestionsController.HandleLive pokes the gate; a self-gating DebounceLoop fires the single coalesced IssueRequest when the ~2.5s window settles; manual refresh + card-pick stay immediate"
  - "4-site Cancel + _pendingIncomingText clear (OnDisable / ResetForNoOpenChat / RestoreForActiveChat top / HandleToggle OFF) so a pending window never fires the wrong chat's fragment"
affects: [10-04 (owner-run both-channel e2e verifies suggestions coalesce; tune WindowSeconds at the e2e), 10-03 (sibling server-side debounce gate — independent)]

# Tech tracking
tech-stack:
  added: []  # no new libraries — pure C# gate + coroutine; NO network call added (combine is free)
  patterns:
    - "Pure STATEFUL injectable-clock gate (holds _deadline + _armed across Poke/ShouldFire) — first stateful member of the OpenChatLivePollGate/DashboardRefreshGate pure-gate family; still UnityEngine-free so synthetic-time EditMode-testable"
    - "Self-gating debounce poll coroutine (WaitForSecondsRealtime 0.25s; fire-inside-if ShouldFire), one always-running instance hosted on the MonoBehaviour (Start in OnEnable / Stop in OnDisable) — mirrors ChatManager.LivePoll"
    - "Lifecycle-symmetric cancel: every request-context boundary drops the pending window + clears the captured text ALONGSIDE the existing _requestSeq++ (never replacing it)"

key-files:
  created:
    - Assets/Scripts/Chat/IncomingDebounceGate.cs
    - Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs
  modified:
    - Assets/Scripts/Chat/SuggestionsController.cs

key-decisions:
  - "WindowSeconds = 2.5f as a single named tunable (per CONTEXT range; tune at the 10-04 owner e2e for perceived single-message latency)"
  - "DebounceLoop polls every 0.25s so the 2.5s window resolves promptly, with a cheap !_semiAutoOn guard so it never fires when off"
  - "Four-site Cancel is load-bearing: RestoreForActiveChat (same-bot chat switch) is the BLOCKER the seq guard cannot cover — it catches a chat-switched RENDER, not a stale lastIncomingText baked into the request payload at fire time"
  - "No network code added — combine is free (payload already ships the last <=12 messages); only the firing cadence changed (BATCH-03 is timer-only)"

patterns-established:
  - "First STATEFUL pure gate in the injectable-clock family (_deadline + _armed held across calls) — driven with synthetic float times in EditMode"
  - "Debounce driver coroutine hosted on the controller MonoBehaviour, gated on ShouldFire, torn down symmetrically in OnDisable"

requirements-completed: [BATCH-03]

# Metrics
duration: 15min
completed: 2026-07-21
---

# Phase 10 Plan 02: Client Suggestions Debounce (BATCH-03) Summary

**A pure injectable-clock `IncomingDebounceGate` coalesces rapid incoming fragments into ONE «Вместе» suggestions request; `SuggestionsController.HandleLive` pokes it and a self-gating `DebounceLoop` fires once when the ~2.5s window settles — cancelled and its captured text cleared at all four lifecycle boundaries so no stale fire ever crosses into the wrong chat.**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-07-21T18:44:00+05:00
- **Completed:** 2026-07-21T18:58:41+05:00
- **Tasks:** 2 (Task 1 is TDD: RED + GREEN)
- **Files modified:** 3 (2 created, 1 modified)

## Accomplishments
- Added `IncomingDebounceGate` — the first STATEFUL member of the project's pure injectable-clock gate family (`Poke`/`Cancel`/`ShouldFire`, `WindowSeconds = 2.5f`), no namespace, no `using UnityEngine`, so the "3 rapid pokes → 1 fire after the window" and "chat-switch cancel → zero cross-chat fire" behaviours are proven in EditMode with synthetic time.
- Rewired `SuggestionsController.HandleLive` from a per-fragment `IssueRequest` to a `_debounce.Poke(Time.time)` + `_pendingIncomingText` capture; a single self-gating `DebounceLoop` (0.25s tick) fires the ONE coalesced `IssueRequest(null, _pendingIncomingText)` when the window settles.
- Cancelled the pending window AND cleared `_pendingIncomingText` at all four request-context boundaries — chat close (`OnDisable`, + `StopCoroutine`), bot switch (`ResetForNoOpenChat`), same-bot chat switch (`RestoreForActiveChat`, at the top — the BLOCKER fix), and toggle-off (`HandleToggle` OFF branch) — each alongside the pre-existing `_requestSeq++`.
- Kept `HandleManualRefresh` (INT-03) and `HandleCardTapped` (INT-04) byte-identical — owner-initiated actions are never delayed by the debounce.
- Full EditMode suite green at **1197/1197** (1191 baseline + 6 new gate tests), 0 failures, verified against a fresh recompile (both `editorAssemblyWrittenUtc` → 13:57:32Z and `Assembly-CSharp.dll` mtime advanced).

## Task Commits

Each task was committed atomically:

1. **Task 1 (TDD RED): failing IncomingDebounceGate spec** — `87096bd` (test)
2. **Task 1 (TDD GREEN): IncomingDebounceGate pure debounce gate** — `c426c41` (feat)
3. **Task 2: wire the gate into SuggestionsController (HandleLive debounced; 4-site cancel; manual/card immediate)** — `e75fea2` (feat)

**Plan metadata:** _(final docs commit — SUMMARY + STATE + ROADMAP + REQUIREMENTS)_

## TDD Gate Compliance

- **RED gate present:** `87096bd` (`test(...)`) — a genuine compile-failure RED (Assembly-CSharp-Editor failed with `CS0103: The name 'IncomingDebounceGate' does not exist`, confirmed in Editor.log), exactly as the plan specified for a brand-new type.
- **GREEN gate present:** `c426c41` (`feat(...)`) — the scoped run reported `total=6, passed=6, failed=0` against a fresh assembly stamp (13:51:14Z), so the 6 behaviours are real, not stale-green.
- Gate sequence `test → feat` is intact in `git log`.

## Files Created/Modified
- `Assets/Scripts/Chat/IncomingDebounceGate.cs` — NEW. Pure stateful debounce gate: `WindowSeconds = 2.5f`, `Poke(now)` re-arms to `now + Window`, `ShouldFire(now)` returns true exactly once when settled then disarms, `Cancel()` disarms. No namespace, no `using UnityEngine`.
- `Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs` — NEW. 6 synthetic-time EditMode cases: disarmed-default, fire-once-then-disarm, 3-rapid→1-fire, cancel-mid-window, burst-then-chat-switch (BLOCKER regression: Cancel → zero fire past the window, then a fresh Poke re-arms for the new chat), re-arm-after-fire.
- `Assets/Scripts/Chat/SuggestionsController.cs` — MODIFIED. `using System.Collections;`; three debounce fields; `HandleLive` pokes+captures; `DebounceLoop` self-gating coroutine; `Cancel()` + `_pendingIncomingText = null` at 4 lifecycle sites; loop started in `OnEnable` / stopped in `OnDisable`. `HandleManualRefresh` and `HandleCardTapped` untouched; no network types added.

## Decisions Made
- **2.5s single tunable** — one named `WindowSeconds` constant; the exact value tunes at the 10-04 owner e2e.
- **0.25s poll cadence** — fast enough that the 2.5s window resolves promptly; the loop is a couple of cheap bool checks per tick when idle.
- **RestoreForActiveChat cancel at the TOP** — a same-bot chat switch (A→B) fires this on every `OnChatSelected`; without it, a window pending from chat A would fire with chat A's `_pendingIncomingText` into a request scoped to chat B (the seq guard captures the chat correctly at fire time, so it catches a chat-switched RENDER but NOT the stale text in the payload). This is the mixed-context call T-10-02-01 mitigates.
- **Timer-only, zero server change** — the «Вместе» payload already ships the last ≤12 messages, so coalescing changes only the firing cadence; no new `UnityWebRequest`, no new endpoint, no new secret (T-10-02-03 accept).

## Deviations from Plan

None - plan executed exactly as written. Every wiring site, the verbatim gate body, the 4-site cancel, and the immediate-refresh/card-pick invariants followed the plan's explicit specification. The only discretionary choice (the `DebounceLoop` 0.25s tick) was left to Claude's discretion by the plan.

## Threat Mitigations (from the plan's threat register)
- **T-10-02-01** (pending fire crosses a chat/bot boundary with stale `_pendingIncomingText`) — mitigated: `_debounce.Cancel()` + `_pendingIncomingText = null` at all four lifecycle sites (grep-verified: 4 × `Cancel()`, 4 × `= null`), the loop is `StopCoroutine`'d on disable, and the existing `_requestSeq++` + captured-chat render guard still discards any superseded/chat-switched render.
- **T-10-02-02** (debounce delaying the owner's explicit refresh/card-pick) — mitigated: ONLY `HandleLive` pokes the gate; `HandleManualRefresh`/`HandleCardTapped` call `IssueRequest` directly and are unchanged (absent from the diff).
- **T-10-02-03** (new surface) — accept: no new secret/endpoint/network call; the gate is a pure timer over data the controller already holds (`grep -c UnityWebRequest` = 0 in the diff).

## Known Stubs
None. The gate's initial `_armed = false` / `_deadline = 0` are legitimate disarmed defaults (a never-poked gate must not fire), not stubs.

## Issues Encountered
- **Environment correction — Editor was open, not closed.** The orchestrator brief said the Editor was closed (use headless); in reality a Unity Editor (PID 9851, ~4h) held the project lock, so `Tools/run-tests-headless.sh` refused. Per CLAUDE.md's Editor-OPEN path, all three runs (RED, scoped GREEN, full regression) went through the in-Editor bridge (`Temp/claude/run-tests.trigger` → `test-summary.json`), which does an `AssetDatabase.Refresh` first (imported the two new `.cs` + generated their `.meta`) and refuses to run against stale assemblies. Freshness was gated correctly: the editor-assembly stamp for the test/gate edits (Task 1) and `Assembly-CSharp.dll` mtime for the runtime-only edit (Task 2) — both advanced on their respective runs.
- New-file `.meta` files are 59-byte guid-only stubs, consistent with every existing test `.meta` in the repo; committed as-is (they match what Unity keeps on disk).

## User Setup Required
None for this plan — pure client-side C#. The live both-channel e2e (multi-fragment → one combined reply; suggestions coalesce; semi-auto skips the server path) is the owner-run **10-04** gate; the sibling server-side debounce redeploy is the owner-run **10-03** gate. Dev n8n / tunnel / real profiles remain owner-run (deny-ruled for Claude) but are NOT needed to verify BATCH-03 (timer-only, EditMode-proven).

## Next Phase Readiness
- BATCH-03 is code-complete and EditMode-green; the client now coalesces rapid incomings the same way the auto path (10-01) does on the server.
- **10-03** (owner-run server redeploy + runData abort-vs-combine matrix) and **10-04** (owner-run both-channel e2e, closes the phase) are the remaining gates; 10-04's e2e is where `WindowSeconds` (client) and the server `Debounce Wait amount` are tuned for perceived latency.

## Self-Check: PASSED

- Created files exist: `IncomingDebounceGate.cs`, `IncomingDebounceGateTests.cs` — FOUND
- Modified file exists: `SuggestionsController.cs` — FOUND
- Task commits exist: `87096bd` (RED test), `c426c41` (GREEN feat), `e75fea2` (Task 2 feat) — FOUND
- Full EditMode suite 1197/1197 (0 failures) against a fresh recompile; 4 × `_debounce.Cancel()` + 4 × `_pendingIncomingText = null`; `HandleManualRefresh`/`HandleCardTapped` unchanged; 0 network types added.

---
*Phase: 10-message-batching-debounce*
*Completed: 2026-07-21*
