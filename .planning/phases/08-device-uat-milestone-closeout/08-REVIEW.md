---
phase: 08-device-uat-milestone-closeout
reviewed: 2026-07-21T08:37:02Z
depth: deep
files_reviewed: 6
files_reviewed_list:
  - Assets/Scripts/Chat/TelegramReactionMerge.cs
  - Assets/Scripts/Chat/OutgoingReaction.cs
  - Assets/Scripts/Chat/TelegramReactionMapper.cs
  - Assets/Scripts/Main/ChatManager.cs
  - Assets/Scripts/Main/ChatManager.QuoteResolve.cs
  - Assets/Scripts/Chat/ReactionBarController.cs
findings:
  critical: 2
  warning: 1
  info: 3
  total: 6
status: issues_found
---

# Phase 08: Code Review Report — Round-6 Diagnostic (D2-view residual / D15 probe)

**Reviewed:** 2026-07-21T08:37:02Z
**Depth:** deep
**Files Reviewed:** 6 (+ cross-file trace through `ChatManager.ReactionSend.cs`, `ReactionStore.cs`, `MessageReaction.cs`, `ReactionEmoji.cs`, `TelegramReactionMergeTests.cs`)
**Status:** issues_found

## Summary

Round-6 pinned the D2-view residual exactly: `[D2-merge] suppressed server-me '🔥' by fresh local '🥺' age=9s` climbing every poll — tapi `reactions[]` is **current-state-only**, so in a rapid own-change the confirming same-emoji echo never exists and the 08-30 "confirmation ends the grace" fix is structurally unreachable. This review traced the optimistic path end-to-end and reaches two Critical verdicts for round 7:

1. **CR-01 (the round-7 design decision): candidate (a) — displaced-emoji tracking — is the correct and only discriminating fix.** The displaced pre-tap value is *already captured* at the tap site (`priorEmoji`, ChatManager.ReactionSend.cs:40, snapshotted today for the failure revert); it threads into `Merge` by storing it ON the optimistic `MessageReaction` entry (survives the disk round-trip that a ChatManager-side map would lose). Candidates (b) and (c) are rejected with code evidence: (b) races the device-verified post-success echo lag (TelegramReactionMerge.cs:55-56) and any residual window re-admits the age=9s failure; (c) is refuted by the round-6 capture itself. Full spec + red/green test set below.
2. **CR-02 (latent, found by deep call-chain trace): the shipped 08-30 CR-01 fix is dead code in the app.** `RefreshCachedMessageReactions` (ChatManager.cs:1864) discards Merge's output whenever `SameReactions` — which keys on (reactorKey, emoji) only, never `time` — reports the lists equal. The same-emoji confirm adopt (TelegramReactionMerge.cs:83) and the unmapped-echo fold re-key (:93-102) produce exactly such key-identical lists, so `cached.reactions = merged` at :1866 never executes and the freshness is never consumed through ANY of the three call sites (ChatManager.cs:752-753, 1230-1231, 1330-1331). The Merge-level unit test passes only because it chains Merge outputs directly (TelegramReactionMergeTests.cs:260-267), bypassing the guard. Round 7 must ship CR-01 and CR-02 together.

Secondary: the deterministic `[D15-probe]` trigger is specced at IN-01 (insertion ChatManager.cs:661, reporting seam ChatManager.QuoteResolve.cs:148-149, no new secrets handling). Suite baseline 1184/1184; every finding carries an EditMode note.

## Critical Issues

### CR-01: Round-7 fix — discriminate stale echo from genuine external own-change via the DISPLACED pre-tap emoji (candidate (a))

**File:** `Assets/Scripts/Chat/TelegramReactionMerge.cs:76-83` (differ-suppress), `:50-67` (tombstone), `:119-140` (stamp) · `Assets/Scripts/Main/ChatManager.ReactionSend.cs:39-57` (tap site) · `Assets/Scripts/Chat/MessageReaction.cs:11-15` (model)

