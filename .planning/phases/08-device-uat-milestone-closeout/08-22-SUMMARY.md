---
phase: 08-device-uat-milestone-closeout
plan: 22
subsystem: ui
tags: [telegram, reactions, tmp-mesh, reaction-bar, override-sorting-canvas, view-refresh]

# Dependency graph
requires:
  - phase: 08-device-uat-milestone-closeout
    provides: "08-06/08-11 Telegram reaction pipeline (TelegramReactionCatalog, tombstone, VS16 identity); 08-17 D2-ext data-layer reconcile"
provides:
  - "ReactionPillView.ForceReRender(): re-render + TMP mesh regeneration (SetAllDirty + ForceMeshUpdate) on the root canvas"
  - "MessageItemView.RefreshReactionsVisual(): public re-render entry callable by the reaction bar"
  - "ReactionBarController one-frame-deferred pill re-render after Hide()/UnliftRow() destroys the lifted overrideSorting Canvas"
  - "Compiled (non-editor) capped [D2-view] diagnostic log in HandleReactionsChanged (id + count only)"
affects: [08-25, reaction-ux, gate-a]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "One-frame-deferred view refresh from an always-active singleton after a deferred Destroy lands (yield return null then re-dirty)"
    - "Force TMP mesh regeneration on canvas re-parenting via SetAllDirty + ForceMeshUpdate"

key-files:
  created: []
  modified:
    - "Assets/Scripts/UI/ReactionPillView.cs"
    - "Assets/Scripts/UI/MessageItemView.cs"
    - "Assets/Scripts/Chat/ReactionBarController.cs"

key-decisions:
  - "View/refresh layer ONLY — TelegramReactionMerge/reconcile untouched (owner: logs always show the correct reaction; the data layer is proven correct)"
  - "Re-render is channel-agnostic + idempotent, so WhatsApp is byte-identical (a pill that was never lost re-renders the same pixels)"
  - "Diagnostic log is compiled (not #if UNITY_EDITOR) so the next device UAT confirms the handler ran; capped to id + integer count, no emoji/body (T-08-22-01)"

patterns-established:
  - "Defer a view refresh one frame after a nested overrideSorting Canvas is destroyed so the graphic re-registers on the root canvas before re-dirtying"

requirements-completed: []

# Metrics
duration: 8min
completed: 2026-07-20
---

# Phase 8 Plan 22: D2-view Deferred Reaction Pill Re-render Summary

**One-frame-deferred TMP pill mesh regeneration wired from the reaction bar's dismiss so a reaction changed on bubble A repaints even after A's lifted overrideSorting Canvas is destroyed — plus a compiled [D2-view] diagnostic log; data layer untouched, WhatsApp byte-identical.**

## Performance

- **Duration:** ~8 min
- **Started:** 2026-07-20T12:00:24Z
- **Completed:** 2026-07-20T12:08:03Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- `ReactionPillView.ForceReRender()` re-renders the last reactions and forces the TMP label mesh to regenerate (`SetAllDirty()` + `ForceMeshUpdate()`) on whatever canvas the label now lives under — the WR-01 lost-mesh cure.
- `MessageItemView.RefreshReactionsVisual()` gives the reaction bar a public, idempotent re-render entry (re-runs `RenderReactions()` then `reactionPill.ForceReRender()`; renders the CURRENT vm, safe on a pooled re-bind).
- `ReactionBarController` captures the pressed `MessageItemView` (`_sourceView`) in `Show()` and, after `UnliftRow()` in `Hide()`, starts a one-frame-deferred coroutine (`RefreshSourceNextFrame`, `yield return null`) so the deferred `Destroy(_liftedCanvas)` lands before it re-renders the pressed bubble on the root canvas — the stale pill self-heals despite the data-layer dedup guard swallowing every future reconcile.
- Compiled (non-editor) capped `[D2-view]` log added as the first statement in `HandleReactionsChanged` after the guards, so the next device UAT confirms the handler ran (id + integer count only — no emoji, `changed.text`, or body).

## Task Commits

Each task was committed atomically:

1. **Task 1: Add the public re-render hooks + compiled diagnostic log** — `be3caf4` (feat)
2. **Task 2: Wire the one-frame-deferred re-render from the reaction bar dismiss** — `1eaf81c` (feat)

**Plan metadata:** _(final docs commit — see below)_

## Files Created/Modified

