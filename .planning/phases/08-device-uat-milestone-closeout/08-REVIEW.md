---
phase: 08-device-uat-milestone-closeout
reviewed: 2026-07-16T13:28:33Z
depth: standard
files_reviewed: 29
files_reviewed_list:
  - Assets/Editor/ChatListSyncIndicatorBuilder.cs
  - Assets/Scripts/Chat/ChatIdFormat.cs
  - Assets/Scripts/Chat/ChatRowSwipePolicy.cs
  - Assets/Scripts/Chat/EmojiPickerController.cs
  - Assets/Scripts/Chat/OpenChatLivePollGate.cs
  - Assets/Scripts/Chat/ReactionBarController.cs
  - Assets/Scripts/Chat/ReactionSummary.cs
  - Assets/Scripts/Chat/SwipeToDelete.cs
  - Assets/Scripts/Chat/TelegramReactionCatalog.cs
  - Assets/Scripts/Chat/TelegramReactionMerge.cs
  - Assets/Scripts/Main/ChatManager.BotState.cs
  - Assets/Scripts/Main/ChatManager.Channel.cs
  - Assets/Scripts/Main/ChatManager.LivePoll.cs
  - Assets/Scripts/Main/ChatManager.ReactionSend.cs
  - Assets/Scripts/Main/ChatManager.cs
  - Assets/Scripts/UI/ChatItemView.cs
  - Assets/Scripts/UI/ChatListSyncIndicator.cs
  - Assets/Scripts/UI/EmptyStateView.cs
  - Assets/Scripts/UI/MessageItemView.cs
  - Assets/Tests/Editor/Chat/ChatIdFormatTests.cs
  - Assets/Tests/Editor/Chat/ChatListDedupTests.cs
  - Assets/Tests/Editor/Chat/ChatRowSwipePolicyTests.cs
  - Assets/Tests/Editor/Chat/OpenChatLivePollGateTests.cs
  - Assets/Tests/Editor/Chat/ReactionSummaryTests.cs
  - Assets/Tests/Editor/Chat/TelegramReactionCatalogTests.cs
  - Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs
  - Tools/n8n/build-suggest-replies.py
  - Tools/n8n/verify-telegram-parity.py
findings:
  critical: 0
  warning: 3
  info: 6
  total: 9
status: issues_found
---

# Phase 8: Code Review Report

**Reviewed:** 2026-07-16T13:28:33Z
**Depth:** standard
**Files Reviewed:** 29
**Status:** issues_found

> Supersedes the 2026-07-15 partial review (2 files, Tools/n8n scripts only, plan 08-02). That
> review's two informational notes are preserved: its IN-01 (argparse exit-2 on malformed CLI,
> fail-closed, no change needed) stands as recorded; its IN-02 (override id bound verbatim,
> by design) is folded into IN-04 below alongside a sharper adjacent edge.

## Summary

Reviewed the six device-UAT gap-closure plans (08-04 open-chat live poll, 08-05 canonical dedup + cross-channel bleed, 08-06 Telegram reaction catalog + removal tombstone, 08-07 video-note presentation, 08-08 swipe policy + lifecycle null-safety, 08-09 RU copy + sync indicator) plus the two Tools/n8n Python scripts from plan 08-02. Overall quality is high: the pure-seam pattern (ChatIdFormat, OpenChatLivePollGate, ChatRowSwipePolicy, TelegramReactionCatalog/Merge) is consistently applied with thorough EditMode coverage; the WhatsApp byte-identical invariant is honored (verified: CanonicalKey verbatim on WhatsApp with pinning tests; reaction bar/picker/tombstone all TG-gated; D9 sync indicator display-gated on the Telegram channel); new network code follows the UnityWebRequest+coroutine pattern with timeout, auth header, explicit Content-Type, and `using`; no secrets in source; the serial media-download queue is untouched.