**Optimistic path, traced end-to-end:**

1. **Tap:** `ReactionBarController.OnEmojiTapped` (ReactionBarController.cs:230-234) and `EmojiPickerController.cs:201` → `ChatManager.SendReaction` (ChatManager.ReactionSend.cs:16).
2. **Displaced value IS available at the tap:** ReactionSend.cs:40 — `string priorEmoji = OutgoingReaction.CurrentMyEmoji(target);` (null ⇒ owner had no reaction). It is captured today solely for the failure revert; it is byte-for-byte the displaced pre-tap state candidate (a) needs.
3. **Fresh local "me" entry:** ReactionSend.cs:41-45 — `OutgoingReaction.Resolve(target, tappedEmoji, now)` mints the event (`time = now`, unix seconds); `ReactionStore.ApplyToMessage` writes it into `target.reactions` (change path re-stamps `existing.time = ev.time`, ReactionStore.cs:76-77; add path ReactionStore.cs:83-90). Removals stamp the tombstone at ReactionSend.cs:52-57. `MessageReaction.time` is the freshness field `IsFreshOptimistic` reads (TelegramReactionMerge.cs:161-162). The entry is persisted to disk immediately (`PersistReaction`, ReactionSend.cs:62/163-177) — so freshness **survives a chat close/reopen and app relaunch** within the window.
4. **POST success callback:** `PostReactionRoutine` — `ok = true` at ReactionSend.cs:110 (`status:"done"`), success exits at :129. Today success mutates NO local reaction state.

**Candidate evaluation:**

