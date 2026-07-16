---
phase: 08-device-uat-milestone-closeout
plan: 06
subsystem: chat
tags: [telegram, reactions, tapi, reconcile, tombstone, unity, csharp]

# Dependency graph
requires:
  - phase: 05-telegram-parity (05-06)
    provides: "Telegram receive-side reactions[] map + TelegramReactionMerge optimistic-me reconcile"
  - phase: 08-device-uat-milestone-closeout (08-04)
    provides: "open-chat 3s live poll — reactions reconcile now runs every poll cycle"
provides:
  - "TelegramReactionCatalog: pure Telegram-allowed reaction set (AllowedSet/IsAllowed/QuickEmojis/FilterCategories)"
  - "Channel-gated reaction quick-bar + '+' picker (Telegram-allowed only; WhatsApp byte-identical)"
  - "Clean HTTP 400 revert of BOTH the pill and the chat-list preview"
  - "Removal tombstone in TelegramReactionMerge so a removed own reaction never resurrects"
affects: [08-10]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Channel-gated emoji source read at click/render time (never cached in an Awake closure)"
    - "Empty-emoji 'me' removal tombstone in the pure reconcile seam (aligns with ReactionEvent.IsRemoval)"

key-files:
  created:
    - Assets/Scripts/Chat/TelegramReactionCatalog.cs
    - Assets/Tests/Editor/Chat/TelegramReactionCatalogTests.cs
    - Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs
  modified:
    - Assets/Scripts/Chat/ReactionBarController.cs
    - Assets/Scripts/Chat/EmojiPickerController.cs
    - Assets/Scripts/Chat/TelegramReactionMerge.cs
    - Assets/Scripts/Chat/ReactionSummary.cs
    - Assets/Scripts/Main/ChatManager.ReactionSend.cs
    - Assets/Scripts/UI/MessageItemView.cs
    - Assets/Tests/Editor/Chat/ReactionSummaryTests.cs

key-decisions:
  - "Tombstone shape = empty-emoji 'me' MessageReaction (no new bool field) — least-invasive, reuses the existing empty-emoji=removal convention"
  - "Allowed set = Telegram's documented standard free reaction set (starting point); re-confirm against a live capture at 08-10"
  - "Telegram quick 6 swaps WhatsApp's invalid 😂→😁 and 😮→🔥"

patterns-established:
  - "Reaction emoji source is ActiveChannel-gated; WhatsApp path unchanged"
  - "Empty-emoji reaction entries never render or count (ReactionSummary + clearance)"

requirements-completed: []

# Metrics
duration: 21min
completed: 2026-07-16
---

# Phase 8 Plan 06: Telegram Reaction Set + Removal Tombstone (D1 + D2) Summary

**Telegram reactions now draw only from tapi's allowed set (no REACTION_INVALID), a 400 reverts both the pill and the chat-list row, and a removed own reaction stays removed via an empty-emoji "me" tombstone in the reconcile — WhatsApp byte-identical.**

## Performance

- **Duration:** ~21 min
- **Started:** 2026-07-16T10:58:17Z
- **Completed:** 2026-07-16T11:19:00Z
- **Tasks:** 2 (both TDD)
- **Files modified:** 10 (3 created + 7 modified, + 3 .meta)

## Accomplishments

- **D1 (REACTION_INVALID):** new pure `TelegramReactionCatalog` (allowed set + `IsAllowed` with VS16 normalization + TG-safe quick 6 + `FilterCategories()`); the quick-bar and "+" picker source their emoji from it when `ActiveChannel == Telegram`. The picker rebuilds per channel (`_builtForChannel`) so it is never a stale WhatsApp grid; the bar reads the channel-correct array at click/render time so a switch is reflected without re-wiring buttons.
- **D1 belt-and-suspenders:** `PostReactionRoutine` now reverts the chat-list preview too on `!ok` (mirrors the existing pill revert) — no stuck "You reacted …" row after a 400.
- **D2 (removal never clears):** `TelegramReactionMerge.Merge` gained a removal-tombstone branch; `StampRemovalTombstone` leaves a fresh empty-emoji "me" marker on a Telegram toggle-off (persisted), which the merge reads to suppress tapi's stale "me" echo within the 90s grace window.
- WhatsApp reaction UX byte-identical (all new behavior gated on `ActiveChannel == Telegram` / the empty-emoji tombstone the WhatsApp path never produces).
- Full EditMode suite **1063/1063 green, FRESH** (1043 baseline + 20 new tests).

