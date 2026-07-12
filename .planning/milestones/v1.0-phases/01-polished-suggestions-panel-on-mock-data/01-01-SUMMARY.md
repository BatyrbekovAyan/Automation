---
phase: 01-polished-suggestions-panel-on-mock-data
plan: 01
subsystem: chat
tags: [suggestions, seam, mock-provider, sequence-guard, editmode-tests, csharp]

# Dependency graph
requires: []
provides:
  - "ISuggestionsProvider seam (DATA-01) — pure C# callback contract, no live-backend/messaging-API/web-request refs"
  - "SuggestionRequest / SuggestionResult / SuggestionItem / SuggestionStatus value objects"
  - "MockSuggestionsProvider (DATA-02) — ranked RU replies, latency, steered re-cluster, error/empty/out-of-order"
  - "SuggestionSequenceGuard.IsCurrent (DATA-03 logic half) — stale-seq + chat-switch discard predicate"
affects: [01-03-suggestions-panel-ui, 01-04-suggestions-controller, phase-2-n8n-provider]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Provider seam: ISuggestionsProvider callback contract above which nothing references the live backend (Phase-2 swap point)"
    - "Pure static single-bool discard predicate (SuggestionSequenceGuard mirrors CrossChatResponseGuard)"
    - "Plain-C# provider with injected MonoBehaviour runner so ranking/steer/error logic is EditMode-testable (BuildResult is pure)"

key-files:
  created:
    - Assets/Scripts/Chat/ISuggestionsProvider.cs
    - Assets/Scripts/Chat/SuggestionRequest.cs
    - Assets/Scripts/Chat/SuggestionResult.cs
    - Assets/Scripts/Chat/SuggestionItem.cs
    - Assets/Scripts/Chat/SuggestionStatus.cs
    - Assets/Scripts/Chat/SuggestionSequenceGuard.cs
    - Assets/Scripts/Chat/MockSuggestionsProvider.cs
    - Assets/Tests/Editor/Chat/SuggestionSequenceGuardTests.cs
    - Assets/Tests/Editor/Chat/MockSuggestionsProviderTests.cs
  modified: []

key-decisions:
  - "Seam-contract tokens (live backend / messaging API / web-request type names) kept out of comments too, not just code — the verification grep treats any occurrence as a breach signal"
  - "MockSuggestionsProvider.Request answers synchronously when runner is null (headless/tests) instead of NRE on StartCoroutine"
  - "Adversarial out-of-order exposed two ways: forcedEchoSeq (explicit override) and simulateOutOfOrder (one-behind), both test-reachable"

patterns-established:
  - "Provider seam pattern: keep the interface + DTOs free of any live-backend reference so Phase 2 swaps the implementation with zero UI edits"
  - "Pure BuildResult + thin coroutine wrapper keeps latency out of the unit tests"

requirements-completed: [DATA-01, DATA-02, DATA-03]

# Metrics
duration: ~15 min
completed: 2026-06-24
---

# Phase 1 Plan 01: Suggestions Provider Seam, RU Mock & Sequence Guard Summary

**Pure-C# reply-suggestions seam (`ISuggestionsProvider`) with a Russian-language `MockSuggestionsProvider` (ranked replies, simulated latency, steered re-cluster, error/empty/out-of-order paths) and a `SuggestionSequenceGuard` discard predicate — 13/13 EditMode tests green.**

## Performance

- **Duration:** ~15 min
- **Completed:** 2026-06-24T13:30:09Z
- **Tasks:** 2 (both TDD)
- **Files created:** 9 (7 source + 2 test)

## Accomplishments
- `ISuggestionsProvider` seam + 4 value objects, verified free of live-backend / messaging-API / web-request references (ROADMAP SC-5 seam contract)
- `MockSuggestionsProvider`: 4 ranked RU replies best-first (one >120-char reply for PANEL-06), `WaitForSeconds` latency, steered re-cluster (distinct ordered set), and `simulateError` / `simulateEmpty` / `simulateOutOfOrder` + `forcedEchoSeq` adversarial paths — all logic in a pure `BuildResult`
- `SuggestionSequenceGuard.IsCurrent` discards stale-seq and chat-switched results, conservative on null==null (mirrors `CrossChatResponseGuard`)
- EditMode tests green via the Editor Test Runner: `SuggestionSequenceGuardTests` 5/5, `MockSuggestionsProviderTests` 8/8

