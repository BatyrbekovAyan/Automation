---
phase: 08-device-uat-milestone-closeout
plan: 11
subsystem: ui
tags: [telegram, reactions, tapi, emoji, vs16, playerprefs, editmode-tests, gap-closure]

# Dependency graph
requires:
  - phase: 08 (08-06)
    provides: TelegramReactionMerge tombstone reconcile, TelegramReactionCatalog allowed set, TelegramReactionMapper owner-id mapping
  - phase: 05 (05-06)
    provides: receive-side tapi reactions[] map + optimistic 'me' preservation seam
provides:
  - Shared ReactionEmoji canonical-emoji seam (CompareKey / SameEmoji / Canonical) threaded through every reaction equality/dedup/display point
  - Per-Telegram-profile persistence of the owner user-id (survives bot/channel switch + relaunch)
  - Same-canonical-emoji fold that collapses an un-mapped own echo into 'me' (fixes count-«2» symptom 1)
  - Editor-only reaction-echo code-point log for the 08-16 device capture
affects: [08-16 device re-verify, telegram reaction UX, whatsapp reaction parity]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "One shared canonical-emoji normalizer threaded at every raw-string reaction seam (compare-only; display keeps the qualified sprite-renderable form)"
    - "Per-profile identity persistence loaded at the SetActiveBot/SetActiveChannel reset choreography using the explicit Telegram id (ActiveChannel still holds the old channel at reset)"

key-files:
  created:
    - Assets/Scripts/Chat/ReactionEmoji.cs
    - Assets/Tests/Editor/Chat/ReactionEmojiTests.cs
  modified:
    - Assets/Scripts/Chat/TelegramReactionMapper.cs
    - Assets/Scripts/Chat/ReactionSummary.cs
    - Assets/Scripts/Chat/TelegramReactionMerge.cs
    - Assets/Scripts/Chat/OutgoingReaction.cs
    - Assets/Scripts/Chat/ReactionBarController.cs
    - Assets/Scripts/Main/ChatManager.cs
    - Assets/Scripts/Main/ChatManager.BotState.cs
    - Assets/Scripts/Main/ChatManager.Channel.cs

key-decisions:
  - "Canonical DISPLAY form is fully-qualified (❤️), never the stripped base — the TMP sprite name needs -fe0f (emoji-sprite-tag-naming gotcha)"
  - "The same-emoji fold reinterprets the 05-06 WR-01 'keep both' case: a same-canonical-emoji numeric-keyed server entry, when a fresh optimistic 'me' is present, is the owner's own un-mapped echo — folded into 'me'"
  - "Owner-id persistence is the primary root-cause-B fix; the fold is belt-and-suspenders for the first-ever reaction before any own row loads"

patterns-established:
  - "ReactionEmoji.CompareKey/SameEmoji at every reaction comparison; ReactionEmoji.Canonical only at the tapi ingest seam (WhatsApp stays byte-identical, compare-only)"

requirements-completed: []

# Metrics
duration: 13min
completed: 2026-07-17
---

# Phase 8 Plan 11: Telegram Reaction-Identity Fix (D2 refined) Summary

**One shared VS16-canonical-emoji seam + per-Telegram-profile owner-id persistence + a same-emoji fold that make an own Telegram reaction read as ONE reaction (no count «2», no stale pill, no double-heart) — WhatsApp byte-identical, 1105/1105 EditMode green FRESH.**

## Performance

- **Duration:** ~13 min (commit span; ~20 min incl. capture analysis + bridge run)
- **Started:** 2026-07-17T06:49Z
- **Completed:** 2026-07-17T07:02Z
- **Tasks:** 2 (both TDD: RED → GREEN)
- **Files modified:** 13 (2 created) + 2 new .meta

## Root Cause — CONFIRMED from the read-only tapi capture

Two independent bugs converge; both confirmed against `Tools/tapi/samples/`:

- **A — emoji-FORM mismatch.** `messages_5258918241.json`'s reaction glyph is the raw bytes `e2 9d a4` = **U+2764 base heart `❤` (NO variation selector)**, while the app's quick-bar / `ReactionEmojiCatalog` store the fully-qualified **`❤️` = U+2764 U+FE0F**. Every raw-string compare across that seam missed (dedup rendered two hearts; `Key`/`SameReactions`, toggle-off, quick-bar highlight all diverged).
- **B — owner-IDENTITY timing.** `messages_725195588.json` shows the owner's own reactions keyed by **`user_id: "1038376805"` — exactly the `from` on the owner's `fromMe` rows** (the learned `_tgOwnUserId`). So the id MATCHES when learned; the defect is that `_tgOwnUserId` was reset to null on every bot/channel switch and re-learned only after an own row Normalized. React with no own message loaded → echo keyed by the numeric id (not `"me"`) → survives ALONGSIDE the optimistic `"me"` → count «2» (symptom 1) and, on a change, both pills (symptom 2).

