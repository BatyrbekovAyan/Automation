---
phase: 02-n8n-live-wiring
plan: 02
subsystem: api
tags: [unity, csharp, unitywebrequest, coroutine, newtonsoft-json, n8n, webhook, suggestions, seam, editmode-tests]

# Dependency graph
requires:
  - phase: 01 (Polished Suggestions Panel on Mock Data)
    provides: ISuggestionsProvider seam + SuggestionRequest/Result/Item/Status DTOs, SuggestionsController swap point (Awake L31), SuggestionSequenceGuard, ChatManager.CurrentChatId + WaitForChatFetchesDrain accessors
  - phase: 02-01 (Suggest Replies workflow)
    provides: live /webhook/SuggestReplies endpoint returning versioned {text,label}[] + requestSeq echo (the frozen v1 wire contract this provider targets)
provides:
  - N8nSuggestionsProvider (live ISuggestionsProvider impl) swapped in on the single SuggestionsController.Awake line (N8N-02)
  - Pure static BuildPayloadJson (frozen v1 request assembly) + MapResponse (tolerant response mapping) — fully EditMode-tested seams
  - ChatManager.TryGetRecentMessages(chatId, n) partial accessor over _activeChatCache
  - SuggestRepliesDtos.cs — [Serializable] v1 request/response wire DTOs