- **(a) Displaced-emoji tracking — RECOMMENDED.** Suppress a differing server-me ONLY when it equals the displaced pre-tap state (the genuine stale echo); any THIRD value is a genuinely newer external own-change → adopt (server element carries `time=0` ⇒ freshness consumed for free). Threading: store `displacedEmoji` on the optimistic `MessageReaction` entry itself — NOT a Merge parameter (the three `RefreshCachedMessageReactions` call sites have no tap context) and NOT a ChatManager dictionary (freshness persists to disk via `PersistReaction` and reloads from `ChatHistoryCache`; a memory-side displaced map would vanish on relaunch while `time` survives, resurrecting the bug). `MessageReaction` is `[Serializable]` public-fields JsonUtility (MessageReaction.cs:8-16) — a new `public string displacedEmoji` round-trips, and pre-upgrade cache entries deserialize it as null = "absence", which degrades to adopt-on-differ (at worst one visible flip inside a live 90 s window at upgrade; acceptable). Tombstone interplay: the tombstone's displaced = the just-removed emoji; a differing (third-value) "me" echo after a removal is a genuine external re-add → adopt + drop tombstone. Displaced-is-absence case: `SameEmoji(serverEmoji, null)` is false (ReactionEmoji.cs:59-60, ordinal; server emoji is never null/empty — the mapper skips empties, TelegramReactionMapper.cs:38) ⇒ any differing echo adopts, which is correct: with no pre-tap own reaction there is no non-empty stale echo to defend against (the stale state "no me" lands in the no-server-me branch, :85-105, unchanged). Fully pure ⇒ EditMode-testable. This fixes the exact round-6 capture: 🥺 tapped (displaced = ∅ or a prior emoji), server-me 🔥 at age=9s is a third value → adopt immediately; the follow-up 👎 applies at once (adopted entry is `time=0`, never fresh).
- **(b) Clear grace on POST HTTP success — REJECTED as primary.** The callback site exists (ReactionSend.cs:110/129), but `status:"done"` ≠ `reactions[]` propagated. The device-verified Merge comment (TelegramReactionMerge.cs:55-56) records the server "keeps echoing for a cycle or more after a successful removal" — a ~3 s poll inside the POST-success→propagation gap still returns the pre-tap state, and a cleared grace would ADOPT it → precisely the D2 flicker the grace was built to prevent. A ~10 s residual covers the *observed* 1-2 cycle lag but has no evidenced upper bound (round-2's 90 s was chosen against multi-poll staleness), and inside any residual the discriminator problem is unsolved — the round-6 failure occurred at age=9s, INSIDE a 10 s residual. Also more plumbing than (a): the success callback must relocate the "me" entry in a possibly-replaced list and re-persist, and it is coroutine-timing-dependent, not purely testable.
- **(c) Shorten the 90 s — REJECTED.** The 90 s protects the round-2 stale-echo defense (multi-poll stale echo of the pre-tap state). The round-6 capture kills (c) directly: the first wrong suppression was age=9s — any grace long enough to beat a 2-3-cycle stale echo (≥ ~10 s) still eats a rapid external change. Pure timing trade, no discriminator.

**Recommendation: (a), grace constant unchanged at 90, shipped together with CR-02.** Known residual to document in the plan (not fixable with current-state-only `reactions[]`): an external own-change BACK TO the displaced emoji within the window is indistinguishable from the stale echo and stays suppressed until a confirming echo of the optimistic emoji or window expiry — narrow, bounded, accepted v1.

**Fix spec (round-7 action text):**

1. `MessageReaction.cs` — add after :15:
```csharp
public string displacedEmoji;  // Telegram-only: the owner's own PRE-TAP state this optimistic entry
                               // displaced (null = had no reaction). Read by TelegramReactionMerge to
                               // tell a stale pre-tap echo (suppress) from a genuinely newer external
                               // own-change (adopt). Never set on server-mapped entries or WhatsApp.
```
2. `TelegramReactionMerge.cs` — differ branch (:76-82) becomes:
```csharp
if (!ReactionEmoji.SameEmoji(result[serverMine].emoji, mine.emoji))
{
    if (ReactionEmoji.SameEmoji(result[serverMine].emoji, mine.displacedEmoji))
        result[serverMine] = mine;   // server still echoes the displaced pre-tap state:
                                     // the stale echo the grace exists to defeat (round-2 D2 defense)
    // else: a THIRD value — neither optimistic nor displaced — is a genuinely newer
    // external own-change: keep the server element (time=0 ⇒ freshness consumed).
}
```
   Tombstone branch (:52-61): gate identically — suppress-and-carry ONLY when `SameEmoji(result[serverMine].emoji, mine.displacedEmoji)`; a different-emoji "me" after a removal keeps the server element and DROPS the tombstone. `StampRemovalTombstone` gains a `string displacedEmoji` parameter, set in both the reuse (:126-129) and add (:132-139) branches. New helper:
```csharp
public static void StampDisplaced(List<MessageReaction> reactions, string displacedEmoji)
{
    if (reactions == null) return;
    int idx = IndexOfMine(reactions);
    if (idx >= 0) reactions[idx].displacedEmoji = displacedEmoji;
}
```
   Update the class doc (:27-41) — the suppress condition is now "differing AND displaced-matching".
3. `ChatManager.ReactionSend.cs` — removal call (:55): `StampRemovalTombstone(target.reactions, now, priorEmoji);`. Set path — add after :57 (inside `SendReaction`, after the apply):
```csharp
if (ActiveChannel == ChatChannel.Telegram && !ev.IsRemoval)
    TelegramReactionMerge.StampDisplaced(target.reactions, priorEmoji);
```
   Revert path (:131-155): **no change.** A revert-created entry has `displacedEmoji = null` (adopt-on-differ ⇒ a ghost-landed reaction self-corrects, see WR-01); a revert-replaced entry keeps the tap's displaced (= priorEmoji = its own restored emoji), where every echo outcome is also correct (same-emoji ⇒ confirm; sent-emoji/third ⇒ adopt).
4. **EditMode tests** (`Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs`) — RED today: `Merge_DifferingEcho_NoDisplacedMatch_AdoptsExternalOwnChange` (the round-6 repro: cached `Me("🥺", Now)` displaced=null, server `Me("🔥", 0)`, now+9 → single 🔥, `time==0`); same with displaced="👍" set (third value still adopts); `Merge_FreshRemoval_DifferentEmojiEcho_ExternalReAddAdopts_TombstoneDropped` (tombstone displaced="👍", server `Me("🔥",0)` → `[🔥]`). MUST STAY GREEN (updated to stamp displaced): `Merge_DifferingEchoWithinGrace_StaleOldEmojiStillSuppressed` (:276 — its 👍→❤️ scenario maps to displaced="👍"), `Merge_FreshRemoval_SuppressesServerEcho_NoResurrection` (:33) and `Merge_TwoSuccessivePolls_TombstoneKeepsSuppressing_NoResurrection` (:118) with displaced=removed emoji. Unchanged green: `Merge_SameEmojiEcho_ConsumesGrace_ThenExternalOwnChangeApplies` (:254), `Merge_LoneFreshRemoval_NoServerEcho_DropsTombstone_AbsenceConfirmed` (:107), `Merge_FreshRemoval_AbsenceConfirmed_ThenExternalReAdd_Applies` (:151), both fold tests (:221, :237), the spoof guard (:307). New VS16 seam test: displaced stored base "❤" still suppresses a qualified "❤️" echo. Extend both `StampRemovalTombstone_*` tests (:168, :181) to assert `displacedEmoji` is set.

### CR-02: `RefreshCachedMessageReactions` discards Merge's freshness-consuming adoptions — the shipped 08-30 confirmation fix never lands through any call site

**File:** `Assets/Scripts/Main/ChatManager.cs:1862-1866` · call sites `:752-753`, `:1230-1231`, `:1330-1331` · `Assets/Scripts/Chat/TelegramReactionMerge.cs:83, 93-102, 144-157, 201`

**Issue:** `SameReactions` compares the (reactorKey, emoji-CompareKey) multiset only — `Key()` at TelegramReactionMerge.cs:201 excludes `time`. The two grace-consuming outcomes of `Merge` change ONLY `time`/identity metadata: the same-emoji confirm adopts the server element (`time=0`, :83) and the unmapped-echo fold re-keys the server element to "me" (:100-101). Both produce a merged list key-identical to the cached one ⇒ `SameReactions(cached.reactions, merged)` is true ⇒ ChatManager.cs:1864 `return false` **before** the `cached.reactions = merged` assignment at :1866. The cached fresh entry (original tap time) survives, the grace is never consumed, and the next differing echo is still suppressed for the full 90 s — so even in the slow-change case where the confirming echo DOES arrive, the shipped CR-01 behaves exactly like the pre-08-30 code. The round-6 device evidence ("confirming echo never arrived") masked this; the Merge unit test passes only because it chains `Merge` outputs directly (TelegramReactionMergeTests.cs:260-267), bypassing the call-site guard. All three reconcile sites funnel through this one method. Note: CR-01's third-value adoption changes the multiset and thus escapes the guard, but the confirm/fold consumption it relies on to narrow the back-to-displaced residual does not — CR-01 and CR-02 must land together.

**Fix:** always adopt the merged list; keep the guard only for render/dirty gating. Make it pure for EditMode by extracting a seam:
```csharp
// TelegramReactionMerge.cs
public static List<MessageReaction> Reconcile(List<MessageReaction> cached, List<MessageReaction> server,
                                              long nowUnix, out bool renderChanged)
{
    List<MessageReaction> merged = Merge(cached, server, nowUnix);
    renderChanged = !SameReactions(cached, merged);
    return merged;
}

// ChatManager.cs:1862-1868
var merged = TelegramReactionMerge.Reconcile(cached.reactions, refreshed.reactions,
                                             DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                             out bool renderChanged);
cached.reactions = merged;         // ALWAYS adopt — confirm/fold consume freshness even when
                                   // the (reactorKey, emoji) multiset is unchanged (CR-02).
if (!renderChanged) return false;  // no re-render, no dirty-mark — the anti-churn guard the
                                   // round-4 WR fixes rely on stays intact.
OnMessageReactionsChanged?.Invoke(cached);
return true;
```
Caveat (accept + document): a visually-unchanged adoption is memory-only (`return false` ⇒ not persisted), so a relaunch inside the window reloads the fresh entry from disk — harmless once CR-01's displaced gating exists, which is another reason the two ship together.

**EditMode:** `Reconcile_SameEmojiConfirm_AdoptsEvenWhenRenderUnchanged_ThenExternalChangeApplies` — step 1: cached `[Me("👍", Now)]`, server `[Me("👍", 0)]` ⇒ `renderChanged == false` BUT the returned list's me entry has `time == 0`; step 2: feed the returned list back with server `[Me("😁", 0)]` at Now+8 ⇒ `renderChanged == true`, emoji 😁. Red today at step 1's time assertion when driven through the guard semantics (the app-equivalent chain), green after.

## Warnings

### WR-01: Failed-POST revert re-arms a fresh optimistic "me" with the original tap time — resolved as a side effect of CR-01, pin it with a test

**File:** `Assets/Scripts/Main/ChatManager.ReactionSend.cs:132-145`
**Issue:** The revert event carries `time = appliedTime` (the ORIGINAL tap time, :139), so a failed POST — including a 30 s timeout where the reaction actually landed server-side (Wappi accepted, response lost) — recreates a *fresh* "me" entry that today suppresses every differing server echo for the remainder of the 90 s window: the ghost-landed sent emoji, or an external own-change made mid-flight, is discarded exactly like the D2-view case. Contained (failure path only), but the same suppression family.
**Fix:** none beyond CR-01 — a revert-created entry has `displacedEmoji = null` ⇒ any differing echo (ghost-landed sent emoji, external change) adopts immediately; the revert-replaced sub-path keeps displaced = its own restored emoji, where a matching echo hits the same-emoji confirm and a sent-emoji echo is a third value ⇒ adopt. Pin with `Merge_RevertShapedFreshMe_NullDisplaced_DifferingEchoAdopts`: cached `[Me("👍", Now)]` with `displacedEmoji=null`, server `[Me("🔥", 0)]`, now+5 ⇒ single 🔥. If round 7 wants belt-and-braces, the revert could stamp `time = 0` (server is the only truth after a failure) — optional, not required once CR-01 lands.

## Info

### IN-01: Deterministic `[D15-probe]` trigger — Editor-only one-shot auto-probe on the first WhatsApp reaction raw

**File:** insertion `Assets/Scripts/Main/ChatManager.cs:661-662` (the existing `[D15]` log block, before the `seenMessageIds.Add` branch at :672 so both seen/unseen raws arm it) · reporting seam `Assets/Scripts/Main/ChatManager.QuoteResolve.cs:142-150`
**Issue:** The current `[D15-probe]` fires only opportunistically — when a WA *quote resolve* happens to drain (QuoteResolve.cs:148-149) — so a UAT pass can end without ever probing a reaction TARGET payload. Deterministic spec: on the first WA `type:"reaction"` raw of the Editor session, enqueue its TARGET id (`raw.stanzaId` — established as the target stanza id by the chat-list reaction-preview resolver) into the existing serial quote-resolve drain:
```csharp
#if UNITY_EDITOR
    if (ActiveChannel == ChatChannel.WhatsApp && raw.type == "reaction"
        && !_d15ProbeArmed && !string.IsNullOrEmpty(raw.stanzaId))
    {
        _d15ProbeArmed = true;   // one-shot per Editor session
        Debug.Log($"[D15-probe] arming target-payload probe for stanza={raw.stanzaId}");
        if (!_quoteResolveInFlight.Contains(raw.stanzaId))
        {
            _quoteResolveInFlight.Add(raw.stanzaId);
            _quoteResolveQueue.Enqueue(raw.stanzaId);
            if (!_quoteResolveDraining) StartCoroutine(DrainQuoteResolveQueue());
        }
    }
#endif
```
with `private static bool _d15ProbeArmed;` (also `#if UNITY_EDITOR` to avoid an unused-field warning in builds). The drain then runs the EXISTING authed `messages/id/get` (Authorization header already set from `Manager.wappiAuthToken` at QuoteResolve.cs:129 — **no new request type, no new secrets handling**), serialized behind `WaitForChatFetchesToDrain` (:125, crossing-safe), and the existing `[D15-probe]` log at :148-149 reports `reactionsKey`/`reactionKey` presence booleans (never content) for the reaction's TARGET message — the exact D15 question. Deliberately bypassing `ResolveQuotedMessage`'s cache short-circuit guarantees the network fetch fires even for a previously-cached id; side effects are benign (the target's preview lands in `QuotedMessageCache` — a legitimate entry; no `_quoteWaiters` registered ⇒ `ApplyResolvedQuote` no-ops). Not unit-testable (network); one Editor session with a WA reaction raw closes it.

### IN-02: `[D15]` device log at the poll cadence — schedule removal with D15 closure

**File:** `Assets/Scripts/Main/ChatManager.cs:661-662`
**Issue:** Deliberately NOT `#if UNITY_EDITOR` (round-5 needed device capture) and content-safe (ids + booleans only), but it logs on every WA reaction raw at the ~3 s cadence in production builds. Carry-over of round-5 IN-03's second half (the `response.txt` dump at :622-628 also still stands as flagged there, preserved at d785061).
**Fix:** Once the owner re-verifies IN-01's probe result and D15 is documented as the platform limit (already in CLAUDE.md), delete this log and the QuoteResolve/one-shot probe blocks in the same round-7 cleanup task. No test needed.

### IN-03: `TelegramReactionMapper` fallback-key comment overstates what `{emoji}@tg` guarantees

**File:** `Assets/Scripts/Chat/TelegramReactionMapper.cs:54-57`
**Issue:** The comment says the emoji-scoped fallback key means "two same-emoji reactors with no id still don't collapse into one" — but two no-id reactors with the SAME emoji share the key `"{emoji}@tg"`. They don't collapse in `Map` itself (both are appended), which is why no behavior bug results today, but downstream identity consumers (`SameReactions` multiset, `IndexOfUnmappedSameEmoji`) treat them as indistinguishable — the key only separates *different-emoji* no-id reactors.
**Fix:** Reword the comment ("distinguishes no-id reactors of different emojis; same-emoji no-id reactors are indistinguishable by design — tapi has shown no such shape"). Doc-only; no test.

---

**Checked clean (round-6 scope):** all three `RefreshCachedMessageReactions` call sites correctly gate on `ActiveChannel == ChatChannel.Telegram` (ChatManager.cs:752, 1230, 1330 — WhatsApp stays byte-identical); the `[D2-merge]` diagnostic (TelegramReactionMerge.cs:78-80) is `#if UNITY_EDITOR` with a fully-qualified `UnityEngine.Debug` so the class stays UnityEngine-free for EditMode; tombstone WR-01/WR-03 branches remain mutually consistent (`FindMine`/`IndexOfMine` single-me invariant holds — the mapper emits at most one own element); `ReactionBarController.RefreshSourceNextFrame` (:157-164) is safe — coroutine started on the always-active singleton root, `_sourceView` captured-then-nulled, one-frame delay lands after the deferred Canvas destroy; `ClearResolveQueues` (QuoteResolve.cs:81-91) covers the StopAllCoroutines stall; `SameEmoji` null semantics confirmed exactly right for the CR-01 displaced condition.

_Reviewed: 2026-07-21T08:37:02Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
