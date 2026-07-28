---
phase: 08-device-uat-milestone-closeout
plan: 27
subsystem: ui
tags: [whatsapp, reactions, reaction-removal, chatmanager, live-poll, instrumentation, idempotency]

# Dependency graph
requires:
  - phase: 08-device-uat-milestone-closeout
    provides: "08-17 Telegram-only reaction refresh on the already-seen/validate paths (the WhatsApp reaction-raw case this plan adds is the case that Telegram gate skips)"
provides:
  - "[D15] compiled capped log for every WhatsApp reaction raw in SyncLatestMessages (both the unseen and the already-seen branch): rawId/stanzaId/bodyEmpty/seen — discriminates the three IN-02 candidate removal shapes on the round-5 device pass"
  - "Candidate-(a) ingest fix: already-seen WhatsApp reaction raws re-run HandleReactionEvent so a removal re-emitted under the SAME id clears the pill (idempotent, WhatsApp-gated)"
  - "Regression pin Apply_RemovalThenRedelivery_SecondIsIdempotentNoOp — the safety property the candidate-(a) re-process depends on"
affects: [08-29 round-5 re-verify checkpoint, device-uat, whatsapp-reactions]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Diagnosis-first instrument + single provably-safe fix (08-15/08-17 pattern): the code alone can't discriminate the three candidate removal shapes, so instrument all three and fix only the highest-code-evidence one (candidate a) with an idempotent change that cannot regress the other two"
    - "WhatsApp-gated reaction re-process on the already-seen poll branch (ActiveChannel == WhatsApp && raw.type == \"reaction\") — Telegram byte-identical because Telegram reactions ride reactions[] on the message, never a type==reaction raw"

key-files:
  created: []
  modified:
    - "Assets/Scripts/Main/ChatManager.cs — SyncLatestMessages: [D15] reaction-raw log (both branches) + candidate-(a) already-seen re-process"
    - "Assets/Tests/Editor/Chat/ReactionStoreTests.cs — Apply_RemovalThenRedelivery_SecondIsIdempotentNoOp pin"

key-decisions:
  - "Only candidate (a) is code-discriminable, so only (a) is fixed; candidates (b) no-removal-raw and (c) missing-stanzaId are left to the [D15] device logs (no blind fix — the diagnosis-first invariant)"
  - "The fix relies on ReactionStore idempotency (an unchanged reaction returns false -> no event, no repaint), so re-processing an already-applied reaction every poll is a no-op and cannot storm OnMessageReactionsChanged — pinned by the Task 2 regression test"

patterns-established:
  - "A capped compiled [D15] shape log (ids + booleans only, never emoji/body content) is the disposition for the two non-code-discriminable candidates"

requirements-completed: []  # closeout phase — no new v1.1 REQ ids (plan frontmatter requirements: [])

# Metrics
duration: 6min
completed: 2026-07-20
---

# Phase 8 Plan 27: D15 WhatsApp Reaction-Removal Ingest Summary

**Instrumented every WhatsApp reaction raw in SyncLatestMessages (both branches) with a capped [D15] shape log and fixed candidate-(a) — an already-seen WhatsApp reaction raw now re-runs HandleReactionEvent, so a removal re-emitted under the same id clears the pill; idempotency pinned by a new regression test.**

## Performance

- **Duration:** ~6 min
- **Started:** 2026-07-20T16:45:07Z
- **Completed:** 2026-07-20T16:50:32Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- `[D15]` compiled capped log at the `SyncLatestMessages` loop top logs `rawId/stanzaId/bodyEmpty/seen` for every WhatsApp reaction raw (fires on BOTH the unseen branch and the already-seen branch), so the round-5 device FAIL discriminates which of the three IN-02 candidate shapes a WhatsApp removal takes.
- Candidate-(a) fix: the already-seen fall-through now re-runs `HandleReactionEvent(raw, cachedList, chatId)` for WhatsApp reaction raws (before the media/quote refresh), recovering a removal that was re-emitted under the same, already-seen id and previously dropped at the seen gate.
- Both new blocks are WhatsApp-gated (`ActiveChannel == ChatChannel.WhatsApp && raw.type == "reaction"`) — Telegram is byte-identical (its reactions ride `reactions[]`, never a `type==reaction` raw), and the Telegram-only reaction refresh at the fall-through is unchanged.
- New regression test pins the exact safety property the fix depends on: after a removal, a re-delivered removal of the same reactor is an idempotent no-op (returns null, `reactions.Count` stays 0) — so re-processing every poll cannot re-fire `OnMessageReactionsChanged`.

## Task Commits

Each task was committed atomically:

1. **Task 1: [D15] instrumentation + candidate-(a) already-seen re-process** - `94fe649` (feat)
2. **Task 2: Regression pin — removal re-delivery is an idempotent no-op** - `96da6f4` (test)

**Plan metadata:** (final docs commit — this file + STATE.md + ROADMAP.md)

## Files Created/Modified
- `Assets/Scripts/Main/ChatManager.cs` - `SyncLatestMessages`: capped `[D15]` reaction-raw log (both branches) + candidate-(a) already-seen WhatsApp reaction re-process (+22 lines)
- `Assets/Tests/Editor/Chat/ReactionStoreTests.cs` - `Apply_RemovalThenRedelivery_SecondIsIdempotentNoOp` (+16 lines)

## Decisions Made
- **Fix only candidate (a).** Candidates (b) no-removal-raw and (c) missing-stanzaId are not code-discriminable; the `[D15]` log is their disposition on the device pass. This preserves the project's diagnosis-first invariant (08-15/08-17) — no blind guess-fix of all three.
- **Idempotency is the safety guarantee.** `ReactionStore.ApplyToMessage` returns false on an unchanged reaction, so `HandleReactionEvent` returns false and fires no event/repaint; the re-process is a genuine-change-only path. Task 2 pins this.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Reconciled internal plan contradiction] Dropped the `[D15]` bracket token from the log-site comment to satisfy the grep==2 acceptance gate**
- **Found during:** Task 1
- **Issue:** The plan's `<action>` snippet writes the log-site comment as `// [D15] Diagnose WhatsApp reaction-REMOVAL ingest`, which contributes a third literal `[D15]` occurrence — but the plan's `<acceptance_criteria>` and `<verification>` both explicitly require `grep -cn "\[D15\]" -> 2` ("the log line + the candidate-(a) fix comment"). The author miscounted the log-site comment's own `[D15]` token. As-written the action produced 3 occurrences, failing the checkable gate.
- **Fix:** Reworded the log-site comment's opening from `// [D15] Diagnose ...` to `// D15 diagnostic — ...` (content otherwise faithful). The functionally-important `[D15]` tag — the `Debug.Log` string used for device logcat filtering — is untouched, and the candidate-(a) fix comment keeps its `[D15]` marker. Grep is now exactly 2.
- **Files modified:** Assets/Scripts/Main/ChatManager.cs
- **Verification:** `grep -cn "\[D15\]"` -> 2 (Debug.Log line 662 + candidate-(a) comment 734); `must_haves` `contains: "[D15]"` still satisfied.
- **Committed in:** 94fe649 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 internal-contradiction reconciliation)
**Impact on plan:** Cosmetic (comment wording only); the functional `[D15]` logcat tag and both WhatsApp gates are exactly as specified. No scope change.

## Issues Encountered
- The in-Editor test bridge writes a `status: "running"` summary before the final `status: "completed"` one, so the first poll (keyed only on summary mtime) returned mid-run; a second poll keyed on `status == "completed"` captured the final 1181/1181. Freshness confirmed per project memory: the runtime-only ChatManager edit was gated on Assembly-CSharp.dll mtime (21:47:22 local, post-edit), and the editor-asm test edit on `editorAssemblyWrittenUtc` (16:49:23Z, post-edit).

## Verification
- Grep gates: `[D15]` count 2; WhatsApp reaction gate (`ActiveChannel == ChatChannel.WhatsApp && raw.type == "reaction"`) count 2 (log + fix, proving both WhatsApp-gated / Telegram byte-identical); no new concurrent messages/get caller (`StartCoroutine(.*messages/get` unchanged at 0); Telegram-only reaction refresh at the fall-through unchanged.
- Candidate-(a) block (`HandleReactionEvent` + `continue`) sits at line 734, BEFORE `NormalizedMessage reconcileNorm = Normalize(raw);` (line 754) media/quote refresh.
- Full EditMode suite: 1180/1180 after Task 1 (delta 0 — glue through already-tested seams), 1181/1181 after Task 2 (baseline + 1). Both runs fresh (real 23.x s durations, not the cached ~2s).

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Device confirmation rides the round-5 re-verify (08-29): (1) a WhatsApp-app reaction removal clears in-app (candidate-a), and (2) the `[D15]` logcat lines capture the WhatsApp removal shape to disposition candidates (b)/(c).
- `[D15]` is a deliberately compiled (not `#if UNITY_EDITOR`) UAT log capped to ids + booleans — tag it for removal/`#if` gating at phase close (mirrors the IN-04 note for `[D2-view]`).

## Self-Check: PASSED

- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-27-SUMMARY.md`
- FOUND: commit `94fe649` (Task 1 — ChatManager.cs)
- FOUND: commit `96da6f4` (Task 2 — ReactionStoreTests.cs)

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-20*
