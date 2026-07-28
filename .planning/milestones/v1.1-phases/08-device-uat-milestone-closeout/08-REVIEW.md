---
phase: 08-device-uat-milestone-closeout
reviewed: 2026-07-21T10:45:14Z
depth: standard
files_reviewed: 7
files_reviewed_list:
  - Assets/Scripts/Chat/MessageReaction.cs
  - Assets/Scripts/Chat/TelegramReactionMerge.cs
  - Assets/Scripts/Main/ChatManager.ReactionSend.cs
  - Assets/Scripts/Main/ChatManager.cs
  - Assets/Scripts/Main/ChatManager.QuoteResolve.cs
  - Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs
  - Assets/Tests/Editor/Chat/TelegramReactionReceiveTests.cs
findings:
  critical: 0
  warning: 2
  info: 6
  total: 8
status: issues_found
---

# Phase 08: Code Review Report (Round 7 — final quality pass)

**Reviewed:** 2026-07-21T10:45:14Z
**Depth:** standard
**Files Reviewed:** 7 (+ cross-checks in `ReactionStore.cs`, `ReactionEmoji.cs`, `ReactionPillView.cs`, `MessageItemView.cs`, `EmptyStateView.cs`, `WappiUnitySync.cs`, `Manager.cs`, `LocalDataWipe.cs`, `ChatManager.PrivacyClear.cs` for the cleanup inventory)
**Status:** issues_found (cleanup debt only — no correctness defects in the shipped round-7 logic)

## Summary

Round-7 scope (`5ac43d5..974b66b`, plan 08-34): `displacedEmoji` field on `MessageReaction`, displaced-gated differ suppression in `TelegramReactionMerge.Merge` (CR-01a), the pure `Reconcile` seam with always-adopt at `RefreshCachedMessageReactions` (CR-02), the WR-01 null-displaced pin, and the deterministic Editor-only `[D15-probe]`. Owner verified ALL-PASS on device/Editor; Gate A passed; suite 1191/1191.

**The shipped logic is sound.** Specific confirmations requested for this pass:

- **Removed `SameReactions` call-site guard — no other caller depended on it.** Full-source grep: `SameReactions` is now referenced only by `TelegramReactionMerge.Reconcile` (internal, `TelegramReactionMerge.cs:123`) and the two EditMode suites. `RefreshCachedMessageReactions` (`ChatManager.cs:1875-1894`) is the sole production consumer of the merge, and all three reconcile call sites (`ChatManager.cs:775`, `:1253`, `:1353`) route through it with the identical `Telegram`-gated pattern. Always-adopt + `renderChanged`-gated repaint preserves the anti-churn guard while consuming freshness — CR-02 is correctly closed.
- **`displacedEmoji` on old cached entries is safe.** `MessageReaction` is `[Serializable]` with a public string; JsonUtility leaves the missing key null on legacy `ChatHistoryCache` entries, and `Merge` reads null-displaced as adopt-on-differ — the strictly-safer default. Pinned by `MessageReaction_JsonUtility_MissingDisplacedEmoji_DefaultsNull`. Server-mapped entries (`TelegramReactionMapper`) and the WhatsApp path never set it, matching the field's contract comment. `ReactionEmoji.SameEmoji(x, null)` is false for any non-empty server emoji (mapper skips empties), so a legacy fresh tombstone with null displaced degrades to adopt — a one-flip-at-upgrade worst case, exactly as specced in round 6.
- **Failed-POST revert interplay is clean.** `PostReactionRoutine`'s revert goes through `ReactionStore.ApplyToMessage`, which reuses the "me" slot (`ReactionStore.cs:73-81`) without touching `displacedEmoji`. Traced all four cases (failed add / change / removal on Telegram; WhatsApp untouched): the leftover displaced value can only ever equal the reverted emoji or be null — both converge to same-emoji-confirm or third-value-adopt on the next poll. `Merge_RevertShapedFreshMe_NullDisplaced` pins the null case explicitly.
- **Probe lifecycle is race-free.** The `[D15-probe]` arming block (`ChatManager.cs:667-684`) reuses the serial quote-resolve drain correctly: `_quoteResolveInFlight` prevents double-enqueue; cooperative coroutine scheduling means the drain's tail-clear runs synchronously after its `while` exits, so a just-enqueued probe id cannot be stranded mid-interleave; no waiter is registered so `ApplyResolvedQuote` no-ops on the probe id; the incidental `QuotedMessageCache.Put` of the reaction target is harmless (correct data under the right key, and warms a real future quote of that message). Bot/channel-switch cleanup is covered by the existing `ClearResolveQueues` fix.