## Confirmed D2 resurrection path

Diagnosed before implementing (path (b), as the plan hypothesized): a bare removal calls `ReactionStore.ApplyToMessage` with an empty-emoji event → `RemoveAt(idx)` deletes the owner's "me" entry entirely, leaving **no trace**. On the next poll, `TelegramReactionMerge.Merge` finds `FindMine(cached) == null`, so it returns the server list verbatim — but tapi keeps echoing the owner's reaction for a cycle after a successful removal, so it re-appears. There was no marker to tell Merge "just removed" from "never reacted." The fix distinguishes them with a fresh empty-emoji "me" tombstone the merge recognizes and suppresses.

## Allowed-set source

Telegram's documented **standard free reaction set** (👍 👎 ❤️ 🔥 🥰 👏 😁 … 😡), stored in the same fully-qualified unicode form `ReactionEmojiCatalog` uses so filtered emoji render identically. `IsAllowed` normalizes a trailing VS16 (U+FE0F) so the base and qualified forms both match. **This is a starting point** and is explicitly flagged in-code to be re-confirmed against a live capture at 08-10 (custom-reaction chats are deliberately not detected — the clean 400 revert covers them).

## Tombstone shape chosen

An empty-emoji `MessageReaction` with `reactorKey == "me"`, `emoji == ""`, `time == removalTapUnix` — **not** a new `removed` bool field. Rationale: least-invasive (no model field to thread through persistence/rendering), and it reuses the codebase's existing convention that an empty emoji means removal (`ReactionEvent.IsRemoval => string.IsNullOrEmpty(emoji)`, and `ReactionSummary`/`ReactionParser` already treat empty emoji as "no reaction"). Survives `JsonUtility` persistence with zero schema change.

## Task Commits

Each task followed TDD (test → feat):

1. **Task 1 (D1) — Telegram-allowed set + clean 400 revert**
   - `7fb0445` test(08-06): failing catalog tests (RED)
   - `bdec75c` feat(08-06): TelegramReactionCatalog + channel-gated bar/picker + preview revert (GREEN)
   - `32bde85` fix(08-06): explicit cast on picker category source (compile-safety)
2. **Task 2 (D2) — removal tombstone**
   - `b0ea849` test(08-06): removal-tombstone reconcile tests (RED)
   - `8d5e5aa` feat(08-06): Merge removal branch + StampRemovalTombstone + SendReaction wiring + render-consistency (GREEN)