## Task Commits

1. **Task 1 + Task 2 (provider seam, value objects, guard, RU mock, both test classes)** — `4ab9c38` (feat)

_Committed once per plan (not per task) per the session's agreed commit policy — code staged as explicit `.cs` + `.meta` pairs, no blanket add._

## Files Created/Modified
- `Assets/Scripts/Chat/ISuggestionsProvider.cs` — DATA-01 seam (`void Request(SuggestionRequest, Action<SuggestionResult>)`)
- `Assets/Scripts/Chat/SuggestionRequest.cs` — `[Serializable]` input DTO (chatId, lastIncomingText, steerTowardText, requestSeq)
- `Assets/Scripts/Chat/SuggestionResult.cs` — output DTO (items, requestSeq, status)
- `Assets/Scripts/Chat/SuggestionItem.cs` — `{ text, intentLabel }` (mirrors Phase-2 `{ text, label }`)
- `Assets/Scripts/Chat/SuggestionStatus.cs` — `enum { Ok, Empty, Error }`
- `Assets/Scripts/Chat/SuggestionSequenceGuard.cs` — DATA-03 static `IsCurrent` predicate
- `Assets/Scripts/Chat/MockSuggestionsProvider.cs` — DATA-02 mock data source
- `Assets/Tests/Editor/Chat/SuggestionSequenceGuardTests.cs` — 5 tests (keep/discard/null cases)
- `Assets/Tests/Editor/Chat/MockSuggestionsProviderTests.cs` — 8 tests (ranking, long reply, label set, steer, error, empty, seq echo, out-of-order)

## Decisions Made
- See key-decisions frontmatter. Most consequential: kept the forbidden seam tokens out of *comments* as well, so the seam-breach grep stays meaningful.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Plan defect] Seam-contract comment wording**
- **Found during:** Task 1 & Task 2 verification
- **Issue:** The plan's suggested XML-doc comment text literally contained "n8n / Wappi / UnityWebRequest", which fails the plan's own forbidden-token verification grep (the grep matches comments too). The plan's action and its acceptance grep were mutually contradictory.
- **Fix:** Reworded comments to "live backend / messaging API / web-request types" and "the live 'steer toward' field". Uppercase requirement IDs (`N8N-03`) are safe (grep is case-sensitive).
- **Files modified:** ISuggestionsProvider.cs, MockSuggestionsProvider.cs
- **Verification:** `grep -rL "...n8n..."` now returns all 3 seam files as clean.
- **Committed in:** `4ab9c38`

---

**Total deviations:** 1 auto-fixed (1 plan defect)
**Impact on plan:** Wording-only; preserves the seam-contract intent and makes the verification gate meaningful. No scope change.

## Issues Encountered
- **Unity new-file `.meta` not generated under unfocused Editor.** After writing the files, `MockSuggestionsProvider.cs` and `MockSuggestionsProviderTests.cs` (the two Cyrillic-containing files) had no `.meta` and were excluded from compilation. `recompile_scripts` alone did not fix it (it recompiles but does not run a full asset refresh). Resolved by an explicit `Assets/Refresh` menu execution, which imported them and generated `.meta`. The earlier delete+recreate cycle (per the documented new-file import quirk) was not the operative fix — the asset refresh was.
- **mcp-unity `run_tests` dropped once** mid domain-reload (connection failed), then succeeded on retry once compilation settled. Consistent with the known unfocused-timeout behavior; the bridge remains the fallback.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- The seam, value objects, guard, and mock are ready for Plan 01-04's `SuggestionsController` to consume, and for Plan 01-03's UI to bind to `SuggestionItem`.
- DATA-03's controller half (driving the guard with a monotonic seq + captured chat) lands in Plan 01-04.

---
*Phase: 01-polished-suggestions-panel-on-mock-data*
*Completed: 2026-06-24*