**DIAGNOSE-FIRST (Task 2, as required):**
1. Ids match when learned → persistence is the fix, not an id-form mismatch (capture: `from` and reaction `user_id` are both bare numeric `1038376805`).
2. The un-mapped echo appears ONLY when `_tgOwnUserId` is null at reaction time (`TelegramReactionMapper` sets `isOwn` only when `ownUserId` is non-empty AND `user_id == ownUserId`; the field was stranded null on switch).

## Accomplishments

- **`ReactionEmoji`** (pure, UnityEngine-free): `CompareKey` (drops a trailing U+FE0F), `SameEmoji` (VS16-insensitive ordinal equality), `Canonical` (requalifies base → the sprite-renderable qualified form, built from `TelegramReactionCatalog.AllowedSet`).
- **Canonicalization threaded at every seam (root cause A):** mapper stores `Canonical(reaction)` (kills symptom 3 at source + renders a sprite); `ReactionSummary` dedups by `CompareKey`; `TelegramReactionMerge.Key` gains a U+0001 separator (IN-06) + `CompareKey`; `OutgoingReaction` toggle-off + `ReactionBarController` highlight use `SameEmoji`.
- **Owner-id persistence (root cause B):** learned id persisted per Telegram profile (`{tgProfileId}TgOwnUserId`); both reset sites LOAD it instead of stranding null, reading the Telegram id EXPLICITLY (`ProfileIdForChannel(bot, ChatChannel.Telegram)`) because `ActiveChannel` still holds the old bot's channel at the SetActiveBot reset.
- **Same-emoji fold (belt-and-suspenders):** `TelegramReactionMerge.Merge` folds a single same-canonical-emoji un-mapped server entry into a fresh optimistic `"me"` (collapses count «2» even for the first-ever reaction), scoped strictly to a present fresh `"me"`.
- **Editor capture instrumentation:** `#if UNITY_EDITOR` one-line-per-reaction log of the echo's Unicode code points + `user_id` + current `_tgOwnUserId` — no file write, never in a device build (IN-03).

## Task Commits

1. **Task 1 (D2-A) RED:** `44b732e` (test) — canonical-emoji + cross-form dedup + base→qualified mapper tests
2. **Task 1 (D2-A) GREEN:** `de48f35` (feat) — `ReactionEmoji` + threaded through all 5 seams + Editor log
3. **Task 2 (D2-B) RED:** `ee97391` (test) — fold + owner-identity tests (incl. WR-01 same-emoji reinterpretation)
4. **Task 2 (D2-B) GREEN:** `1be6300` (feat) — persist owner-id + load at both reset sites + Merge fold
5. **Unity meta:** `ab29bd6` (chore) — `.meta` for the 2 new files

## Files Created/Modified

- `Assets/Scripts/Chat/ReactionEmoji.cs` — shared canonical-emoji seam (created)
- `Assets/Scripts/Chat/TelegramReactionMapper.cs` — `Canonical(reaction)` at ingest
- `Assets/Scripts/Chat/ReactionSummary.cs` — dedup by `CompareKey`, still display the qualified glyph
- `Assets/Scripts/Chat/TelegramReactionMerge.cs` — `Key` (U+0001 + CompareKey) + same-emoji fold in `Merge`
- `Assets/Scripts/Chat/OutgoingReaction.cs` — `SameEmoji` toggle-off
- `Assets/Scripts/Chat/ReactionBarController.cs` — `SameEmoji` quick-bar highlight
- `Assets/Scripts/Main/ChatManager.cs` — persist owner-id on learn + `#if UNITY_EDITOR` echo log
- `Assets/Scripts/Main/ChatManager.BotState.cs` — SetActiveBot reset LOADS per-profile owner-id + `LoadPersistedTgOwnUserId`
- `Assets/Scripts/Main/ChatManager.Channel.cs` — SetActiveChannel reset LOADS per-profile owner-id
- `Assets/Tests/Editor/Chat/ReactionEmojiTests.cs` — 6 tests (created)
- `Assets/Tests/Editor/Chat/ReactionSummaryTests.cs` — +1 cross-form dedup test
- `Assets/Tests/Editor/Chat/TelegramReactionReceiveTests.cs` — +1 base→qualified mapper test; WR-01 test reinterpreted
- `Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs` — +4 fold tests (15 total)

## Decisions Made

- **WhatsApp byte-identical:** canonicalization is COMPARE-only for WhatsApp (its stored/displayed strings are already qualified); `Canonical`-at-ingest fires ONLY on the Telegram mapper path.
- **Display keeps the qualified form:** `Canonical` never returns a stripped base — the TMP reaction sprite name needs `-fe0f` (memory: emoji-sprite-tag-naming), else the pill renders a literal text heart.
- **Persistence-first, fold-second:** the persisted id maps the echo to `"me"` upstream (clean single-pill change); the fold only covers the first-ever reaction before any own row loads.

