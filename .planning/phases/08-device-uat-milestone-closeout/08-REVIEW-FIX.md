---
phase: 08-device-uat-milestone-closeout
fixed_at: 2026-07-17T09:07:18Z
review_path: .planning/phases/08-device-uat-milestone-closeout/08-REVIEW.md
iteration: 1
findings_in_scope: 3
fixed: 3
skipped: 0
status: all_fixed
---

# Phase 8: Code Review Fix Report — Round 2 (gap-closure delta)

**Fixed at:** 2026-07-17T09:07:18Z
**Source review:** .planning/phases/08-device-uat-milestone-closeout/08-REVIEW.md (round-2 review, 2026-07-17; supersedes round 1)
**Iteration:** 1 (first fix pass against the round-2 review; this file overwrites the round-1 report, preserved in git history at commit 1b2e60b)

**Summary:**
- Findings in scope: 3 (WR-04, WR-05, WR-06 — fix_scope: critical_warning; IN-07/IN-08 out of scope)
- Fixed: 3
- Skipped: 0

**Suite verification (in-Editor bridge):** 1124/1124 EditMode PASSED, 0 failed, fresh — `editorAssemblyWrittenUtc 2026-07-17T09:05:49Z` postdates the last .cs edit; `Assembly-CSharp(.Editor).dll` mtimes advanced past the trigger drop, `status: completed`, `finishedAt` advanced. Baseline was 1121/1121; the +3 delta is exactly the three new WR-04 heart-on-fire tests. Run duration 23.4 s.

## Fixed Issues

### WR-04: VS16 normalizer misses mid-sequence FE0F (heart-on-fire)

**Files modified:** `Assets/Scripts/Chat/ReactionEmoji.cs`, `Assets/Tests/Editor/Chat/ReactionEmojiTests.cs`
**Commit:** 4a4bc60
**Applied fix:** `ReactionEmoji.CompareKey` now strips EVERY U+FE0F occurrence (fast path returns unchanged when none present; strip via a cached `VariationSelector16String` — `string.Replace` has no char-remove overload, and the cached field is deliberately declared BEFORE `Requalify` because static field initializers run in textual order and `BuildRequalify` calls `CompareKey`). Because `BuildRequalify` keys by `CompareKey`, this one change also fixes `Canonical` requalification of tapi's base-form heart-on-fire echo, exactly as the review predicted. Doc comments on the const, `CompareKey`, `SameEmoji`, and `BuildRequalify` updated from "trailing" to "all occurrences".

**Independently re-verified before applying:** the review's collision-free claim was re-checked against the live catalog with a script — 73 entries, 73 unique base forms after a full strip; heart-on-fire (`U+2764 U+FE0F U+200D U+1F525`) confirmed as the ONLY mid-sequence-FE0F entry (the two shrug variants carry it trailing).

**Tests added (review-requested pin):** `SameEmoji_BaseAndQualifiedHeartOnFire_AreEqual`, `CompareKey_DropsMidSequenceVariationSelector`, `Canonical_BaseHeartOnFire_RequalifiesToDisplayForm`. The two heart-on-fire forms are composed from the existing annotated `BaseHeart`/`QualifiedHeart` constants plus an explicit `((char)0x200D)` ZWJ so the invisible base-vs-qualified difference stays reviewable (no raw invisible code points typed into source).

**Deliberately NOT touched:** the review's parenthetical on `TelegramReactionCatalog.StripVariationSelector` sharing the trailing-only limitation on the `IsAllowed` side — the reviewer explicitly marked it "outside this delta", and `IsAllowed`'s inputs are the picker/bar's fully-qualified catalog forms, so it is not on the D2 echo path. Remains open alongside the 08-16 device capture that will confirm tapi's echoed form for ZWJ reactions.

### WR-05: Persisted Telegram owner-id never loaded on cold start