affects: [02-03 (e2e hardening consumes this provider's payload/mapping), 02-04 (live device UAT exercises the swapped seam)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Live provider behind an existing seam: plain C# ISuggestionsProvider impl, network coroutine hosted on the always-active ChatManager.Instance (never the possibly-inactive controller)"
    - "Pure static payload/response seams (BuildPayloadJson / MapResponse) isolated from Unity/network for EditMode testability — mirrors MockSuggestionsProvider.BuildResult + DashboardResponse.Parse"
    - "Serial-guarded pull: yield WaitForChatFetchesDrain() before assembly; provider only waits, never bumps the chat-fetch in-flight counter"

key-files:
  created:
    - Assets/Scripts/Chat/N8nSuggestionsProvider.cs
    - Assets/Scripts/Chat/SuggestRepliesDtos.cs
    - Assets/Scripts/Main/ChatManager.RecentMessages.cs
    - Assets/Tests/Editor/Chat/SuggestRepliesPayloadTests.cs
    - Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs
  modified:
    - Assets/Scripts/Chat/SuggestionsController.cs

key-decisions:
  - "DTOs kept in a separate SuggestRepliesDtos.cs (plan's default; files_modified listed it separately) rather than folded into the provider"
  - "MediaText exposed public static for direct all-branch coverage; BuildPayloadJson/MapResponse public static per acceptance criteria"
  - "Tolerant parse lives inside MapResponse's try/catch (mirroring DashboardResponse.Parse) — no separate Parse method on the DTO"
  - "Run() re-resolves bot + messages AFTER the drain (up to ~3s) so the payload uses the freshest history and bails on a mid-flight chat/bot switch"

patterns-established:
  - "Provider coroutine host: ChatManager.Instance (always active) — the controller GameObject can be inactive ~300ms at OnChatSelected"
  - "requestSeq stamped from the REQUEST, not the server echo — Phase-1 SuggestionSequenceGuard owns stale/superseded discards"

requirements-completed: [N8N-01, N8N-02, N8N-03, N8N-04]

# Metrics
duration: 13min
completed: 2026-07-10
---

# Phase 02 Plan 02: Unity Live Suggestions Provider Summary

**`N8nSuggestionsProvider` consumes the live `/webhook/SuggestReplies` flow behind the `ISuggestionsProvider` seam via a single Awake-line swap — pure static `BuildPayloadJson`/`MapResponse` (v1 contract), a `ChatManager.TryGetRecentMessages` accessor, and 26 green EditMode tests — with zero other Phase-1 edits.**

## Performance

- **Duration:** 13 min
- **Started:** 2026-07-10T15:06:25Z
- **Completed:** 2026-07-10T15:19:49Z
- **Tasks:** 3
- **Files modified:** 6 source (+5 generated `.meta`)

## Accomplishments
- Shipped the live provider swap (N8N-02): the Phase-1 panel now sources suggestions from n8n via one changed line in `SuggestionsController.Awake` — the seam held (any other Phase-1 UI edit would have been a defect).
- `BuildPayloadJson` emits the frozen v1 request: `v==1`, req passthrough, bot fields, `ownerPrompt`≤500 / `catalog`≤1500 clamps, ≤12 messages oldest→newest, role mapping, media placeholders, sentinel `botWaId`, steer + `requestSeq` passthrough.
- `MapResponse` handles every branch: HTTP fail / malformed / null / `error` field / null-or-0-valid → `Error`; 1–4 valid `{text,label}` → `Ok` remapped to `{text,intentLabel}`, `requestSeq` stamped from the REQUEST.
- Network coroutine hosted on the always-active `ChatManager.Instance`, gated behind the public `WaitForChatFetchesDrain()` serial guard, POST per networking rules (Content-Type json, timeout 30), never bumping the chat-fetch in-flight counter.
- 26/26 new EditMode tests green headless; full suite 787/787 (no regression).

## Task Commits

Each task was committed atomically:

1. **Task 1: Wire DTOs + ChatManager.RecentMessages accessor** - `2f1f8eb` (feat)
2. **Task 2: N8nSuggestionsProvider + single seam swap** - `c549284` (feat)
3. **Task 3: EditMode tests for BuildPayloadJson + MapResponse** - `6390c17` (test)

**Plan metadata:** _(this docs commit)_

## Files Created/Modified
- `Assets/Scripts/Chat/N8nSuggestionsProvider.cs` - Live `ISuggestionsProvider`: short-circuit → coroutine on `ChatManager.Instance` → drain → assemble → POST `/webhook/SuggestReplies` → map. Pure static `BuildPayloadJson`, `MapResponse`, `MediaText`.
- `Assets/Scripts/Chat/SuggestRepliesDtos.cs` - `[Serializable]` v1 wire DTOs: `WireMessage`, `SuggestRepliesRequestDto`, `SuggestReplyDto`, `SuggestRepliesResponse`.
- `Assets/Scripts/Main/ChatManager.RecentMessages.cs` - Partial accessor `TryGetRecentMessages(chatId, n, out msgs)` over the private `_activeChatCache` scoped to `currentChatId`.
- `Assets/Scripts/Chat/SuggestionsController.cs` - **One line** (Awake L31): `new MockSuggestionsProvider(this, _mockLatencySeconds)` → `new N8nSuggestionsProvider()`.
- `Assets/Tests/Editor/Chat/SuggestRepliesPayloadTests.cs` - 16 tests for `BuildPayloadJson` + `MediaText`.
- `Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs` - 10 tests for `MapResponse`.

## Decisions Made
- **Private ChatManager members confirmed and used verbatim** (no adaptation needed): `currentChatId` (ChatManager.cs L139) and `_activeChatCache` (`List<MessageViewModel>`, L157) — exactly as the plan/PATTERNS predicted. Reused the existing public `CurrentChatId` (Suggestions.cs L11) rather than re-exposing it; new file is a pure additive partial (ChatManager.cs untouched).
- **DTO layout:** separate `SuggestRepliesDtos.cs` (not folded into the provider), matching `files_modified`.
- **`MediaText` made `public static`** for cheap all-branch coverage; tolerant parse folded into `MapResponse`'s try/catch (no DTO `Parse` method).
- **`Run()` re-resolves the active bot + messages after the drain** (Rule 2 defensive): the drain can take ~3s during which the open chat/bot may change, so assembling against a re-fetched slice keeps the payload fresh and aborts cleanly (→ `Empty`) on a switch.

## Deviations from Plan

None - plan executed exactly as written. (One in-flight comment reword was required so the file passes the `! grep _chatFetchesInFlight` acceptance check — see Issues Encountered; this was a wording fix within Task 2, not a behavioral deviation.)

## Issues Encountered
- **Acceptance grep vs. explanatory comment:** my first draft of `N8nSuggestionsProvider.cs` named `_chatFetchesInFlight` in two comments ("never increment `_chatFetchesInFlight`"). The Task 2 acceptance check is a literal `! grep -q "_chatFetchesInFlight"`, so even a comment mention fails it. Reworded both comments to "never bumps the chat-fetch in-flight counter" — intent preserved, invariant grep now green. Behavior unchanged; caught before any commit.

## TDD Gate Compliance
Tasks 1 & 2 carry `tdd="true"`, but the plan deliberately **co-locates all pure-static tests in Task 3** (its `files_modified` assigns the two test files there, and the only unit-testable surface — `BuildPayloadJson`/`MapResponse` — cannot compile before Task 2 defines their signatures; `TryGetRecentMessages` is a MonoBehaviour-private accessor not reachable from EditMode). Per-task RED gates were therefore folded into the Task 3 `test(...)` commit. Gate outcome is preserved: a `test(...)` commit (`6390c17`) validates the seams, and the suite is green headless (26/26 filtered; 787/787 full). Commit sequence: `feat` (T1) → `feat` (T2) → `test` (T3).

## Verification
- **Full EditMode suite:** 787 passed / 0 failed (`editorAssemblyWrittenUtc` 2026-07-10T15:16:24Z — fresh vs. the 11:30:30Z pre-run DLL, confirming a real recompile, not stale-green).
- **Filtered `SuggestReplies`:** 26 passed / 0 failed — proves the new tests executed (not silently compile-excluded).
- **Zero-edit invariant:** `git diff` since plan start = only the 6 `files_modified` (+5 `.meta`); no other Phase-1 seam/UI file changed; `SuggestionsController.cs` = exactly 1 insertion / 1 deletion; `Main.unity` untouched.
- **Provider shape:** plain C# `: ISuggestionsProvider` (not MonoBehaviour); `StartCoroutine` on `ChatManager.Instance`; yields `WaitForChatFetchesDrain()`; POST `webhook/SuggestReplies` with `Content-Type application/json` + `timeout = 30`; no `X-N8N-API-KEY`; no `async`/`await`; does not reference `_chatFetchesInFlight`.

## Known Stubs
None. (Grep flags the word "placeholder", but every match is the media-**placeholder** feature — `[фото]`/`[видео]`/`[документ]` etc. — real, implemented, and unit-tested mapping logic, not stub code.)

## Next Phase Readiness
- **Ready for 02-03** (adversarial e2e hardening): the live provider + frozen payload/mapping are in place; 02-03 exercises the workflow against injection/grounding/steer/trivial/sentinel matrices and commits the canonical workflow.
- **Ready for 02-04** (live device UAT): the seam is swapped, so a device build now hits real n8n — toggle «Вместе» → skeleton → live cards → pick → steered set.
- **Requirements:** N8N-02 completed here (the client provider + zero-edit swap). N8N-01/03/04 were already complete server-side from 02-01; all four are listed on this plan's frontmatter.
- **Notes carried:** RAG grounding is still catalog-only on dev (Supabase absent in the OpenAI-only dev runtime) — verify RAG grounding at prod bagkz replication (per 02 RESEARCH Pitfall 6). No new blockers.

## Self-Check: PASSED

- Created files verified present: `N8nSuggestionsProvider.cs`, `SuggestRepliesDtos.cs`, `ChatManager.RecentMessages.cs`, `SuggestRepliesPayloadTests.cs`, `SuggestRepliesMapTests.cs`, `02-02-SUMMARY.md` (all `.meta` present).
- Commits verified in history: `2f1f8eb` (T1 feat), `c549284` (T2 feat), `6390c17` (T3 test).

---
*Phase: 02-n8n-live-wiring*
*Completed: 2026-07-10*