- `Assets/Scripts/UI/ReactionPillView.cs` — added `public void ForceReRender()` (Render + SetAllDirty + ForceMeshUpdate).
- `Assets/Scripts/UI/MessageItemView.cs` — added `public void RefreshReactionsVisual()` wrapper; added the compiled capped `[D2-view]` log in `HandleReactionsChanged`.
- `Assets/Scripts/Chat/ReactionBarController.cs` — added `_sourceView` field, capture in `Show()`, deferred re-render in `Hide()`, and the `RefreshSourceNextFrame` coroutine.

## Decisions Made

- **Kept the fix strictly in the view/refresh layer.** Owner reported "logs actually always shows correct reaction" — the data layer is proven correct, so `TelegramReactionMerge`/reconcile stayed untouched (grep count 0 for merge/reconcile tokens in all three files). The stale pill is a lost-mesh render, not a data problem.
- **Diagnostic log compiled, not editor-only.** So the next device UAT pass (08-25) can confirm the handler fired on-device; capped to `messageId` + integer count for T-08-22-01.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Reworded a comment token to resolve a plan-internal grep contradiction**
- **Found during:** Task 2 (reaction-bar wiring)
- **Issue:** The plan's suggested `Hide()` comment text contained the literal token `SameReactions`, but the same task's acceptance criterion asserts `grep -cn "TelegramReactionMerge\|RefreshCachedMessageReactions\|SameReactions" → 0` (proving no reconcile/merge code was touched). Pasting the suggested comment verbatim made the grep return 1 — a false positive on descriptive comment text, contradicting the plan's own criterion.
- **Fix:** Reworded the comment "The SameReactions dedup guard…" to "The data-layer dedup guard…", preserving the full mechanism explanation while making the acceptance grep return 0. No code behavior change — comment text only.
- **Files modified:** Assets/Scripts/Chat/ReactionBarController.cs
- **Verification:** `grep -cn "TelegramReactionMerge\|RefreshCachedMessageReactions\|SameReactions"` → 0; suite green.
- **Committed in:** `1eaf81c` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 plan-internal grep contradiction reconciled)
**Impact on plan:** No scope change. The intent of the acceptance criterion (no reconcile/merge code touched) is honored both literally and semantically; the mechanism is still documented in the comment.

## Issues Encountered

- **Verification tooling:** the initial poll loops failed instantly because `status` is a read-only variable in zsh (the project shell) — renamed the loop variable to `st` and re-polled. Not a code issue; the Editor bridge run itself succeeded.
- **Editor focus:** the in-Editor test bridge only ticks while Unity is frontmost. Brought Unity forward with `open -a`; Bash subprocess polls do not steal GUI focus, so the run completed cleanly.

## Verification

- Full EditMode suite via the in-Editor ClaudeTestBridge: **1170/1170 passed, 0 failed** (overall Passed), `editorAssemblyWrittenUtc` 2026-07-20T12:05:04Z — fresh, postdates the 12:03:29Z edits. Delta 0 new tests (view-layer frame-timing fix has no pure seam).
- Clean compile: `Assembly-CSharp.dll` (662016 bytes) + `Assembly-CSharp-Editor.dll` both regenerated at 12:05Z with no compilation-failed summary.
- All task acceptance-criteria greps pass; reconcile/merge grep count = 0 across the three files (data layer honored).
- WhatsApp byte-identical: the deferred re-render is channel-agnostic and idempotent; no WhatsApp-only path changes behavior.

## Known Stubs

None — both new public methods are fully wired to real render paths and exercised on every reaction-bar dismiss.

## Next Phase Readiness

- Device confirmation of the repro (change a reaction on bubble A, start changing on bubble B → A repaints; WhatsApp unchanged) rides the consolidated 08-25 owner re-verify.
- Sibling round-4 plans 08-23 (D12-ext) and 08-24 (D14) remain; on all-PASS at 08-25 Gate A flips to PASS and unblocks Gates B/C.
- WR-03 (deep-scrolled reaction staleness past the 100-message cache cap) remains accepted-v1 per the plan's `<deferred>` — out of D2-view's view-only scope; the data-layer reconcile it lives in was deliberately not touched.

## Self-Check: PASSED

- All 3 modified files present on disk.
- SUMMARY.md present.
- Task commits `be3caf4` + `1eaf81c` present in git history.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-20*