Remaining findings are **phase-close cleanup debt**: one un-gated production file dump (WR-01), the deliberately-compiled UAT diagnostics now due for stripping (WR-02), and six informational items. The definitive strip/keep inventory is in its own section below, followed by the D15 v1.2 riders.

## Warnings

### WR-01: Un-gated `response.txt` full chat-list dump ships in device builds

**File:** `Assets/Scripts/Main/ChatManager.cs:456-461` (inside `SyncAllChats`)
**Issue:** Unlike its two siblings (`:625-632` and `:1292-1299`, both `#if UNITY_EDITOR`), this dump has **no Editor guard**. Every background chat-list sync on device synchronously writes the entire `chats/filter` response — all chat names, ids, previews — to `persistentDataPath/response.txt` in plaintext, plus a `Debug.Log("Saved to: ...")`. That is main-thread blocking file I/O on a hot path and unencrypted PII at rest beyond what any feature needs (`LocalDataWipe.cs:24` and `ChatManager.PrivacyClear.cs:102-105` already treat this file as a liability to delete). Pre-existing (IN-03 lineage from earlier rounds), but v1.1 is now heading to prod — this is the one dump that actually executes on user devices.
**Fix:**
```csharp
// SyncAllChats — delete lines 456-461 entirely, or at minimum:
#if UNITY_EDITOR
            System.IO.File.WriteAllText(
                Application.persistentDataPath + "/response.txt",
                www.downloadHandler.text);
#endif
```
Keep the deleters in `LocalDataWipe`/`PrivacyClear` regardless — old installs still carry the file.
**EditMode-testable:** No (coroutine + file I/O); verify by grep after cleanup.

### WR-02: Compiled UAT diagnostics due for removal now Gate A has passed

**Files:** `Assets/Scripts/Main/ChatManager.cs:658-665`, `Assets/Scripts/UI/MessageItemView.cs:4659-4661` and `:4681-4687`, `Assets/Scripts/UI/ReactionPillView.cs:66-69`
**Issue:** Three log sites were *deliberately* compiled (not Editor-gated) so the device UAT pass could confirm behavior on-device — that purpose is now fulfilled:
1. **`[D15]` wa-reaction shape log** (`ChatManager.cs:664-665`) — fires for **every** WhatsApp `type:"reaction"` raw on **every ~3 s live poll**, including the already-seen rows that round 5 proved re-deliver indefinitely (`seen=true` each poll). On device this is continuous log spam with per-call stack-capture cost for any chat holding a reaction in the latest window. The D15 conclusion is permanently recorded in CLAUDE.md; the evidence collector has no remaining purpose.
2. **`[D2-view]` reactions-changed log** (`MessageItemView.cs:4661`) and **post-render state log** (`MessageItemView.cs:4687`) — fire on every reaction change plus one frame later. The content discipline held (ids/booleans/lengths only, per T-08-22-01), but they must not ship in v1.1 prod.
3. **`ReactionPillView.DiagnosticActive/DiagnosticLabelLength/DiagnosticLabelCulled`** (`ReactionPillView.cs:66-69`) — public surface existing solely for log #2; dead code the moment it is stripped.

**Fix:** Delete the three `Debug.Log` lines and the three `Diagnostic*` properties. Keep the *behavioral* hardening they were attached to: `HandleReactionsChanged`'s render chain, `RefreshReactionsNextFrame`'s `RefreshReactionsVisual()` call (the WR-01 pin), and `ForceReRender` all stay.
**EditMode-testable:** No (log-only); verify by grep (`\[D15\]|\[D2-view\]|Diagnostic`) after cleanup.

## Info

### IN-01: Inherent, bounded ambiguity — external own-change back to the displaced emoji is suppressed for the grace window

**File:** `Assets/Scripts/Chat/TelegramReactionMerge.cs:53-66, 71-82`
**Issue:** The displaced discrimination cannot distinguish a *stale echo of the pre-tap emoji* from a *genuine external own-change back to that same emoji* within the 90 s grace: (a) owner had 👍, taps ❤️ in-app, then re-selects 👍 in the Telegram app — the 👍 echo matches `displacedEmoji` and stays suppressed until grace lapses or a confirming ❤️ echo lands; (b) tombstone twin: owner removes 👍 in-app, then re-adds 👍 in the Telegram app within the window — same suppression. This is the exact residual round-6 CR-01 documented as accepted-v1 ("not fixable with current-state-only `reactions[]`"), strictly narrower than the pre-round-7 behavior (which suppressed *all* differing echoes), and self-heals in ≤90 s. Recorded here so the v1.2 follow-up doesn't rediscover it as a bug.
**Fix:** None for v1.1. If v1.2 wants it: only an event transport (reaction rows with timestamps) can break the tie. The seam is pure; the case is trivially EditMode-pinnable — `Merge(cached=[Me("❤️", Now, "👍")], server=[Me("👍", 0)], Now+n)` asserting the current (suppressing) behavior would document it as a characterization test.

