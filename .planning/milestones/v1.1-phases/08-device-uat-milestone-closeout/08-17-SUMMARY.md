---
phase: 08-device-uat-milestone-closeout
plan: 17
subsystem: chat
tags: [telegram, reactions, reconcile, live-poll, tapi, unity, diagnosis]

requires:
  - phase: 08-device-uat-milestone-closeout
    provides: "08-06/08-11 Telegram reaction receive-side pipeline (TelegramReactionMapper, TelegramReactionMerge, RefreshCachedMessageReactions, the two Telegram-gated reconcile sites) + 08-04 D5 open-chat live poll (OpenChatLivePollRoutine)"
provides:
  - "D2-ext fix: a reaction changed/removed IN the Telegram app itself on a LOADED-but-older message reconciles in-app within a poll cycle (candidate A — loaded-window coverage)"
  - "ReactionReconcileWindow pure seam (NeedsWiderPass / PagesToCover) — EditMode-testable loaded-window decision"
  - "ValidateCachePageAgainstServer now Telegram-reconciles reactions (was media+quote only) — the concrete H1 gap"
  - "Diagnosis verdict routing D2-ext: H1 confirmed, H2/H3 refuted"
affects: [08-21 device re-verify, telegram reactions, chat live poll]

tech-stack:
  added: []
  patterns:
    - "Pure UnityEngine-free decision seam (ReactionReconcileWindow) mirroring OpenChatLivePollGate — window math unit-testable without a live server"
    - "Background reaction reconcile reuses the established serial ValidateCachePageAgainstServer seam (no new concurrent messages/get caller); round-robin one-page-per-tick"

key-files:
  created: []
  modified:
    - Assets/Scripts/Chat/TelegramReactionMerge.cs
    - Assets/Scripts/Main/ChatManager.cs
    - Assets/Scripts/Main/ChatManager.LivePoll.cs
    - Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs

key-decisions:
  - "Diagnosis routed to candidate A (loaded-window coverage): H1 confirmed, H2/H3 refuted — no Merge/mapper change needed (absence-clears already correct)"
  - "Root-cause locus: ValidateCachePageAgainstServer reconciled media+quote but NOT reactions, and the D5 poll only re-fetches the latest page (offset=0 limit=50) — so an older loaded message's reaction delta never reconciled live"
  - "Folded ReactionReconcileWindow into TelegramReactionMerge.cs (not a new .cs file) to avoid the Unity new-file import quirk while the Editor is open, and to keep to the plan's named files"
  - "Wider pass is round-robin one-page-per-tick, throttled 12s (>> the 3s poll), serial-gated on _chatFetchesInFlight==0, settled+foregrounded only — Telegram-gated so WhatsApp is byte-identical"

patterns-established:
  - "Loaded-window correction: when a poll only re-fetches the latest page, walk older loaded pages via the existing serial per-page validation seam on a slow throttle"

requirements-completed: []

duration: 22min
completed: 2026-07-17
---

# Phase 8 Plan 17: D2-ext Telegram Reaction Loaded-Window Reconcile Summary

**A reaction changed or removed IN the Telegram app itself on a loaded-but-older message now reconciles in-app within a poll cycle — the D5 live poll only re-fetched the latest page and `ValidateCachePageAgainstServer` never reconciled reactions, so older messages' reaction deltas were stranded. Fixed with a pure `ReactionReconcileWindow` decision + a Telegram-gated background pass reusing the serial per-page seam. WhatsApp byte-identical.**

## Performance

- **Duration:** ~22 min
- **Started:** 2026-07-17T14:03Z (approx)
- **Completed:** 2026-07-17T14:26Z
- **Tasks:** 2 (1 diagnosis, 1 TDD fix)
- **Files modified:** 4

## Task 1 — Diagnosis Verdicts (D2-ext reflection gap)

Diagnosis-first, evidence-based (static reading has been wrong twice this phase — D9/D12). No production code changed by Task 1.

