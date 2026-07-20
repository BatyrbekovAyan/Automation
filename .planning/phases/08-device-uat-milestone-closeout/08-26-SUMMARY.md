---
phase: 08-device-uat-milestone-closeout
plan: 26
subsystem: ui
tags: [reactions, telegram, tmpro, unicode, surrogate, coroutine, d2-view]

# Dependency graph
requires:
  - phase: 08 (08-22)
    provides: RefreshReactionsVisual + ReactionPillView.ForceReRender (the hardened one-frame-deferred re-render, previously bar-dismiss only)
  - phase: 08 (08-17)
    provides: poll-path reaction reconcile (RefreshCachedMessageReactions → OnMessageReactionsChanged, one-shot per change via SameReactions dedup)
provides:
  - Poll-driven HandleReactionsChanged now routes through the same hardened one-frame-deferred re-render (channel-agnostic, idempotent)
  - Lone/unpaired-surrogate loop-entry guard in UnicodeEmojiConverter (throw-safe walk)
  - try/catch count-only fallback in ReactionPillView.Render (a bad emoji payload can never abort the multicast or the sync coroutine)
  - Discriminating [D2-view] post-render state log (active/len/culled) for the round-5 device pass
affects: [08-29 owner re-verify, D2-view, reaction pipeline]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Poll-path reaction repaint reuses the bar-dismiss hardened re-render (RefreshReactionsNextFrame → yield null → RefreshReactionsVisual)"
    - "Loop-entry surrogate guard: emit stray surrogate raw + advance 1 char so char.ConvertToUtf32 never throws"
    - "Diagnostic getters (id/booleans/length only) drive a capped device state log — no emoji/body content"

key-files:
  created: []
  modified:
    - Assets/Scripts/Chat/UnicodeEmojiConverter.cs
    - Assets/Scripts/UI/ReactionPillView.cs
    - Assets/Scripts/UI/MessageItemView.cs
    - Assets/Tests/Editor/Chat/UnicodeEmojiConverterPatchTests.cs

key-decisions:
  - "The 08-22 fix was correct but attached to the wrong trigger; round 5 reuses it verbatim on the poll path rather than building a new heal"
  - "Loop-entry guard covers both review test inputs (both land at loop entry); the lookahead reads at 89/106 are left untouched — the pill try/catch is the belt-and-suspenders net for any exotic mid-sequence surrogate"
  - "Diagnostic log kept compiled (not #if) for one more UAT round by design (IN-04), capped to id + booleans/length"

patterns-established:
  - "Pattern: a one-shot data event (dedup-guarded) that drives a fragile same-frame repaint must defer the hardened re-render one frame so a transient loss self-heals"

requirements-completed: []

# Metrics
duration: ~20min
completed: 2026-07-20
---

# Phase 8 Plan 26: D2-view Poll-Path Re-render + Converter Hardening Summary

**Poll-driven reaction repaints now route through the 08-22 hardened one-frame-deferred re-render (the fix that only ever ran on bar-dismiss), plus a lone-surrogate loop-entry guard + pill try/catch so a malformed emoji payload can never abort the OnMessageReactionsChanged multicast or the SyncLatestMessages coroutine, plus a discriminating [D2-view] state log for the round-5 device pass.**

## Performance

- **Duration:** ~20 min
- **Started:** 2026-07-20T16:29Z (baseline run)
- **Completed:** 2026-07-20T16:40Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- **Task 1 (WR-01, TDD):** `UnicodeEmojiConverter.ConvertRealEmojisToSprites` is now throw-safe on lone/unpaired surrogates — a loop-entry guard emits the stray surrogate raw and advances one char, so `char.ConvertToUtf32` never throws `ArgumentException` on a malformed reaction-emoji payload. 4 new tests (3 lone-surrogate no-throw + 1 valid-pair regression guard). Well-formed text is byte-identical (valid pairs keep the +2 path).
- **Task 2 (CR-01):** `MessageItemView.HandleReactionsChanged` now `StartCoroutine(RefreshReactionsNextFrame(id))` — a one-frame-deferred `RefreshReactionsVisual()` (RenderReactions + `reactionPill.ForceReRender` → SetAllDirty + ForceMeshUpdate), the same hardened re-render the reaction-bar-dismiss path used. Because `OnMessageReactionsChanged` is one-shot per change (SameReactions dedup), this is the only way the poll path can self-heal a same-frame-fragile repaint.
- **WR-01 layer 2:** `ReactionPillView.Render` wraps the converter call in try/catch with a count-only fallback (`sprites = ""`), so even an exotic mid-sequence surrogate can never crash the multicast or kill the live-sync coroutine.
- **Diagnostic:** a one-frame-later `[D2-view] post-render` log reports `active/len/culled` (id + booleans/length only) so a round-5 device FAIL discriminates exception (line never prints) vs RectMask2D cull (culled=true) vs TMP submesh churn (active + len>0 + culled=false).

