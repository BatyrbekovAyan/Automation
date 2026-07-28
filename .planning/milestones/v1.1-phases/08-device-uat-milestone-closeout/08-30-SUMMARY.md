---
phase: 08-device-uat-milestone-closeout
plan: 30
subsystem: chat
tags: [telegram, reactions, reconcile, optimistic-grace, tdd, unity]

# Dependency graph
requires:
  - phase: 08 (08-06)
    provides: TelegramReactionMerge + optimistic-grace tombstone reconcile
  - phase: 08 (08-11)
    provides: ReactionEmoji.SameEmoji (VS16-insensitive) + un-mapped same-emoji fold
provides:
  - "confirmation-clears-grace: a same-emoji server echo ENDS the 90s optimistic grace (adopt the server 'me' element) so a later external own-reaction change made in the Telegram app repaints"
  - "never-clear-on-differ: a differing echo within the window is still suppressed (no D2 flicker regression)"
  - "WR-01 tombstone drop-on-confirmed-absence: removal tombstone dropped once the server confirms no 'me', so an external own re-add applies"
  - "Editor-only [D2-merge] differ-suppress diagnostic log"
affects: [08-33 owner re-verify, D2-view Gate A]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Optimistic grace ended by server confirmation (same-emoji echo) rather than tap-time alone — identity+confirmation, not identity+time"
    - "Tombstone carried WHILE the server echoes, DROPPED on confirmed absence (mirror of the confirmation rule)"

key-files:
  created: []
  modified:
    - Assets/Scripts/Chat/TelegramReactionMerge.cs
    - Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs

key-decisions:
  - "Grace ends on a SAME-emoji echo (adopt server element, non-fresh), NOT on a differing echo — clearing on differ would regress the original D2 stale-old-emoji flicker (pinned by Merge_DifferingEchoWithinGrace_StaleOldEmojiStillSuppressed)"
  - "Fold branch re-keys the un-mapped same-emoji echo to 'me' (reactorKey+fromMe) instead of pinning the fresh optimistic entry, so the grace is consumed there too"
  - "Tombstone dropped only when serverMine<0 (confirmed absence); server-still-echoes carry (WR-03) preserved verbatim"
  - "The known residual edge (external change before the optimistic echo ever lands) keeps the 90s worst case — accepted, vastly narrower than today (threat T-08-30-02)"

patterns-established:
  - "Pattern: reconcile grace windows should be ended by positive server confirmation, not by a bare time budget"

requirements-completed: []

# Metrics
duration: 9min
completed: 2026-07-20
---

# Phase 8 Plan 30: D2-view Root Fix — Confirmation-Clears-Grace + WR-01 Tombstone Mirror Summary

**A same-emoji Telegram server echo now CONSUMES the 90s optimistic grace (adopt the server "me" element) so an own-reaction change made in the Telegram app repaints; a differing echo is still suppressed; and the removal tombstone is dropped on confirmed absence so an external re-add applies — all in `TelegramReactionMerge.Merge`, WhatsApp byte-identical.**

## Performance

- **Duration:** ~9 min
- **Started:** 2026-07-20T21:21:31Z
- **Completed:** 2026-07-20T21:30:01Z
- **Tasks:** 2 (both TDD)
- **Files modified:** 2

## Accomplishments

