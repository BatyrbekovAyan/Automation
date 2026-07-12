---
phase: 01-polished-suggestions-panel-on-mock-data
plan: 04
subsystem: chat
tags: [suggestions-controller, mediator, sequence-guard, semi-auto, editor-wirer, mock-loop]

# Dependency graph
requires:
  - phase: 01-01
    provides: "ISuggestionsProvider seam, MockSuggestionsProvider, SuggestionSequenceGuard"
  - phase: 01-02
    provides: "ChatManager.CurrentChatId, SemiAutoStore"
  - phase: 01-03
    provides: "SuggestionsPanel / SuggestionCard / SemiAutoToggle views"
provides:
  - "SuggestionsController — the live mediator (subscriptions, guard, persistence-driven show/hide, hand-off, auto-populate, refresh)"
  - "SuggestionsControllerWirer [MenuItem] — attaches + wires the controller in Main.unity"
  - "End-to-end semi-auto suggestions loop running on mock data with zero backend"
affects: [phase-2-n8n-provider]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Capture-and-discard concurrency (monotonic seq + captured chatId) mirroring ChatManager.QuoteResolve, via SuggestionSequenceGuard"
    - "Awake/OnDestroy (chat-select/bot-change/view events, fire-while-inactive) vs OnEnable/OnDisable (live messages, active-only) subscription split"
    - "Single-swap-line provider seam: MockSuggestionsProvider named on exactly one line; Phase 2 swaps it with zero other edits"

key-files:
  created:
    - Assets/Scripts/Chat/SuggestionsController.cs
    - Assets/Editor/SuggestionsControllerWirer.cs
  modified:
    - Assets/Scripts/Chat/MockSuggestionsProvider.cs   # review fix: synchronous fallback when runner GO inactive
    - Assets/Scenes/Main.unity                          # wirer output (controller attached + wired); saved, not committed in plan

key-decisions:
  - "Result render is gated on _semiAutoOn (not just seq+chat) so an opt-out mid-flight can't repopulate the panel"
  - "Bot switch resets to OFF/hidden (CurrentChatId is sticky to the previous bot's chat); real restore deferred to chat-open"
  - "Mock answers synchronously when the runner GO is inactive (OnChatSelected fires before SlideInToMessages activates the panel)"
  - "Dropped the plan's unused _activeChatId field (the closure capture carries the request's chatId) to avoid a CS0414 warning"

patterns-established:
  - "Adversarial multi-lens review of correctness-critical mediators before commit (caught a guaranteed crash + 3 races)"

requirements-completed: [INT-01, INT-02, INT-03, INT-04, SEMI-03, DATA-03]

# Metrics
duration: ~40 min
completed: 2026-06-25
---

# Phase 1 Plan 04: SuggestionsController + Wirer Summary

**`SuggestionsController` — the MonoBehaviour mediator that makes Phase 1 live on mock data: toggle → persist + show/hide, card tap → composer hand-off + steered re-cluster (never auto-sends), incoming → auto-populate cards (never the composer), manual refresh, and a monotonic-seq + captured-chat guard discarding stale/superseded results. Wired via a `[MenuItem]`; verified end-to-end in Play Mode.**

## Performance
- **Duration:** ~40 min (incl. adversarial review + fixes)
- **Completed:** 2026-06-25
- **Tasks:** 2 code + 1 human-verify checkpoint
- **Files created:** 2 (controller + wirer); modified MockSuggestionsProvider (review fix) + Main.unity (wirer output, saved not committed)

## Accomplishments
- Full Phase-1 loop runs with no backend: toggle ON → skeleton → 4 ranked RU cards → tap → composer fills + re-cluster; incoming → cards refresh (composer untouched); refresh; toggle OFF → hide; per-chat persistence
- DATA-03 guard wired in `OnResult` (seq + captured chat + semi-auto state); exercised by the mock's adversarial out-of-order path
- Awake/OnEnable subscription split mirrors `MessageListView`; symmetric teardown
- Hardened by an adversarial multi-lens review (see Deviations) — caught a guaranteed crash + 3 races before going live
- User verified the running loop ("seems working")

## Task Commits
1. **Task 1 + Task 2 (controller + wirer + review fixes)** — `958e80f` (feat)

## Files Created/Modified
- `Assets/Scripts/Chat/SuggestionsController.cs` — the mediator
- `Assets/Editor/SuggestionsControllerWirer.cs` — [MenuItem] ref-wiring
- `Assets/Scripts/Chat/MockSuggestionsProvider.cs` — synchronous fallback for an inactive runner (review fix)

## Decisions Made
See key-decisions frontmatter.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Plan defect] Forbidden token in comment**
- Reworded "NEVER call ChatManager.SendTextMessage" → "NEVER auto-send …" so the `! grep SendTextMessage` gate passes. Committed `958e80f`.

**2. [Rule 2 - Quality] Dropped unused `_activeChatId`**
- The plan declared `_activeChatId` but the seq+chat guard uses the closure-captured chatId; keeping it would emit a CS0414 warning. Removed. Committed `958e80f`.

**3. [Rule 1 - Bug, review] CRITICAL: StartCoroutine on inactive runner**
- **Found during:** adversarial review of Task 1
- **Issue:** `OnChatSelected` fires ~300ms before `SlideInToMessages` activates the composer panel (ChatManager.cs:491-492); opening a semi-auto chat → `RestoreForActiveChat` → `IssueRequest` → `MockSuggestionsProvider.Request` → `StartCoroutine` on the inactive controller → throws every time.
- **Fix:** `Request` answers synchronously when `_runner == null || !_runner.isActiveAndEnabled`.
- **Verification:** confirmed against ChatManager.cs:478/491-492 (currentChatId set only in OpenChat; OnChatSelected fired while panel inactive). Committed `958e80f`.

**4. [Rule 1 - Bug, review] Stale render after opt-out + bot-switch stale-chat restore + in-flight on disable**
- `OnResult` now early-returns on `!_semiAutoOn`; `_requestSeq` bumped on toggle-off / disable / bot-change. `HandleBotChanged` resets to OFF/hidden instead of restoring against the sticky `CurrentChatId` (never cleared on bot switch — verified grep: single assignment at ChatManager.cs:478). Committed `958e80f`.

---
**Total deviations:** 4 auto-fixed (1 plan defect, 1 quality, 2 review-found bugs incl. 1 critical). **Impact:** all correctness/robustness; no scope change. The review (4 reviewers + adversarial verification, 2 findings correctly dismissed) prevented a guaranteed runtime crash.

## Issues Encountered
- 26 compile warnings surfaced on the first full Editor-assembly recompile — ALL pre-existing tech debt (obsolete `TMP_Text.enableWordWrapping` in ~12 other Editor builders + 2 prior warnings). None from this phase's files. Left as-is (out of scope).
- mcp-unity `run_tests` 10s WebSocket timeout (run completes) — read results from `TestResults.xml`.

## User Setup Required
None. (Toggle/refresh icon sprites remain null placeholders — non-blocking.)

## Next Phase Readiness
- Phase 1 is feature-complete on mock data. Phase 2 swaps `MockSuggestionsProvider` for an `N8nSuggestionsProvider` on the single Awake line — zero edits to the controller or any view (seam contract honored).

---
*Phase: 01-polished-suggestions-panel-on-mock-data*
*Completed: 2026-06-25*