Three warnings — all in the D5 live poll's interaction with the rest of the system — would burn the owner device re-verify if they trigger: a privacy-clear path that permanently kills the poll (WR-01), a visibility check that keeps polling while the chat screen is hidden by a tab switch (WR-02), and the D2 removal tombstone being consumed on its first reconcile, which at the new 3-second poll cadence shrinks its designed 90-second grace window to a single cycle (WR-03). All three have small, local fixes.

Verified non-issues worth recording for the device pass:

- **ChatManager lifecycle:** ChatManager lives on a root GameObject (`m_Father: {fileID: 0}`, `m_IsActive: 1` in Main.unity), so `OnApplicationFocus`/`OnApplicationPause` and the poll's host object are lifecycle-safe; the only poll-stranding paths are explicit `StopAllCoroutines()` calls (see WR-01).
- **No inactive-StartCoroutine crash from hidden-screen polling:** `MessageListView` subscribes `HandleLiveMessages` in OnEnable and unsubscribes in OnDisable (MessageListView.cs:85-86, 107, 134), so live fires while the chat screen is hidden cannot reach `StartCoroutine`/`Instantiate` on an inactive hierarchy — WR-02's impact is wasted polling, not a crash.
- **No mid-merge kill:** `SyncLatestMessages` has no yield points after its park-wait, so the poll's defensive `StopCoroutine(_activeSync)` can never kill a sync between `seenMessageIds` mutation and the cache write; the worst case is one dropped-and-refetched response.
- **D6 coverage is complete:** `SwipeToDelete`'s lazy `Rt` property covers every pre-Awake entry point (`ResetClosed`, `Close`, `Open`, `OnEndDrag`, `SetContentX`), and `_scroll` is lazily resolved in both `OnInitializePotentialDrag` and `OnBeginDrag`; `OnDisable` resets `_openInstance`, tweens, and position.
- **D4 tap/scroll semantics survive disabling the component:** with `SwipeToDelete.enabled = false` on Telegram rows, ExecuteEvents skips disabled Behaviours, so drags resolve to the parent ScrollRect; `pointerPress != pointerDrag` then makes the EventSystem clear `eligibleForClick`, so a scroll ending on a row does not open a chat — the bypassed `ConsumeDragFlag` protection is not needed on that path. The pooled-row rebind explicitly resets closed state and hides the delete button before disabling.
- **D2 tombstone vs. toggle/highlight/revert:** `ReactionStore.ApplyToMessage` keys by `reactorKey`, so a failed-removal revert overwrites the tombstone in place; a re-tap after removal correctly resolves as an add (`CurrentMyEmoji` returns `""`, not the tapped emoji); `ReactionSummary`/`RenderReactions` keep a tombstone-bearing list invisible with no reserved clearance.
- **D7 keying is consistent end-to-end within ParseChatsJson:** serverIds, the isDeleted branch, the smart merge, the spawn constructor, notify policy, and the stale sweep all use the same canonical key; `OnChatRemoved` and `ChatHistoryCache.DeleteHistory` receive the same key the vm was constructed with.
- **Python tools:** `verify-telegram-parity.py` is fail-closed (`OSError`/`KeyError`/`IndexError`/`JSONDecodeError` all exit 1) with no injection surface; `build-suggest-replies.py`'s override precedence (flag > env > SQLite-by-name > pinned dev) is correctly ordered and inert on the `--export` path.

## Warnings

### WR-01: `ClearAllLocalHistory` kills the D5 live poll for the rest of the session