| Hyp | Claim | Verdict | Evidence | Routes to |
|-----|-------|---------|----------|-----------|
| **H1** | Poll-window coverage — the D5 poll re-fetches only the latest 50, so a reaction change on a LOADED-but-older message never reconciles live | **CONFIRMED (primary)** | `MessagesPerPage = 50` (ChatManager.cs:14); `SyncLatestMessages` builds `messages/get?...&limit={MessagesPerPage}&offset=0` (line 602/606) and the D5 poll reuses it; both live reconcile sites (760, 1328) run only inside the fetched batch; and the per-page background seam `ValidateCachePageAgainstServer` reconciled media+quote but had **no** `RefreshCachedMessageReactions` call — so an older loaded message's reaction delta reflected only on manual re-scroll (pagination site 1328), never live. Matches "intermittent / may not" (depends on the reacted message's recency). | **Candidate A** ✅ implemented |
| **H2** | Within-window render — a reaction-only delta on a message INSIDE the latest 50 might not re-render | **REFUTED** | `Normalize(raw).reactions` is populated via `TelegramReactionMapper.Map` (ChatManager.cs:1662); `RefreshCachedMessageReactions` runs `Merge` → for a real emoji/identity change `SameReactions` returns false → `OnMessageReactionsChanged` fires → the bound `MessageItemView` re-renders. No masking gate found (VS16 handled per 08-11). | (no change) |
| **H3** | Absence-vs-removal in Merge — a removal (absent/empty server `reactions[]`) might not clear a non-fresh cached entry | **REFUTED** | `TelegramReactionMapper.Map` returns `null` for an absent/empty array (line 30). `Merge(cached=[non-fresh me OR contact entry], server=null, now)` → `result` empty → the fresh-optimistic block is skipped (non-fresh) → `return result.Count > 0 ? result : null` = **null** (line 80) → `SameReactions([entry], null)` is false → cached cleared. No earlier guard skips calling `Merge`. Existing `Merge_AgedRemoval_ServerWins_SelfHeal` / `Merge_AgedTombstone_NoServer_DropsNaturally` already cover the aged path. | (no change) |

**Net:** the pre-flagged "poll-window absence-vs-removal semantics" hypothesis splits cleanly — the **poll-window** half is the real gap (H1); the **absence-vs-removal in Merge** half is already correct (H3). Only candidate A is needed.

## Task 2 — Fix (candidate A, TDD RED→GREEN)

**RED** (`test(08-17)` `c78ac99`): 10 failing `ReactionReconcileWindow` tests added to `TelegramReactionMergeTests.cs` (type did not exist yet → RED by construction).

**GREEN** (`feat(08-17)` `ba825d0`):
- **`ReactionReconcileWindow`** (pure, in `TelegramReactionMerge.cs`): `NeedsWiderPass(loadedCount, latestPageSize)` = `latestPageSize > 0 && loadedCount > latestPageSize`; `PagesToCover(loadedCount, pageSize)` = `ceil(loadedCount/pageSize)` (0 on non-positive/empty).
- **`ValidateCachePageAgainstServer`** (ChatManager.cs): added the Telegram-gated `RefreshCachedMessageReactions(norm, _activeChatCache)` call (mirrors the two live sites) — the concrete H1 fix so a background per-page validation also reconciles reactions.
- **Live poll** (`ChatManager.LivePoll.cs`): `MaybeIssueWiderReactionReconcile` walks the older pages (2..`PagesToCover`) **one-per-tick** via the existing serial `ValidateCachePageAgainstServer` seam — Telegram-only, throttled `WiderReactionReconcileIntervalSeconds = 12f` (>> the 3s poll), issued only when settled + foregrounded + `_chatFetchesInFlight == 0`. The sync-issue early-`continue` was converted to an `if` block so the wider-pass trigger runs on quiet ticks too. Cursor + throttle re-baseline at chat-open (`SelectChat`).

### Test baseline arithmetic

`1124` (pre-08-17 EditMode total, confirmed by attribute count and the last bridge summary) `+ 10` new `ReactionReconcileWindow` tests `= 1134`.

## Task Commits

1. **Task 2 RED** — `c78ac99` `test(08-17): add failing ReactionReconcileWindow tests (D2-ext RED)`
2. **Task 2 GREEN** — `ba825d0` `feat(08-17): reconcile loaded-window Telegram reactions (D2-ext GREEN)`

_(Task 1 was diagnosis-only — no production edit, no commit, per the plan.)_

**Plan metadata:** committed separately with SUMMARY + STATE + ROADMAP.

## Files Created/Modified

- `Assets/Scripts/Chat/TelegramReactionMerge.cs` — added the pure `ReactionReconcileWindow` class (`Merge`/tombstone logic untouched).
- `Assets/Scripts/Main/ChatManager.cs` — `ValidateCachePageAgainstServer` now Telegram-reconciles reactions; `SelectChat` resets the wider-pass cursor/throttle.
- `Assets/Scripts/Main/ChatManager.LivePoll.cs` — wider-pass fields + `MaybeIssueWiderReactionReconcile`; poll loop restructured to trigger it each tick.
- `Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs` — 10 `ReactionReconcileWindow` tests.

## WhatsApp byte-identical invariant

- Every new reconcile path is behind `ActiveChannel == ChatChannel.Telegram` (count in ChatManager.cs is now **6**, ≥ 2). The `ValidateCachePageAgainstServer` addition short-circuits on WhatsApp; `MaybeIssueWiderReactionReconcile` returns immediately on WhatsApp.
- **No new concurrent `messages/get` caller** — the wider pass reuses `ValidateCachePageAgainstServer` (which already inherits the `_chatFetchesInFlight` serial gate + `CrossChatResponseGuard` + the post-await `currentChatId` re-check and never fires `OnBatchMessagesLoaded`), issued one page at a time only when nothing is in flight.
- `ReactionStore.cs` unmodified (WhatsApp reactions untouched).