**Files modified:** `Assets/Scripts/Main/ChatManager.BotState.cs`, `Assets/Scripts/Main/ChatManager.Channel.cs`, `Assets/Scripts/Main/ChatManager.cs`
**Commit:** fdc0c55
**Applied fix:** Adopted the review's suggested helper extraction: new `ReloadTgOwnUserIdFor(string botId)` in `ChatManager.BotState.cs` (resolves the bot, reads the TELEGRAM profile id explicitly — never `GetActiveProfileId()` — and loads `_tgOwnUserId` via `LoadPersistedTgOwnUserId`). All three rebind sites now call it:
- `SetActiveBot` (BotState.cs) — inline 3 lines replaced with the helper call;
- `SetActiveChannel` (Channel.cs) — same;
- `InitializeActiveBotNextFrame` (BotState.cs) — the previously-missing cold-start load, placed after `ActiveChannel = ResolveChannelForBot(CurrentBotId)` and before `OnActiveBotChanged`/`BeginLoadForActiveBot`, so the initial load's Normalize path sees the persisted id.

`LoadPersistedTgOwnUserId` now has exactly one caller (the helper), no orphaned locals remain at the two switch sites, and the `_tgOwnUserId` field doc in `ChatManager.cs` now names all three rebind sites. The learn-site comment's "survives app relaunches" claim is now true.

**Verification note:** compile + suite green; the fix is a mechanical mirror of the two already-shipping switch-site loads, but the cold-start path itself is scene/lifecycle-coupled and not EditMode-testable. The 08-16 device pass should include the exact D2-B repro after a full app relaunch (react in a TG chat with no own rows loaded, first session after relaunch) to confirm no phantom «2».

### WR-06: Sync-pill channel-switch catch-up strands a permanent pill

**Files modified:** `Assets/Scripts/UI/ChatListSyncIndicator.cs`
**Commit:** e17973e
**Applied fix:** Removed the H3 catch-up line in `HandleActiveChannelChanged` (`if (... IsChatListSyncing) BeginSpin();`); the handler now only hides on a switch away from Telegram. Replaced the method comment with the full rationale (why no catch-up, why the connected case is covered, why the unconnected case would strand the pill) plus a note distinguishing the still-correct `OnEnable` catch-up, which is untouched.

**Scope-note verification performed before removal — the reviewer read the flow correctly:**
1. `OnActiveChannelChanged` fires from exactly ONE site (`ChatManager.Channel.cs:76`), always two lines before `StopAllCoroutines()` + `_chatListSyncing = false` — and a killed `SyncAllChats` never runs its `finally`, so no `OnChatListSyncEnd` ever fires for the observed sync (project-established, documented at three separate sites in the codebase). Any sync visible at the event moment is therefore always the doomed OLD channel's sync.
2. Connected-TG switches lose nothing: `SyncAllChats` (ChatManager.cs:451-454) sets `_chatListSyncing = true` and fires `OnChatListSyncStart` BEFORE its first yield, and `StartCoroutine` runs that prologue synchronously — so `BeginLoadForActiveBot → SyncAllChats` re-shows the pill inside the same `SetActiveChannel` call stack.
3. The stuck-pill path is real and reachable: `ChannelSwitcherView` deliberately keeps unconnected chips tappable (SWITCH-02, ChannelSwitcherView.cs:94-95), and for an unconnected Telegram profile `BeginLoadForActiveBot` early-returns with `OnEmptyState` — no sync start, no end, nothing to ever hide the pill. `DashboardPage.cs:423` is a second `SetActiveChannel` caller with identical choreography.

**Verification note:** compile + suite green; the pure floor math is unchanged and still pinned by `ChatListSyncIndicatorGateTests`. The pill lifecycle itself is scene behavior — the 08-16 device pass should confirm both directions: tap the unconnected Telegram chip mid-WhatsApp-sync (no stuck pill over «Подключите Telegram») and a connected TG switch (pill still appears via the same-stack sync start).

## Out-of-Scope Findings (not addressed)

IN-07 and IN-08 are Info severity, outside the critical_warning fix scope. Notable for the phase: IN-07 (tombstone cannot suppress an un-mapped own echo) is a bounded 3–6 s self-healing transient made rarer by the WR-05 fix — per the review, do not mistake it for a D2 regression during the 08-16 device pass. IN-08 (Editor-only `CodePointHex` throws on lone surrogates) affects only the Editor diagnostics logger.

---

_Fixed: 2026-07-17T09:07:18Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
