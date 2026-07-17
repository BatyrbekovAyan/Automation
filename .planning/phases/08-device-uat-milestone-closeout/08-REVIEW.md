---
phase: 08-device-uat-milestone-closeout
reviewed: 2026-07-17T08:45:09Z
depth: standard
files_reviewed: 20
files_reviewed_list:
  - Assets/Scripts/Chat/ChatListSyncIndicatorGate.cs
  - Assets/Scripts/Chat/MediaDownloadFailure.cs
  - Assets/Scripts/Chat/OutgoingReaction.cs
  - Assets/Scripts/Chat/ReactionBarController.cs
  - Assets/Scripts/Chat/ReactionEmoji.cs
  - Assets/Scripts/Chat/ReactionSummary.cs
  - Assets/Scripts/Chat/TelegramReactionMapper.cs
  - Assets/Scripts/Chat/TelegramReactionMerge.cs
  - Assets/Scripts/Main/ChatManager.BotState.cs
  - Assets/Scripts/Main/ChatManager.Channel.cs
  - Assets/Scripts/Main/ChatManager.cs
  - Assets/Scripts/UI/ChatListSyncIndicator.cs
  - Assets/Scripts/UI/EmptyStateView.cs
  - Assets/Tests/Editor/Chat/ChatListSyncIndicatorGateTests.cs
  - Assets/Tests/Editor/Chat/MediaDownloadFailureTests.cs
  - Assets/Tests/Editor/Chat/ReactionEmojiTests.cs
  - Assets/Tests/Editor/Chat/ReactionSummaryTests.cs
  - Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs
  - Assets/Tests/Editor/Chat/TelegramReactionReceiveTests.cs
  - Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json
findings:
  critical: 0
  warning: 3
  info: 2
  total: 5
status: issues_found
---

# Phase 8: Code Review Report — Round 2 (gap-closure delta `44b732e^..HEAD`)

**Reviewed:** 2026-07-17T08:45:09Z
**Depth:** standard
**Files Reviewed:** 20
**Status:** issues_found

> NOTE: this review discusses invisible Unicode characters (VS16 U+FE0F, ZWJ U+200D,
> the U+0001 key separator) — all such characters are written below in escaped `U+XXXX` /
> `\uXXXX` ASCII notation, never as raw code points.

> Supersedes the 2026-07-16 round-1 review (29 files, plans 08-04..08-09), whose findings were
> dispositioned in `08-REVIEW-FIX.md` (commit 1b2e60b). This round covers ONLY the round-2
> gap-closure delta (plans 08-11..08-15). Finding numbers CONTINUE round 1's sequence
> (WR-04.., IN-07..) because code comments already cite round-1 ids ("08-REVIEW WR-03",
> "IN-06" in TelegramReactionMerge.cs). Round-1 findings resolved *inside this delta* and
> verified here: IN-01 (VS16 toggle/highlight equality — closed by the new `ReactionEmoji`
> seam threaded through OutgoingReaction/ReactionBarController, with the residual gap noted
> in WR-04 below) and IN-06 (`SameReactions` key separator — closed with a `"\u0001"`
> separator + compare key). Round-1 WR-03 (tombstone carry) was fixed before this range and
> its behavior is re-verified by the new `Merge_TwoSuccessivePolls_TombstoneKeepsSuppressing` test.

## Summary

Reviewed the round-2 gap-closure delta: D2 reaction identity (canonical-emoji seam, persisted Telegram owner-id, un-mapped-echo fold — 08-11), D9 sync-pill minimum-visible-duration (08-12), D10 Suggest_Replies newest-incoming anchor (08-13), D12 channel-aware create-bot CTA (08-14), and D11 media-download diagnostics + one serial-safe retry (08-15). `ChatManager.cs` was reviewed via its three diff hunks plus the surrounding seams they interact with (SetActiveBot/SetActiveChannel reset choreography, SyncAllChats events, the merge/mapper/summary pipeline, the media-download drain worker).

The delta is high quality. The pure seams (`ReactionEmoji`, `MediaDownloadFailure`, `ChatListSyncIndicatorGate`, the `Merge` fold) are null-tolerant, well-documented, and pinned by focused EditMode tests including the spoofing guard (T-08-11-01) and the information-disclosure cap (T-08-15-01). Verified specifically:

- **D11 retry seriality holds:** the transient retry runs inline on the drain worker's coroutine with the request disposed before the 1.5 s backoff — never two concurrent `/media/download` requests (the Wappi cross-serving constraint); the final attempt can never set `retryTransient`, so the loop provably exits via `yield break`; `timeout = 30` re-applied per attempt; queue bookkeeping reset covers all switch paths including `ClearAllLocalHistory`.
- **T-08-15-01 enforced:** every `FormatLog` call site passes `Snippet(...)` output — capped 256 chars, single-line, no file writes, no full payloads (does not grow the pre-existing round-1 IN-03 `response.txt` dump, which remains open and out of this delta).
- **D10 wired end-to-end:** the `Prep` node genuinely produces `queryText` (prefers `lastIncomingText`, else the newest client message via a backwards scan, 500-char cap); `Assemble` injects it as `fenced.lastClientMessage` *inside* the fenced untrusted block while the system prompt references it by field name only — the injection-fencing discipline is preserved.
- **D12 CTA correct:** `Manager.SelectPlatform(2)` is the Telegram mode (1=WA, 2=TG, 3=both); WhatsApp resolves to 1 byte-identically; the zero-bots case correctly inherits the surviving `ActiveChannel` so the CTA matches the themed empty state.
- **Owner-id key hygiene:** the learn key (`GetActiveProfileId()` under the TG-only Normalize branch) and both load keys (explicit `ProfileIdForChannel(bot, ChatChannel.Telegram)`) always resolve the same Telegram profile id; the unauthed `"-1"` sentinel is never written (learn side validates) and reads back null harmlessly; the `_tgOwnUserId != raw.from` guard limits `PlayerPrefs.Save()` to one write per profile.
- **Deferred-hide lifecycle:** `KillDeferredHide` runs from `OnDisable`, `Hide`, and `BeginSpin`; `DeferredHideRoutine` uses `WaitForSecondsRealtime` and re-checks `IsChatListSyncing` at wake; the H5 privacy-clear rescue works because `ClearAllLocalHistory` always reaches a follow-on sync whose `OnChatListSyncStart` re-owns the pill.

Three warnings: a residual hole in the new VS16 normalizer for the one mid-sequence-FE0F emoji in the Telegram catalog (WR-04), the persisted owner-id never loading on the cold-start path (WR-05), and the sync-pill channel-switch catch-up stranding a permanently-visible pill on the designed tap-unconnected-Telegram flow (WR-06). Two bounded Info items.

## Warnings

### WR-04: VS16 normalizer misses mid-sequence FE0F — heart-on-fire reintroduces every D2 symptom for that emoji