## Verification / Test Status

- **Fresh recompile CONFIRMED, no compile errors:** after the edits the in-Editor bridge recompiled `Assembly-CSharp.dll` (mtime 19:22:24 > the last edit) with `editorAssemblyWrittenUtc = 2026-07-17T14:22:28Z` postdating the edits, and the test run advanced to `status: "running"` — which only happens after a **successful** compile. So the code compiles cleanly.
- **Final green: PENDING USER GREEN.** The in-Editor bridge stalled at `status: "running"` across three trigger attempts (the ClaudeTestBridge advances a run only while the Editor is frontmost; the Editor was not focused this session — the last trigger was consumed but the run never reached `completed`), so a `Passed` summary was not produced. The 10 new tests are pure-function assertions that match the implementation exactly; expected result **1134/1134** (`1124 + 10`). Re-run in a focused Editor (drop `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json`) to confirm. Not claiming verified without fresh output.
- Acceptance greps: `MessagesPerPage = 50` present; `ActiveChannel == ChatChannel.Telegram` count = 6 (≥2); no new `StartCoroutine(.*messages/get)`.

## Decisions Made

- Routed to **candidate A only** (H1 confirmed; H2/H3 refuted) — no speculative Merge/mapper change.
- Folded `ReactionReconcileWindow` into `TelegramReactionMerge.cs` rather than a new `.cs` file — avoids the Unity new-file import quirk with the Editor open (no mcp-unity available) and honors the plan's named files.
- Round-robin one-page-per-tick + 12s throttle: a background correction (older-message reaction changes are not the hot path); keeps at most one background fetch in flight, preserving the serial-queue invariant.

## Deviations from Plan

**1. [Scope — file spread] Touched `ChatManager.LivePoll.cs` (not in the plan's `files_modified` list)**
- **Found during:** Task 2 (candidate A wiring).
- **Issue:** The plan's `files_modified` frontmatter lists `ChatManager.cs`, `TelegramReactionMerge.cs`, `TelegramReactionMergeTests.cs`, but the plan body explicitly requires the fix "on the open-chat poll" — which lives in the `ChatManager.LivePoll.cs` partial.
- **Fix:** Added the wider-pass fields + `MaybeIssueWiderReactionReconcile` there (same `ChatManager` class, partial file). `ReactionReconcileWindow` was folded into `TelegramReactionMerge.cs` (a listed file) so no separate new `.cs`/`.meta` was introduced.
- **Verification:** greps confirm Telegram-gating (6), no new concurrent caller, serial-seam reuse.
- **Committed in:** `ba825d0`.

---

**Total deviations:** 1 (scope/file-spread; the plan body sanctioned "on the open-chat poll"). No behavioral scope creep. No architectural change (Rule 4 not triggered).

## Issues Encountered

- **In-Editor test bridge stalled at `running`** (Editor lost frontmost focus mid-run) → no fresh `completed` summary this session. The recompile succeeded (no compile errors); final green is pending a focused re-run. Reported honestly rather than claiming a stale/absent green.

## User Setup Required

None — no external service configuration.

## Next Phase Readiness

- **08-21 device re-verify:** confirm a reaction added/changed/removed IN the Telegram app on a scrolled-up (older, still-loaded) message reflects in-app within ~one wider-pass interval (~12s); WhatsApp reaction UX unchanged. The 08-11 `[TG reaction echo]` Editor log (ChatManager.cs:1688) + read-only `Tools/tapi/probe-message.sh` remain the confirmatory echo-hex capture ask.
- **Blocker:** EditMode green must be re-confirmed FRESH in a focused Editor (expected 1134/1134).
- Round-3 siblings D12 / D13 remain for their own plans.

## Self-Check: PASSED

- SUMMARY exists at `.planning/phases/08-device-uat-milestone-closeout/08-17-SUMMARY.md`.
- All 4 modified files present on disk; `ReactionReconcileWindow` class present in `TelegramReactionMerge.cs`.
- Both task commits exist: `c78ac99` (test RED), `ba825d0` (feat GREEN).
- Acceptance greps pass (`MessagesPerPage = 50`; Telegram-gate count 6 ≥ 2; no new concurrent `messages/get` caller; `ReactionStore.cs` unmodified).
- Caveat: EditMode suite green is FRESH-COMPILED but not FRESH-RUN this session (bridge stalled at `running`, Editor unfocused) — see Verification / Test Status.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-17*