## Task Commits

1. **Task 1 RED: add failing lone-surrogate tests** — `df75f60` (test)
2. **Task 1 GREEN: harden emoji converter against lone surrogates** — `4a9ac31` (feat)
3. **Task 2: route poll path through hardened re-render + pill try/catch + discriminating log** — `fc02e16` (feat)

_Task 1 was TDD (RED test → GREEN feat); no refactor commit needed._

## Files Created/Modified

- `Assets/Scripts/Chat/UnicodeEmojiConverter.cs` — loop-entry lone/unpaired-surrogate guard (emit raw + advance 1) before `char.ConvertToUtf32`.
- `Assets/Scripts/UI/ReactionPillView.cs` — try/catch (count-only fallback) around `ConvertRealEmojisToSprites`; three diagnostic getters (`DiagnosticActive` / `DiagnosticLabelLength` / `DiagnosticLabelCulled`).
- `Assets/Scripts/UI/MessageItemView.cs` — `HandleReactionsChanged` fires `RefreshReactionsNextFrame(changed.messageId)`; new coroutine yields one frame → `RefreshReactionsVisual()` → discriminating `[D2-view] post-render` state log.
- `Assets/Tests/Editor/Chat/UnicodeEmojiConverterPatchTests.cs` — 4 new tests (`Convert_LoneHighSurrogate_DoesNotThrow_Passthrough`, `Convert_LoneLowSurrogateMidString_DoesNotThrow`, `Convert_LoneLowSurrogateAtStart_DoesNotThrow`, `Convert_ValidSurrogatePairEmoji_StillConverts_AfterGuard`).

## Decisions Made

- Reuse the 08-22 hardened re-render verbatim on the poll path (the fix was correct — it just never ran on the failing path) rather than build a second heal.
- Loop-entry guard only (both 08-REVIEW test inputs land there); the lookahead `char.ConvertToUtf32` reads at 89/106 are left untouched, with the pill try/catch as the belt-and-suspenders net for any exotic mid-sequence surrogate.
- Data layer (`TelegramReactionMerge` / `RefreshCachedMessageReactions` / `SameReactions`) deliberately untouched — owner-confirmed correct (logs always show the right reaction). Verified: reconcile/merge grep count = 0 in both view files.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- The baseline full-suite bridge run took ~7 min (large suite, ~1176 tests) and reported `running` for the first 180s poll window; a second poll window caught completion. Not an error — the EditMode suite genuinely takes minutes to enumerate; filtered runs (`UnicodeEmojiConverter`) completed in seconds.

## Verification

- **Baseline (fresh, before changes):** 1176/1176 EditMode green, editorAssemblyWrittenUtc 12:34:31Z.
- **Task 1 RED (filtered):** 20 total, 17 passed, **3 failed by throwing `ArgumentException` at `UnicodeEmojiConverter.cs:71`** (the 3 lone-surrogate tests); valid-pair regression test passed. editor stamp 16:36:46Z postdates the 16:36:39Z edit.
- **Task 1 GREEN (filtered):** 20/20 green. Runtime freshness confirmed: `Assembly-CSharp.dll` mtime advanced to 16:37:37Z, postdating the 16:37:32Z edit (editor stamp false-stales on runtime-only edits, as documented).
- **Task 2 (full suite):** **1180/1180 green (baseline 1176 + 4), 0 failed.** `Assembly-CSharp.dll` mtime advanced to 16:39:16Z, postdating the ~16:39:12Z edits.
- **Grep assertions:** all Task 1 + Task 2 acceptance greps pass (loop-entry guard present, 4 new tests present, `RefreshReactionsNextFrame` ×2, `[D2-view] post-render` ×1, pill `catch` ×1, `DiagnosticLabelCulled` ×1, data-layer tokens 0/0 in both view files).
- **Post-commit deletion check:** no file deletions across the 3 plan commits.

## WhatsApp Byte-Identical

- The deferred re-render is channel-agnostic + idempotent (re-renders the SAME vm data, only forces a mesh flush); the WhatsApp live path reaches the identical `HandleReactionEvent` → `HandleReactionsChanged` handler, so it self-heals identically.
- The converter guard is a no-op for well-formed text (valid pairs keep the +2 path).
- The pill try/catch only fires on a throw that previously crashed the chain.

## Next Phase Readiness

- Device confirmation rides **08-29** (owner re-verify): a reaction changed IN the Telegram app now repaints the bubble pill on the poll path; if it still fails, the `[D2-view] post-render` log discriminates exception / cull / submesh-churn. WhatsApp unchanged.
- The compiled `[D2-view]` logs (entry + new post-render) are tagged for removal/`#if` gating at phase close (IN-04) — one more UAT round by design.

## Self-Check: PASSED

- SUMMARY.md present.
- All 4 modified files present on disk.
- All 3 task commits (`df75f60`, `4a9ac31`, `fc02e16`) exist in history.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-20*