**Supporting:** `20e65a2` chore(08-06): Unity .meta for the 3 new files (generated by the bridge's refresh).

## Files Created/Modified

- `Assets/Scripts/Chat/TelegramReactionCatalog.cs` — pure allowed-set (AllowedSet/IsAllowed/QuickEmojis/FilterCategories), no UnityEngine
- `Assets/Scripts/Chat/ReactionBarController.cs` — `ActiveQuickEmojis` channel gate; quick 6 read at click/render time
- `Assets/Scripts/Chat/EmojiPickerController.cs` — per-channel picker build (`_builtForChannel`) from `FilterCategories()` on Telegram
- `Assets/Scripts/Chat/TelegramReactionMerge.cs` — removal-tombstone branch in `Merge` + `StampRemovalTombstone`
- `Assets/Scripts/Main/ChatManager.ReactionSend.cs` — tombstone stamp on Telegram toggle-off; chat-list preview revert on 400
- `Assets/Scripts/Chat/ReactionSummary.cs` — count excludes empty-emoji entries (tombstone-safe)
- `Assets/Scripts/UI/MessageItemView.cs` — pill clearance follows visible emoji, not raw list count
- `Assets/Tests/Editor/Chat/TelegramReactionCatalogTests.cs` — 9 tests
- `Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs` — 9 tests
- `Assets/Tests/Editor/Chat/ReactionSummaryTests.cs` — +2 empty-emoji-exclusion lock tests

## Decisions Made

- Tombstone as empty-emoji "me" entry (see "Tombstone shape chosen").
- Telegram quick 6 = `{👍 ❤️ 😁 🔥 😢 🙏}` (swaps the two WhatsApp offenders).
- Picker offers `catalog ∩ allowed`; allowed emoji not present in the catalog simply aren't offered (v1 — the quick 6 covers the common ones).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Empty-emoji tombstone must not inflate the pill count**
- **Found during:** Task 2 (removal tombstone)
- **Issue:** With a tombstone present alongside another reactor (e.g. `[{other,❤},{me,""}]`), `ReactionSummary.Build` returned `count = reactions.Count = 2`, so the pill would read "❤ 2" instead of "❤" during the ≤3s tombstone window.
- **Fix:** `Build` now counts only entries with a non-empty emoji (the emoji list already skipped empties). WhatsApp lists never contain empty entries, so byte-identical there.
- **Files modified:** Assets/Scripts/Chat/ReactionSummary.cs (+ 2 lock tests in ReactionSummaryTests.cs)
- **Verification:** New `Build_EmptyEmojiTombstone_ExcludedFromEmojisAndCount` + `Build_LoneTombstone_IsEmpty`; full suite green.
- **Committed in:** 8d5e5aa

**2. [Rule 2 - Missing Critical] Lone tombstone must not reserve empty pill clearance**
- **Found during:** Task 2 (removal tombstone)
- **Issue:** `MessageItemView.RenderReactions` gated clearance on `reactions.Count > 0`, so a lone tombstone `[{me,""}]` (pill hidden) would still reserve empty vertical space below the bubble.
- **Fix:** Clearance now follows `ReactionSummary.Build(...).emojis.Count > 0` (visible reactions), matching the pill's own visibility rule. WhatsApp byte-identical.
- **Files modified:** Assets/Scripts/UI/MessageItemView.cs
- **Verification:** Covered by the reconcile behavior + full suite green; device confirm rides 08-10.
- **Committed in:** 8d5e5aa

**3. [Rule 3 - Blocking] Explicit cast on the picker's WhatsApp category branch**
- **Found during:** Task 1 (picker channel gate)
- **Issue:** The category-source ternary relied on a target-typed conditional (`IReadOnlyList` vs `Category[]`); rather than depend on the C# language version, made it explicit.
- **Fix:** Cast `ReactionEmojiCatalog.Categories` to `IReadOnlyList<Category>` in the ternary.
- **Files modified:** Assets/Scripts/Chat/EmojiPickerController.cs
- **Verification:** Suite compiled + green.
- **Committed in:** 32bde85

---

**Total deviations:** 3 auto-fixed (2 missing-critical render-consistency for the tombstone, 1 blocking/compile-safety)
**Impact on plan:** Both render-consistency fixes are required by the tombstone I introduced (empty-emoji entries must never count or reserve space). No scope creep — WhatsApp remains byte-identical.

## Threat Register Outcomes

- **T-08-06-01 (mitigate):** DONE — `PostReactionRoutine` reverts both the pill and the chat-list preview on `!ok`.
- **T-08-06-02 (mitigate):** DONE — `TelegramReactionCatalog.AllowedSet`/`IsAllowed`/`FilterCategories` constrain selection before any POST.
- **T-08-06-03 (mitigate):** DONE — the fresh removal tombstone suppresses the stale "me" echo within the grace window.
- **T-08-06-04 (accept):** unchanged — tombstone is scoped to `MeReactorKey`; a test asserts another user's same-emoji reaction is never consumed.

## Issues Encountered

None. TDD RED was written test-first but not observed as a red bar locally (brand-new `.cs` are invisible to the open Editor until a refresh; headless is refused while the Editor holds the lock). The final in-Editor bridge run (which does an `AssetDatabase.Refresh` first) compiled all new files and ran the full suite.

## Verification

- **In-Editor bridge, FRESH:** `overall: Passed`, `total: 1063`, `passed: 1063`, `failed: 0`, `editorAssemblyWrittenUtc: 2026-07-16T11:18:21Z` (postdates every `.cs` edit). Baseline 1043 + 20 new tests = 1063 (no regressions).
- All plan `grep` acceptance checks pass for both tasks.
- **Device re-verify (08-10):** B9 add on Telegram succeeds (no REACTION_INVALID); B13 remove clears and stays cleared across polls.

## Next Phase Readiness

- D1 + D2 code-complete and unit-verified; ready for the consolidated device re-verify at 08-10.
- The allowed set is a documented starting point — confirm against a live capture at 08-10 and widen/narrow if a standard emoji still 400s (or a custom-reaction chat needs more).

## Self-Check: PASSED

- All created files present (TelegramReactionCatalog.cs, TelegramReactionCatalogTests.cs, TelegramReactionMergeTests.cs, 08-06-SUMMARY.md).
- All task commits present (7fb0445, bdec75c, 32bde85, b0ea849, 8d5e5aa, 20e65a2).

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-16*