**File:** `Assets/Scripts/Main/ChatManager.PrivacyClear.cs:81` (interaction with `Assets/Scripts/Main/ChatManager.LivePoll.cs:38-46`)
**Issue:** Plan 08-04 re-kicks `_livePollRoutine` after each of the three known `StopAllCoroutines()` sites — `Start()` (ChatManager.cs:243-244), `SetActiveBot` (ChatManager.BotState.cs:138-139), and `SetActiveChannel` (ChatManager.Channel.cs:89-90) — but the fourth site, `ClearAllLocalHistory()` (Профиль → Конфиденциальность → clear local history), calls `StopAllCoroutines()` at line 81 and never restarts the poll. After the owner runs a privacy clear, the open-chat live poll is dead until the next bot switch, channel switch, or app restart. For the common single-bot, single-channel owner there is no such switch — D5 silently regresses for the rest of the session, which is exactly the defect this phase closes. (Adjacent pre-existing gap in the same method: it also skips `ClearResolveQueues()`, unlike SetActiveBot/SetActiveChannel, so the killed quote/reaction drain workers' bookkeeping is never reset — worth fixing in the same touch.)
**Fix:**
```csharp
// ChatManager.PrivacyClear.cs — ClearAllLocalHistory(), after StopAllCoroutines():
StopAllCoroutines();
_chatFetchesInFlight = 0;
_chatListSyncing = false;
ClearVideoThumbQueue();
ClearMediaDownloadQueue();
ClearResolveQueues();       // drain workers were just killed; reset their bookkeeping

// D5: StopAllCoroutines() above killed the open-chat live poll — re-kick it, matching
// SetActiveBot / SetActiveChannel.
if (_livePollRoutine != null) StopCoroutine(_livePollRoutine);
_livePollRoutine = StartCoroutine(OpenChatLivePollRoutine());
```

### WR-02: Live poll keeps issuing messages/get while the chat screen is hidden by a tab switch

**File:** `Assets/Scripts/Main/ChatManager.LivePoll.cs:58-59`
**Issue:** `chatIsOpen` gates on `MessageListPanel.activeSelf`. `BottomTabManager` hides screens by deactivating the whole screen panel (`tab.screenPanel.SetActive(isActive)`, BottomTabManager.cs:245); `MessageListPanel` is a child of `Screen_Whatsapp`, so its own `activeSelf` stays `true` while the screen is off. Flow: owner opens a chat, taps «Сводка»/«Боты»/«Профиль» → the poll keeps firing a messages/get every 3 seconds indefinitely (the app is still focused), violating the gate's own contract ("a chat is open and on-screen") and adding sustained Wappi/tapi request pressure and battery drain during normal multi-tab use. No crash results (`MessageListView` unsubscribes its live handler in OnDisable), but this is continuous background network churn in a very reachable state, and it also means `_activeChatCache`/history keep churning for a chat the owner is not looking at.
**Fix:**
```csharp
// ChatManager.LivePoll.cs — OpenChatLivePollRoutine():
bool chatIsOpen = !string.IsNullOrEmpty(currentChatId)
                  && MessageListPanel != null && MessageListPanel.activeInHierarchy;
```
`activeInHierarchy` is false while any ancestor (the tab screen) is deactivated, so the poll pauses on tab-away and resumes when the owner returns. (The pre-existing `activeSelf` use for `chatPanelVisible` in `ParseChatsJson` line 300 has the same quirk but only affects a notification-cue heuristic — out of this phase's scope.)

### WR-03: D2 removal tombstone is consumed by the first reconcile — the 90 s grace window is effectively one 3 s poll cycle

**File:** `Assets/Scripts/Chat/TelegramReactionMerge.cs:44-50` (interaction with `Assets/Scripts/Main/ChatManager.cs:1796-1800` and the 08-04 poll cadence)
**Issue:** `Merge` suppresses the server's stale "me" echo when a fresh tombstone exists, but never carries the tombstone into the merged result (pinned by test `Merge_LoneFreshRemoval_NoServer_IsNull`: "never lingers as its own entry"). `RefreshCachedMessageReactions` then assigns `cached.reactions = merged`, so the tombstone is gone after the FIRST reconcile. A fresh optimistic ADD, by contrast, survives every merge inside the grace window (`result.Add(mine)` / `result[serverMine] = mine`) — the removal path is asymmetric. Before this phase, reconciles ran only at chat open, so one suppression usually spanned the whole echo lifetime. With 08-04's 3-second live poll, reconciles now run every ~3 s: if tapi's stale echo outlives one poll interval — poll 1 consumes the tombstone; poll 2 sees no "me" in the cache and a still-echoed server "me" — the just-removed reaction resurrects: the exact D2 symptom, re-opened by D5. `OptimisticGraceSeconds = 90` documents 90 seconds of protection; the implementation delivers ~3.
**Fix:** Carry the tombstone forward inside the grace window so every reconcile within 90 s keeps suppressing:
```csharp
if (string.IsNullOrEmpty(mine.emoji))
{
    // Fresh optimistic REMOVAL tombstone: drop the server's stale "me" echo AND keep the
    // tombstone in the result, so the NEXT reconcile (3 s later at the D5 poll cadence)
    // still suppresses a late echo. ReactionSummary hides empty-emoji entries and the
    // RenderReactions clearance follows visible emoji, so a lingering tombstone never renders.
    if (serverMine >= 0) result.RemoveAt(serverMine);
    result.Add(mine);
}
```
Display safety is already in place this phase: `ReactionSummary.Build` skips empty-emoji entries and `MessageItemView.RenderReactions` (lines 4655-4661) bases clearance on visible emoji, so a tombstone-only list renders as "no reactions" with no reserved space. Once the grace expires, the next merge drops the stale tombstone naturally (server list wins; a tombstone is never in the server list). Update `Merge_LoneFreshRemoval_NoServer_IsNull` and `StampThenMerge_EndToEnd_RemovedReactionStaysRemoved` to expect a tombstone-only list instead of null, and add a two-successive-reconciles test asserting no resurrection while fresh. Alternative (smaller, if the single-cycle echo assumption is confirmed at the 08-10 capture): re-stamp the tombstone in `RefreshCachedMessageReactions` when a merge consumed one that is still fresh.

## Info

### IN-01: Toggle-off/highlight emoji equality does not normalize the variation selector — verify echoed form at 08-10 capture

**File:** `Assets/Scripts/Chat/OutgoingReaction.cs:27-28`, `Assets/Scripts/Chat/ReactionBarController.cs:259-265`
**Issue:** `TelegramReactionCatalog.IsAllowed` deliberately normalizes the trailing U+FE0F ("Telegram accepts the unqualified form"), but `OutgoingReaction.Resolve`'s toggle-off check (`current == tappedEmoji`) and `RefreshHighlight`'s `quick[i] == mine` compare raw strings. If tapi echoes the owner's "❤️" back in base form "❤" (post-grace, when the server-mapped entry becomes "me"), the quick-bar highlight misses and the first toggle-off tap resolves as a re-send instead of a removal (self-corrects on the second tap).
**Fix:** Route both comparisons through a shared VS16-stripping normalizer (promote `TelegramReactionCatalog.StripVariationSelector`). Confirm the echoed form in the 08-10 device capture first; if tapi echoes fully-qualified, no change is needed.

### IN-02: chatLookup keying asymmetry — only `GetChat` canonicalizes

**File:** `Assets/Scripts/Main/ChatManager.cs:267` vs `ChatManager.cs:541` (SelectChat), `ChatManager.DeleteChat.cs:31,43`, `ChatManager.Dashboard.cs:11,22,34`
**Issue:** D7 made `GetChat` resolve through `ChatIdFormat.CanonicalKey`, but `SelectChat`, `DeleteChat`, and the Dashboard `TryGet*` helpers still index `chatLookup` with the raw argument. All current callers pass `vm.ChatId` (already canonical) or bare server session ids, so nothing breaks today — but any future caller holding a suffix-twinned Telegram id will silently miss the row in those paths while `GetChat` finds it.
**Fix:** Either canonicalize at each chatLookup entry point (`ChatIdFormat.CanonicalKey(chatId, ActiveChannel)`), or add a one-line comment on the `chatLookup` declaration stating "keys are channel-canonical ids; external ids must pass through CanonicalKey" so the convention survives future edits.

### IN-03: `SyncAllChats` writes a full-payload `response.txt` dump on device (pre-existing)

**File:** `Assets/Scripts/Main/ChatManager.cs:466-471`
**Issue:** Adjacent to this phase's new sync events: the debug dump `File.WriteAllText(persistentDataPath + "/response.txt", text)` runs unconditionally on every chats/filter sync — unlike the equivalent dump in `SyncLatestMessages` (line 632), which is `#if UNITY_EDITOR`-gated. On device this is a synchronous main-thread disk write of the entire chat-list JSON per sync, plus a plaintext chat-metadata artifact on disk (mitigated: `ClearAllLocalHistory` deletes it). Pre-existing, not introduced this phase.
**Fix:** Wrap the dump in `#if UNITY_EDITOR` to match the SyncLatestMessages precedent.

### IN-04: build-suggest-replies.py — silent DEV-credential fallback on a no-SQLite prod target; unhandled URLError

**File:** `Tools/n8n/build-suggest-replies.py:93-116, 119-130`
**Issue:** (a) On a target with no local SQLite (n8n Cloud) and no `--openai-cred`/`--supabase-cred`/env override, `resolve_cred` silently falls back to the pinned DEV credential ids (line 116) — the deploy succeeds but the workflow fails at first execution with invalid credentials. The docstring documents the override path, but the script does not enforce it when the DB is simply absent. This is a sharper edge than the by-design verbatim-override binding the 2026-07-15 review recorded (that finding stands: an operator-supplied id cannot be validated against Cloud and is used verbatim, deliberately). (b) `req()` catches `HTTPError` only; a connection-refused/unreachable `BASE` raises an uncaught `urllib.error.URLError` traceback instead of a clean failure message.
**Fix:** (a) In `resolve_cred`, when `not os.path.exists(DEV_DB)` and no override is set, fail loudly if `BASE` is not a localhost URL (mirror the existing "refusing to guess" `SystemExit`); optionally log the resolved `(id, name)` per credential in `deploy()` so bound ids are visible in deploy output. (b) Add `except urllib.error.URLError as e: raise SystemExit(f"cannot reach {url}: {e.reason}")` in `req()`.

### IN-05: `ReactionPillView.HasReactions` counts removal tombstones

**File:** `Assets/Scripts/UI/ReactionPillView.cs:40`
**Issue:** `HasReactions => _last != null && _last.Count > 0` is raw-count based, so a lone D2 tombstone reports `true`. The consequence is benign today (it only gates the `OnEmojiReady` re-render, and `Render` itself correctly hides via `ReactionSummary.Build`), but it is now inconsistent with the "visible reactions" semantics this phase established in `ReactionSummary`/`RenderReactions` — and becomes load-bearing if WR-03's fix makes tombstone-only lists persist.
**Fix:** `public bool HasReactions => ReactionSummary.Build(_last).emojis.Count > 0;` (or document the raw-count intent).

### IN-06: `TelegramReactionMerge.Key` concatenates without a separator

**File:** `Assets/Scripts/Chat/TelegramReactionMerge.cs:132`
**Issue:** `Key` builds `$"{r?.reactorKey}{r?.emoji}"`, so `SameReactions` can theoretically collide across (reactorKey, emoji) boundaries (e.g. `"ab"+"c"` vs `"a"+"bc"`). Practically unreachable — reactor keys are numeric ids/"me" and emoji are non-ASCII glyphs — but a one-character fix removes the class.
**Fix:** Insert a separator that cannot appear in a reactor key, e.g. `$"{r?.reactorKey}\u0001{r?.emoji}"`.

---

_Reviewed: 2026-07-16T13:28:33Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