**File:** `Assets/Scripts/Chat/ReactionEmoji.cs:38-44` (CompareKey), `:70-76` (BuildRequalify); catalog evidence `Assets/Scripts/Chat/TelegramReactionCatalog.cs:27`
**Issue:** `ReactionEmoji.CompareKey` strips only a single *trailing* U+FE0F. The Telegram allowed set contains heart-on-fire = `U+2764 U+FE0F U+200D U+1F525` — verified to be the only catalog entry whose FE0F is mid-sequence (the shrug variants carry it trailing and are handled). Telegram's canonical reaction string for heart-on-fire is the FE0F-less `U+2764 U+200D U+1F525` — the same base-form convention tapi already exhibits for the plain heart (the observed root cause A). For this emoji:
- `CompareKey` leaves both forms unchanged (the last char is the flame's low surrogate, not FE0F), so `SameEmoji(baseForm, qualifiedForm)` is **false** — the `ReactionSummary` dedup, the `IndexOfUnmappedSameEmoji` fold, `OutgoingReaction.Resolve` toggle-off, and the quick-bar highlight all miss;
- the `Requalify` map key for heart-on-fire is the *unstripped* qualified form (built via `CompareKey`), so a base-form echo never matches — `Canonical` passes the base form through, and the pill renders a literal text glyph (the TMP sprite name requires the `-fe0f` segment — the exact constraint the class doc cites).
Net: reacting with heart-on-fire on Telegram reproduces D2 symptoms 1/2/3 (double count, stale pill on change, literal-text glyph) that this delta fixes for the plain heart.
**Fix:** Strip *all* FE0F occurrences in `CompareKey` — verified collision-free across the full 73-glyph catalog (all base forms stay unique after full-strip), and because `BuildRequalify` keys by `CompareKey`, this one change also fixes `Canonical` requalification:
```csharp
public static string CompareKey(string emoji)
{
    if (string.IsNullOrEmpty(emoji)) return emoji;
    if (emoji.IndexOf(VariationSelector16) < 0) return emoji;   // fast path, no alloc
    return emoji.Replace("\uFE0F", "");
}
```
Add a test pair pinning it: `SameEmoji("\u2764\u200D\uD83D\uDD25", "\u2764\uFE0F\u200D\uD83D\uDD25")` is true, and `Canonical` of the base form returns the qualified catalog form. (`TelegramReactionCatalog.StripVariationSelector` shares the trailing-only limitation on the `IsAllowed` side — outside this delta, worth aligning in the same touch. The 08-16 device capture can confirm tapi's echoed form for ZWJ reactions.)

### WR-05: Persisted Telegram owner-id is never loaded on cold start — the "survives app relaunches" claim holds only after a switch

**File:** `Assets/Scripts/Main/ChatManager.BotState.cs:322-334` (`InitializeActiveBotNextFrame` / `ResolveInitialActiveBot`); loads exist only at `ChatManager.BotState.cs:143` and `ChatManager.Channel.cs:91`
**Issue:** The D2 root-cause-B fix persists `_tgOwnUserId` per Telegram profile (`ChatManager.cs:1639-1648`) and loads it in `SetActiveBot` and `SetActiveChannel` — but the app-launch path (`Start()` → `InitializeActiveBotNextFrame` → `ResolveInitialActiveBot`) assigns `CurrentBotId` directly, never routes through `SetActiveBot` (whose `botId == CurrentBotId` guard would also early-return for the same bot later), and performs **no load**. After every relaunch `_tgOwnUserId` is null until an own (`fromMe`) row happens to be Normalized or the user switches bot/channel — so the exact D2-B repro (owner reacts in a chat with no own rows loaded) recurs in the first-session window after each relaunch, contradicting the learn-site comment ("survives bot/channel switches **and app relaunches**", `ChatManager.cs:1633-1636`). Impact is bounded — the `Merge` fold covers the count-«2» symptom within the 90 s grace and the id re-learns from the first own row — but the persistence half of the fix is inert on the most common entry path, and the un-mapped different-emoji change case (deliberately not folded, per `Merge_FreshOptimisticMe_DifferentEmojiEcho_NotFolded`) relies on the mapped-echo path that this gap disables.
**Fix:** Load the persisted id when the initial bot resolves, mirroring the two switch sites:
```csharp
// InitializeActiveBotNextFrame, after ActiveChannel = ResolveChannelForBot(CurrentBotId);
Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
string tgProfileId = bot != null ? ProfileIdForChannel(bot, ChatChannel.Telegram) : null;
_tgOwnUserId = LoadPersistedTgOwnUserId(tgProfileId);
```
Consider extracting the three shared lines into a `ReloadTgOwnUserIdFor(string botId)` helper so no future reset site can miss it.

### WR-06: Sync-pill H3 catch-up reads the doomed old-channel sync — stuck «Синхронизация…» pill on the tap-unconnected-Telegram path

**File:** `Assets/Scripts/UI/ChatListSyncIndicator.cs:110-114` (`HandleActiveChannelChanged`); interacting order `Assets/Scripts/Main/ChatManager.Channel.cs:76-80` and `Assets/Scripts/Main/ChatManager.BotState.cs:250-257`
**Issue:** `SetActiveChannel` fires `OnActiveChannelChanged` (Channel.cs:76) *before* it kills the in-flight sync and resets the flag (`StopAllCoroutines(); … _chatListSyncing = false;`, Channel.cs:78-80). So when the owner taps the Telegram chip while a WhatsApp `chats/filter` sync is in flight, the new H3 line sees `IsChatListSyncing == true` — but that flag describes the *old* channel's sync, killed two lines later **without ever firing `OnChatListSyncEnd`**. The pill shown by `BeginSpin()` is then owned by nobody unless a follow-on sync starts. `ChannelSwitcherView` deliberately keeps unconnected chips tappable ("tapping an unconnected channel is how the owner reaches its connect empty state", SWITCH-02) — and for an unconnected Telegram profile `BeginLoadForActiveBot` early-returns with `OnEmptyState` *before* any `SyncAllChats` (BotState.cs:253-257): no start, no end. Result: a permanently-spinning «Синхронизация…» pill over the «Подключите Telegram» empty state until the next bot/channel switch — the T-08-12-01 stuck-pill class this very plan mitigates elsewhere. Note the H3 line is also redundant in the connected case: `BeginLoadForActiveBot` → `StartCoroutine(SyncAllChats)` executes `_chatListSyncing = true; OnChatListSyncStart?.Invoke()` synchronously in the same `SetActiveChannel` call stack (the first yield is `SendWebRequest`), so `HandleSyncStart` → `BeginSpin` re-shows the pill moments after `HandleActiveChannelChanged` anyway.
**Fix:** Remove the catch-up (the same-stack `OnChatListSyncStart` covers every connected-TG switch, including the mid-flight case the comment targets), or gate it on the new channel actually being loadable:
```csharp
private void HandleActiveChannelChanged(ChatChannel channel)
{
    if (channel != ChatChannel.Telegram) { Hide(); return; }
    // No catch-up: the old channel's in-flight sync is about to be killed with no End
    // event, and the follow-on TG SyncAllChats fires OnChatListSyncStart in this same
    // call stack when a TG profile exists. Showing here strands the pill when it doesn't
    // (BeginLoadForActiveBot -> empty state, no sync) — the SWITCH-02 unconnected tap.
}
```
(The OnEnable catch-up at line 60 is unaffected and remains correct — there the in-flight sync genuinely belongs to the active Telegram channel.)

## Info

### IN-07: Removal tombstone cannot suppress an un-mapped own echo — one-poll transient resurrection in the unlearned-id window

**File:** `Assets/Scripts/Chat/TelegramReactionMerge.cs:48-59` (tombstone branch) vs `:66-77` (fold)
**Issue:** The tombstone branch removes only a server entry keyed `"me"`. If the owner reacted *and* toggled off while `_tgOwnUserId` was still unlearned, tapi's stale echo is keyed by the numeric `user_id`, so the fresh tombstone rides alongside it — the just-removed reaction re-renders (glyph, count 1) for roughly one D5 poll cycle until the server stops echoing, after which the carried tombstone leaves the list empty. The fold cannot help here because the tombstone's emoji is `""` (nothing to match). Bounded, self-healing (3–6 s), and made rare by the persisted-id fix — recorded so the residual is not mistaken for a D2 regression during the 08-16 device pass.
**Fix:** Accept as a documented residual, or carry the removed emoji on the tombstone (e.g. a non-serialized `removedEmoji` consumed only by `Merge`) so the tombstone branch can also fold a single un-mapped same-emoji echo.

### IN-08: Editor-only `CodePointHex` throws on lone surrogates

**File:** `Assets/Scripts/Main/ChatManager.cs:1702-1713`
**Issue:** `char.ConvertToUtf32(s, i)` throws `ArgumentException` on an unpaired surrogate. JSON can legally encode lone surrogates (`"\ud83d"` with no low-surrogate escape following) and Newtonsoft materializes them into the string, so a malformed tapi `reaction` value would throw *inside `Normalize`* — Editor-only (`#if UNITY_EDITOR`), but it would break message parsing for that batch precisely while diagnosing the malformed data the logger exists to capture.
**Fix:** Guard the pair before converting:
```csharp
int cp = char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])
    ? char.ConvertToUtf32(s, i)
    : s[i];                       // log the lone unit as-is
i += (cp > 0xFFFF) ? 2 : 1;
```

---

_Reviewed: 2026-07-17T08:45:09Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