### IN-02: Freshness consumption is not persisted when `renderChanged` is false — verified safe

**File:** `Assets/Scripts/Main/ChatManager.cs:1884-1891`
**Issue:** On a same-emoji confirm or un-mapped-echo fold, `cached.reactions = merged` adopts in memory, but the call sites see `false` and skip the cache dirty-mark, so the adopted `time=0` is not written to `ChatHistoryCache` until something else dirties it. After an app kill + relaunch inside the grace window, the disk copy resurrects the fresh optimistic time. Traced the consequence: with the round-7 displaced discrimination, a resurrected-fresh entry still adopts any third-value external change immediately — the only divergence is the IN-01 ambiguity case, already bounded at 90 s from the original tap. The deliberate anti-churn tradeoff (avoiding a disk write per ~3 s poll per reacted message) is sound; documenting so nobody "fixes" it into per-poll saves.
**Fix:** None required. Optionally extend the comment at `:1887-1889` with one line noting the persistence half of the tradeoff.

### IN-03: `[D15-probe]` one-shot semantics — comment inaccuracy and a silent-consume edge

**File:** `Assets/Scripts/Main/ChatManager.cs:46-48, 667-684`; `Assets/Scripts/Main/ChatManager.QuoteResolve.cs:142-150`
**Issue:** Two nits on the otherwise-clean probe: (1) `_d15ProbeArmed` is a static field, which resets on every domain reload — with default Editor settings that is one-shot **per Play-mode entry**, not "per Editor session" as commented (arguably better: each play re-probes; with domain-reload disabled it does behave per-session). (2) The probe consumes its shot at arming time, but the report only prints inside the `status=="done"` branch of `DrainQuoteResolveQueue` — a not-found target or network failure silently spends the shot with no `[D15-probe]` output for that play. Both acceptable for an Editor-only diagnostic.
**Fix:** Correct the comment to "one-shot per domain load"; optionally log a `[D15-probe] target fetch not definitive` line in the failure branch. Recommend **keeping** the probe gated (see inventory) — it is the documented v1.2 detection seam.
**EditMode-testable:** No (coroutine/network); comment-only change.

### IN-04: Stale TDD-phase comments in the merge suite

**File:** `Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs:271, 387`
**Issue:** `// FAILS today` (line 271, `Merge_SameEmojiEcho_ConsumesGrace_ThenExternalOwnChangeApplies`) and `// <-- RED here` (line 387, `Reconcile_SameEmojiConfirm_AdoptsEvenWhenRenderUnchanged_ThenExternalChangeApplies`) date from the RED commit (`1de1252`); both assertions have been GREEN since `6aff076`/`d2576e7`. A future reader will waste time wondering whether the suite has known failures.
**Fix:** Delete the two trailing markers (keep the explanatory comments themselves).

### IN-05: Editor `response.txt` dump in `GetMessagesRoutine` still writes before the result check

**File:** `Assets/Scripts/Main/ChatManager.cs:1292-1299`
**Issue:** The round-5 note stands: the dump executes before the `www.result != Success` check at `:1301`, so a failed/timed-out request clobbers the last good dump with an empty body. It is now `#if UNITY_EDITOR`-gated, so device impact is zero — and since the sibling in `SyncLatestMessages` (`:625-632`, correctly ordered after its result check) dumps to the *same file path*, the debugging value is marginal anyway.
**Fix:** Delete both Editor dumps (and their `Debug.Log("Saved to: ...")` companions) as part of the phase-close cleanup rather than reordering.

### IN-06: Dead debug code with hardcoded profile ids and un-gated payload dumps

**File:** `Assets/Scripts/Main/Manager.cs:3930-3970`; `Assets/Scripts/Main/WappiUnitySync.cs`
**Issue:** Two legacy debug artifacts, both compiled into builds but currently unreachable: (1) `Manager.GetWhatsappMesseges()` has **zero callers**, hardcodes real Wappi profile ids/message ids in its request URLs (plus six more in commented variants), and writes `response.txt` un-gated. (2) `WappiUnitySync` is **not attached in Main.unity** (component GUID absent from the scene), but if ever re-added it would write `response.txt` un-gated and `Debug.Log` full message payloads (`Full Message Data: {msg}`, line 64 — PII) per message — and it uses async/await, violating the project's coroutine-only networking rule.
**Fix:** Delete `GetWhatsappMesseges()` outright; delete `WappiUnitySync.cs` + `.meta` (or park the call-detection experiment outside `Assets/` if still wanted someday). Update the `WappiUnitySync` mention in `LocalDataWipe.cs:29`'s comment and CLAUDE.md's helper list when removed.