- **CR-01 (D2-view, milestone #1 defect):** The optimistic grace in `Merge` is no longer keyed on identity + tap-time only. When the server echo carries the SAME emoji the owner set, the server entry is adopted (time=0, non-fresh, still `reactorKey "me"`) — grace consumed — so the NEXT differing echo is a genuine external own-change and applies immediately (`SameReactions` false → `OnMessageReactionsChanged` fires). This closes the round-2..5 echo-without-event: `result[serverMine] = mine` no longer carries the same fresh `mine` forward for the full 90s window.
- **Never-clear-on-differ:** A DIFFERING echo within the window still yields to the fresh local emoji (stale-old-emoji suppress), so the original D2 flicker does not regress. Pinned by `Merge_DifferingEchoWithinGrace_StaleOldEmojiStillSuppressed`.
- **Fold branch:** an un-mapped same-emoji echo is re-keyed to "me" (`reactorKey` + `fromMe`) rather than pinning the fresh optimistic entry — same confirmation semantics, and the existing `..._CollapsesToOneMe` fold test still passes.
- **WR-01 tombstone mirror:** the empty-emoji removal tombstone is CARRIED only while the server still echoes the stale "me" (serverMine≥0, WR-03), and DROPPED once the server confirms the absence (serverMine<0) — so an external own re-add made in the Telegram app applies instead of being suppressed for the rest of the window.
- **Editor-only `[D2-merge]` diagnostic log** on the differ-suppress path (`#if UNITY_EDITOR`, fully-qualified `UnityEngine.Debug.Log`) — the class stays UnityEngine-free in the shipped player build.

## Task Commits

Each TDD task was committed atomically (RED → GREEN):

1. **Task 1 RED: CR-01 failing tests** - `744178e` (test)
2. **Task 1 GREEN: confirmation-clears-grace in Merge** - `b904106` (feat)
3. **Task 2 RED: WR-01 drop-tombstone tests** - `801087f` (test)
4. **Task 2 GREEN: drop tombstone on confirmed absence** - `cbe6695` (feat)

**Plan metadata:** (docs commit — SUMMARY/STATE/ROADMAP)

## Files Created/Modified

- `Assets/Scripts/Chat/TelegramReactionMerge.cs` — confirmation-clears-grace in the `serverMine>=0` branch (`!ReactionEmoji.SameEmoji(result[serverMine].emoji, mine.emoji)` gate) + fold re-key to "me" + WR-01 tombstone drop-on-confirmed-absence + Editor-only `[D2-merge]` log + updated class-summary doc.
- `Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs` — 2 CR-01 tests (`Merge_SameEmojiEcho_ConsumesGrace_ThenExternalOwnChangeApplies`, `Merge_DifferingEchoWithinGrace_StaleOldEmojiStillSuppressed`), 2 renamed WR-01 tests (`Merge_OtherUserSameEmoji_MyAbsenceConfirmed_DropsTombstone_OtherKept`, `Merge_LoneFreshRemoval_NoServerEcho_DropsTombstone_AbsenceConfirmed`), 1 new WR-01 test (`Merge_FreshRemoval_AbsenceConfirmed_ThenExternalReAdd_Applies`).

## Verification

- **TDD RED gates (fail for the right reason):**
  - Task 1 RED (filtered): 26 pass / 1 fail — `Merge_SameEmojiEcho_ConsumesGrace_ThenExternalOwnChangeApplies` failed on `SameReactions` (Expected False, was True), exactly the grace-not-consumed defect. `Merge_DifferingEchoWithinGrace` green today (boundary guard).
  - Task 2 RED (filtered): 25 pass / 3 fail — the 3 WR-01 tests (2× "Expected null but was MessageReaction", 1× "Expected 1 but was 2"), the tombstone-carried-on-confirmed-absence defect.
- **GREEN:** Task 1 27/27, Task 2 28/28 (filtered `TelegramReactionMerge`).
- **Full EditMode suite: 1184/1184 green** (baseline 1181 + 3 net; Task 1 +2, Task 2 +1 net-new, 2 renames flat). Fresh: runtime `Assembly-CSharp.dll` recompiled after the WR-01 edit (previously-RED WR-01 tests now pass = definitive), test `editorAssemblyWrittenUtc` 2026-07-20T21:26:19Z postdates the test edits.
- **Acceptance greps all pass:** `SameEmoji(result[serverMine].emoji, mine.emoji)` →1; `result[echoIdx] = mine` →0; `[D2-merge]` →1; `ConsumesGrace|DifferingEchoWithinGrace` →2; `AbsenceConfirmed|MyAbsenceConfirmed|ThenExternalReAdd` →3; `KeepsInvisibleTombstone|NotConsumedByTombstone` →0; `result.Add(mine);` →2 (tombstone-carry + fold-else).
- **WhatsApp byte-identical:** only `TelegramReactionMerge.cs` + its tests changed across all 4 commits; `ReactionStore` (WhatsApp) and every other file untouched. `TelegramReactionMerge` is Telegram-only — WhatsApp reactions never reach `Merge`.

## TDD Gate Compliance

Plan `type: tdd`. Gate sequence honored per task: `test(...)` RED commit precedes the `feat(...)` GREEN commit for both Task 1 (`744178e` → `b904106`) and Task 2 (`801087f` → `cbe6695`). No unexpected RED-phase pass (Task 1's `Merge_DifferingEchoWithinGrace` passing at RED is the intended boundary guard, not the feature-under-test). No REFACTOR commit needed.

## Decisions Made

See key-decisions frontmatter. In short: grace ends on a same-emoji confirmation only (never on differ); fold re-keys rather than pins; tombstone drops only on confirmed absence; the narrow pre-echo-external-change edge keeps the 90s worst case (accepted).

## Deviations from Plan

None - plan executed exactly as written. No auto-fixes, no blocking issues, no authentication gates. The only mid-edit friction was a doc-comment line-wrap left by Task 1's edit ("tombstone" wrapped onto its own line), which required re-reading the exact text before Task 2's doc rewrite — resolved without changing scope.

## Known Stubs

None. The change is pure reconcile logic on live data; no hardcoded empty values, placeholders, or unwired data sources introduced.

## Issues Encountered

None during planned work.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- **08-33 owner re-verify** is the device/Editor gate: confirm an own-reaction change made in the Telegram app repaints the bubble (D2-view closed) and that a differing echo does not flicker. The `[D2-merge]` Editor log discriminates any residual (differ-suppress fired vs genuinely no event).
- Diagnostic `[D2-merge]` log is tagged for removal at phase close (per IN-03 in 08-REVIEW).
- WhatsApp reaction paths unchanged — D15/D17 (plans 08-31/08-32) are independent.

## Self-Check: PASSED

- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-30-SUMMARY.md`
- FOUND commits: `744178e` (test), `b904106` (feat), `801087f` (test), `cbe6695` (feat)
- FOUND modified: `Assets/Scripts/Chat/TelegramReactionMerge.cs`, `Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs`

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-20*