## Deviations from Plan

### Auto-fixed / in-scope adjustments

**1. [Rule 1 - Bug] WR-01 same-emoji test reinterpreted to match the designed fold**
- **Found during:** Task 2 (fold implementation)
- **Issue:** The existing `Merge_OtherUserSameEmoji_DoesNotConsumeMyEntry` (05-06 WR-01) asserted that an optimistic `me:👍` + a numeric-keyed server `👍` keeps BOTH entries — but that exact shape IS symptom 1 (count «2»), and the plan's fold deliberately collapses it. The two are mutually exclusive.
- **Fix:** Renamed to `Merge_SameEmojiUnmappedEcho_FoldedIntoMe` and updated to assert the fold (count 1, one `"me"`), with a comment documenting the reinterpretation and the self-correcting rare stranger-same-emoji case (persisted id maps the real echo to `"me"` ⇒ replace, not fold; different-emoji strangers never folded; nothing folded without a fresh optimistic `"me"` — T-08-11-01).
- **Files modified:** `Assets/Tests/Editor/Chat/TelegramReactionReceiveTests.cs`
- **Verification:** Full suite green (1105/1105); `Merge_MyEmojiNotYetEchoed_KeptAlongsideOthers` (different-emoji) and `Merge_OtherUserSameEmoji_NoOptimisticMe_NotFolded` (no fresh me) both pass, proving the scope guards hold.
- **Committed in:** `ee97391` (Task 2 RED)

**2. [Rule 3 - Blocking] Replaced a raw U+0001 byte in `Key` with the explicit `\u0001` escape**
- **Found during:** Task 1 (threading `CompareKey` into `TelegramReactionMerge.Key`)
- **Issue:** The committed `Key` already carried a RAW U+0001 control byte (invisible in source), not the `\u0001` escape — a code-hygiene hazard and it would not satisfy the plan's `grep "\u0001"` acceptance check.
- **Fix:** Byte-replaced it with the explicit `\u0001` C# escape + added `CompareKey` and an explanatory comment.
- **Files modified:** `Assets/Scripts/Chat/TelegramReactionMerge.cs`
- **Verification:** `grep -q "\\u0001"` passes; `SameReactions` tests green.
- **Committed in:** `de48f35` (Task 1 GREEN)

---

**Total deviations:** 2 (1 test reinterpretation driven by the plan's designed fold, 1 code-hygiene fix). No scope creep — both are consequences of the plan's own design.
**Impact on plan:** None negative; both keep the suite coherent and satisfy the plan's acceptance greps.

## Issues Encountered

- **Unity new-file import + Editor lock:** the Editor was open (headless refused). The in-Editor bridge ticked on focus, RE-COMPILED to import the two new `.cs` files (`editorAssemblyWrittenUtc=2026-07-17T07:00:43Z`, postdating all edits), and ran a FRESH **1105/1105** EditMode green (1093 baseline + 12 new). `.meta` for both new files appeared post-import and were committed.

## Device Capture Ask for 08-16 (exact)

On the authorized dev Telegram profile, with the Editor console open, react + change + remove on a message and capture the `[TG reaction echo]` lines. Confirm:
1. **Form:** the echoed heart logs `U+2764` (base) — proving the app now requalifies it to `❤️` for display/dedup.
2. **Identity:** the reaction's `user_id` equals `ownId=` (the learned/persisted `_tgOwnUserId`) after a fresh app launch WITHOUT opening a chat that has an own message — proving persistence keyed the echo to `"me"`.
3. **Symptoms gone:** own single reaction = one glyph / count 1; changing a reaction leaves ONE pill; adding ❤️ next to an existing heart renders ONE heart; removal stays removed (WR-03 preserved).

## Next Phase Readiness

- D2 (refined) code-closed; rides the 08-16 device re-verify checkpoint alongside D9/D10/D11/D12.
- WhatsApp reaction UX byte-identical (compare-only normalization; no numeric-keyed own-echo path exists on WhatsApp).

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-17*

## Self-Check: PASSED

- Created files verified on disk: `ReactionEmoji.cs` (+.meta), `ReactionEmojiTests.cs` (+.meta), `08-11-SUMMARY.md`.
- Commits verified in git: `44b732e`, `de48f35`, `ee97391`, `1be6300`, `ab29bd6`.
- EditMode suite: 1105/1105 PASSED FRESH (`editorAssemblyWrittenUtc=2026-07-17T07:00:43Z`, postdates all edits).

## TDD Gate Compliance

Both tasks followed RED → GREEN with atomic commits (no unexpected pass in RED — the
new-type/new-behavior tests could not resolve until GREEN): test(08-11) `44b732e` → feat(08-11)
`de48f35` (Task 1); test(08-11) `ee97391` → feat(08-11) `1be6300` (Task 2). No REFACTOR needed.