---

## Phase-Close Cleanup Inventory (definitive strip list before v1.1 prod)

Post-cleanup verification: `grep -rn "\[D15\]\|\[D2-view\]\|\[D2-merge\]\|\[D12\]\|response\.txt\|Diagnostic" Assets/Scripts --include="*.cs"` should return only the KEEP rows.

| # | Artifact | Location | Current guard | Action |
|---|----------|----------|---------------|--------|
| 1 | `[D2-view]` reactions-changed log | `MessageItemView.cs:4659-4661` | **Compiled** | **REMOVE** (WR-02). Keep the render chain around it. |
| 2 | `[D2-view]` post-render state log | `MessageItemView.cs:4681-4687` | **Compiled** | **REMOVE** (WR-02). Keep `RefreshReactionsNextFrame`'s `RefreshReactionsVisual()` call — that is the WR-01 behavioral pin, not diagnostics. |
| 3 | `Diagnostic*` properties | `ReactionPillView.cs:66-69` | Compiled (public) | **REMOVE** with #2 — dead once the log goes. |
| 4 | `[D15]` wa-reaction shape log | `ChatManager.cs:658-665` | **Compiled** — fires per WA reaction raw per ~3 s poll | **REMOVE** (WR-02). Conclusion documented in CLAUDE.md (D15 platform limit). |
| 5 | `[D15-probe]` arming block + `_d15ProbeArmed` | `ChatManager.cs:46-48, 667-684` | `#if UNITY_EDITOR` | **KEEP** gated — documented v1.2 detection seam for the absence-based WA reconcile. Fix the "per Editor session" comment (IN-03). |
| 6 | `[D15-probe]` report | `ChatManager.QuoteResolve.cs:142-150` | `#if UNITY_EDITOR` | **KEEP** gated (pairs with #5). |
| 7 | `[D2-merge]` suppression log | `TelegramReactionMerge.cs:75-77` | `#if UNITY_EDITOR` | **REMOVE** — the displaced discrimination is now pinned by the test suite; the log only adds noise to EditMode runs. |
| 8 | `response.txt` dump, `SyncAllChats` | `ChatManager.cs:456-461` | **Compiled — NOT gated** | **GATE or DELETE** (WR-01 — the priority item). |
| 9 | `response.txt` dump, `SyncLatestMessages` | `ChatManager.cs:625-632` | `#if UNITY_EDITOR` | **DELETE** (IN-03-lineage debt). |
| 10 | `response.txt` dump, `GetMessagesRoutine` | `ChatManager.cs:1292-1299` | `#if UNITY_EDITOR`, pre-result-check | **DELETE** (IN-05). |
| 11 | `Debug.Log("Saved to: ...")` companions | with #8/#9/#10 | mixed | **DELETE** with their dumps. |
| 12 | `[D12]` flow-trace logs | `EmptyStateView.cs:266-271, 282-284, 303-304` | `#if UNITY_EDITOR` ("grep-removable" by design) | **REMOVE** — D12 closed in round 4. |
| 13 | `[D12]` abort warnings | `EmptyStateView.cs:313, 319` | Compiled | **KEEP** — genuine error-path warnings, not diagnostics. Optionally retag `[EmptyStateView]`. |
| 14 | `GetWhatsappMesseges()` | `Manager.cs:3930-3970` | Compiled, zero callers, hardcoded ids | **DELETE** (IN-06). |
| 15 | `WappiUnitySync.cs` | whole file | Compiled, not in scene, full-payload PII logs | **DELETE** (IN-06). |
| 16 | `response.txt` deleters | `LocalDataWipe.cs:24`, `ChatManager.PrivacyClear.cs:102-105` | Compiled | **KEEP** — old installs still carry the file. |

## D15 Follow-up Riders (v1.2 — absence-based WA reconcile, deferred)

- **Detection trigger stays live:** inventory #5/#6 (the Editor probe) is the tripwire — if a WhatsApp `messages/id/get` payload ever exposes a `reactions`/`reaction` key, an absence-based reconcile becomes possible. `RefreshCachedMessageReactions` is already channel-gated at every call site, so a WA branch has a natural, zero-risk insertion point mirroring `TelegramReactionMerge`'s pure-seam pattern.
- **Carry IN-01 forward:** any future WA merge inherits the same displaced-emoji ambiguity; reuse the pure seam and its EditMode pins wholesale rather than re-deriving.
- **Safe to strip #4 now:** the `[D15]` ingest evidence (add-raws re-deliver `seen=true` per poll, no removal raw, no empty-body row) is permanently recorded in CLAUDE.md's `message/reaction` section — the collector has no remaining purpose.

---

_Reviewed: 2026-07-21T10:45:14Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
