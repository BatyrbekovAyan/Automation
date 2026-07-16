---
phase: 08-device-uat-milestone-closeout
fixed_at: 2026-07-16T13:50:28Z
review_path: .planning/phases/08-device-uat-milestone-closeout/08-REVIEW.md
iteration: 1
findings_in_scope: 3
fixed: 3
skipped: 0
status: all_fixed
---

# Phase 8: Code Review Fix Report

**Fixed at:** 2026-07-16T13:50:28Z
**Source review:** .planning/phases/08-device-uat-milestone-closeout/08-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 3 (fix_scope: critical_warning — 0 Critical, 3 Warning; 6 Info findings out of scope)
- Fixed: 3
- Skipped: 0

**Suite verification (in-Editor bridge):** 1093/1093 EditMode PASSED, 0 failed, fresh — `editorAssemblyWrittenUtc 2026-07-16T13:49:10Z` postdates the last .cs edit (13:47:18Z), so this is not a stale-green. Baseline was 1091/1091; the +2 delta is exactly the two new WR-03 tests.

## Fixed Issues

### WR-01: `ClearAllLocalHistory` kills the D5 live poll for the rest of the session

**Files modified:** `Assets/Scripts/Main/ChatManager.PrivacyClear.cs`, `Assets/Scripts/Main/ChatManager.cs`, `Assets/Scripts/Main/ChatManager.LivePoll.cs`
**Commit:** bea3453
**Applied fix:** `ClearAllLocalHistory()` now mirrors the SetActiveBot/SetActiveChannel post-`StopAllCoroutines()` block: added `ClearResolveQueues()` (the adjacent pre-existing gap — killed quote/reaction drain workers left `_*ResolveDraining` stuck true) and the D5 live-poll re-kick (`StopCoroutine(_livePollRoutine)` guard + `StartCoroutine(OpenChatLivePollRoutine())`). The re-kick placement before the file deletes is safe: the poll routine yields immediately (`WaitForSecondsRealtime(1f)` is its first statement) and `_activeChatCache = null` later in the method keeps it inert until a chat reopens. Also updated the two comments that enumerate the re-kick sites (`ChatManager.cs` Start(), `_livePollRoutine` field doc) to include the third site.
**Verification note:** compile + suite green; the poll-survives-privacy-clear behavior itself is scene/lifecycle-dependent (not EditMode-testable) — covered by the phase's owner device re-verify (Профиль → Конфиденциальность → clear, then confirm live messages still land in an open chat).

### WR-02: Live poll keeps issuing messages/get while the chat screen is hidden by a tab switch

**Files modified:** `Assets/Scripts/Main/ChatManager.LivePoll.cs`
**Commit:** 37135a3
**Applied fix:** `chatIsOpen` now gates on `MessageListPanel.activeInHierarchy` instead of `activeSelf`. Verified the mechanism before fixing: `BottomTabManager.cs:245` hides screens via `tab.screenPanel.SetActive(isActive)` on the whole screen panel, leaving `MessageListPanel`'s own `activeSelf` true. `activeInHierarchy` strictly tightens the gate (false in every state `activeSelf` was false, plus the tab-away case), so the poll pauses on tab-away and resumes on return. Channel-agnostic — the gate is identical on WhatsApp and Telegram, as designed. The pre-existing `activeSelf` at `ParseChatsJson` line ~300 (notification-cue heuristic) was left alone per the review's explicit out-of-scope note.
**Verification note:** compile + suite green; the pure gate (`OpenChatLivePollGateTests`) is unaffected — the panel-visibility predicate is Unity-scene behavior, covered by the device re-verify (open chat → tap «Сводка»/«Боты»/«Профиль» → no 3 s messages/get churn).

### WR-03: D2 removal tombstone consumed by first reconcile — 90 s grace window was one 3 s poll cycle

**Files modified:** `Assets/Scripts/Chat/TelegramReactionMerge.cs`, `Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs`
**Commit:** 8d3d084
**Applied fix:** The fresh-tombstone branch in `Merge` now carries the tombstone into the result (`result.Add(mine)` after suppressing the server's stale "me" echo), so every reconcile within the 90 s grace window keeps suppressing at the D5 3 s poll cadence. Post-grace the next merge drops it naturally (server list wins; a tombstone is never in the server list) — pinned by the new `Merge_AgedTombstone_NoServer_DropsNaturally` test. Updated the `Merge` contract doc (a tombstone-only result is returned as-is, non-null; null still means "all reactions removed" post-grace).

Test updates (deliberate, same commit — five tests pinned the consumed-tombstone behavior, not just the two the review named):
- `Merge_LoneFreshRemoval_NoServer_IsNull` → renamed `Merge_LoneFreshRemoval_NoServer_KeepsInvisibleTombstone`, expects the tombstone-only list
- `StampThenMerge_EndToEnd_RemovedReactionStaysRemoved` → expects tombstone-only list, asserts `ReactionSummary.Build(...).count == 0`
- `Merge_FreshRemoval_SuppressesServerEcho_NoResurrection` → expects tombstone-only list + invisible render
- `Merge_FreshRemoval_WithOtherReactor_DropsOnlyMe` → renamed `..._DropsOnlyMyEcho`, expects other reactor + carried tombstone, only ❤ visible
- `Merge_OtherUserSameEmoji_NotConsumedByTombstone` → expects other's 👍 + carried tombstone
- NEW: `Merge_TwoSuccessivePolls_TombstoneKeepsSuppressing_NoResurrection` (the review-requested two-reconciles case)
- NEW: `Merge_AgedTombstone_NoServer_DropsNaturally` (no immortal marker)

**Display-safety claim verified before relying on it** (per review + fix instructions):
- `ReactionSummary.Build` skips empty-emoji entries and excludes them from the reactor count (`ReactionSummary.cs:16-25`) — holds.
- `MessageItemView.RenderReactions` (lines 4655-4665) bases clearance on visible emoji ("a lone removal tombstone hides the pill, must not reserve empty space") — holds.
- `ReactionPillView.Render` hides the pill when `Build(...).emojis.Count == 0` — holds.
- IN-05 (`ReactionPillView.HasReactions` counts tombstones): verified it only gates the `OnEmojiReady` re-render path, and that re-render calls `Render(_last)` which re-hides correctly (the `activeSelf` guard makes it a no-op on an already-hidden pill). Benign with the persisting tombstone — a wasted no-op call at worst, no visual effect — so no extension of the fix was needed. IN-05 itself is Info scope, not fixed here.

WhatsApp byte-identical invariant confirmed: both `TelegramReactionMerge` entry points stay Telegram-gated — `RefreshCachedMessageReactions`'s caller gates on `ActiveChannel == Telegram` (`ChatManager.cs:1784-1785`), and `StampRemovalTombstone` is gated at `ChatManager.ReactionSend.cs:52`. Also checked `TelegramReactionReceiveTests` (the other `Merge` test consumer): none of its merge cases place a tombstone in the cached list, so none pin the changed behavior.

## Out-of-Scope Findings (not addressed)

IN-01 through IN-06 are Info severity, outside the `critical_warning` fix scope. Notable for the phase: IN-01 (VS16 normalization) is deliberately deferred to the 08-10 device capture per the review's own guidance; IN-05 became less load-bearing than feared (verified benign above) but its suggested one-liner (`HasReactions => ReactionSummary.Build(_last).emojis.Count > 0`) remains a reasonable consistency cleanup for a later pass.

---

_Fixed: 2026-07-16T13:50:28Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
