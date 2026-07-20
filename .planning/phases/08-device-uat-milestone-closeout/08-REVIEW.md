---
phase: 08-device-uat-milestone-closeout
reviewed: 2026-07-20T15:37:53Z
depth: deep
files_reviewed: 7
files_reviewed_list:
  - Assets/Scripts/Chat/ReactionBarController.cs
  - Assets/Scripts/UI/MessageItemView.cs
  - Assets/Scripts/UI/ReactionPillView.cs
  - Assets/Scripts/Main/WhatsAppTabState.cs
  - Assets/Scripts/UI/EmptyStateView.cs
  - Assets/Scripts/UI/SyncingView.cs
  - Assets/Tests/Editor/Chat/ChannelTabStateResolverTests.cs
findings:
  critical: 1
  warning: 1
  info: 5
  total: 7
status: issues_found
---

# Phase 8: Code Review Report — Round 4 (gap-closure delta `e0a6547..fd8218b`)

**Reviewed:** 2026-07-20T15:37:53Z
**Depth:** deep
**Files Reviewed:** 7
**Status:** issues_found

## Summary

Round-4 gap-closure code only (plans 08-22 D2-view, 08-23 D12-ext, 08-24 D14). The round-3 report is preserved at `fa39bcc`; this review treats the round-4 device verdicts as priority evidence and traced the poll-driven reaction path end-to-end across `ChatManager.cs`, `ChatManager.LivePoll.cs`, `ReactionStore.cs`, `TelegramReactionMerge.cs`, `ReactionParser.cs`, and `MessageListView.cs`.

**Headline (CR-01):** the D2-view third-round device FAIL has a precise structural explanation — **the 08-22 self-heal was wired to the wrong trigger for the confirmed repro path.** The hardened re-render (`RefreshReactionsVisual` → `ForceReRender` → `SetAllDirty + ForceMeshUpdate`) is reachable ONLY from `ReactionBarController.Hide()` (bar dismiss). The device repro — reaction changed *in the Telegram app*, delivered by the live poll — flows through `MessageItemView.HandleReactionsChanged`, which still uses the plain, un-hardened `RenderReactions()` that round 3 already proved losable. Because `RefreshCachedMessageReactions` fires `OnMessageReactionsChanged` exactly once per data change (the `SameReactions` dedup at `ChatManager.cs:1855`), any single lost repaint on the poll path is permanent. The fix built in 08-22 is correct — it just never runs on the path that fails.

The D12-ext (08-23) and D14 (08-24) deltas are solid: `EmptyStateReasonPolicy` is a clean pure seam with the WhatsApp invariant pinned by tests; `HandleActiveChannelChanged` correctly re-derives instead of replaying `_lastReason` (round-3 WR-02 closed); `SyncingView`'s accent recolor mirrors `EmptyStateView`'s authored-color-capture pattern and is null-guarded throughout, with the WhatsApp copy and colors byte-identical (pass-through `ChannelAccent.Resolve` + authored-value capture at `Awake` before any recolor). New defects D15/D16 are out of the 7-file scope but were traced during the deep pass — precise round-5 pointers are recorded as IN-01/IN-02.

## Critical Issues

### CR-01: [D2-view residual mechanism] The poll-driven reaction repaint is still un-hardened and unhealable — the 08-22 fix cannot reach the confirmed repro path

**Files (mechanism chain):**
- `Assets/Scripts/UI/MessageItemView.cs:4666` — `HandleReactionsChanged` uses plain `RenderReactions()`; the hardened `RefreshReactionsVisual()` sits unused 15 lines above (`MessageItemView.cs:4648-4652`)
- `Assets/Scripts/Chat/ReactionBarController.cs:157` — the ONLY trigger of the deferred self-heal is `Hide()` (bar dismiss)
- `Assets/Scripts/Main/ChatManager.cs:1855` — `SameReactions` guard makes the event one-shot per data change (all three reconcile sites: `ChatManager.cs:744`, `1222`, `1322`)
- `Assets/Scripts/UI/ReactionPillView.cs:35-37` — the plain `Render` only assigns `label.text` (passive dirty-mark; no forced mesh flush)

**Issue:** The confirmed repro (reaction changed IN the Telegram app → live poll → `SyncLatestMessages` reconcile at `ChatManager.cs:744` → `RefreshCachedMessageReactions` → `OnMessageReactionsChanged`) lands in `HandleReactionsChanged`, where the `[D2-view]` log fires with correct data (device-confirmed). The repaint is then a single passive TMP dirty-mark. The structural defect: **this event fires exactly once per change** — `RefreshCachedMessageReactions` swallows every subsequent poll cycle via `SameReactions` because the VM data is already correct — so any transient loss of that one repaint is permanent for the session (until a full re-bind on chat re-open). Round 3 proved at least one same-frame loss mechanism exists in this exact hierarchy; 08-22 built the heal but attached it only to the bar-dismiss trigger, which the poll repro never touches.

The single-frame loss itself, on the poll path, is one of these candidates (in order of code-evidence strength — all three are neutralized by the same fix):

1. **Handler exception after the log (exact log-then-no-update fingerprint).** `ReactionPillView.Render` calls `UnicodeEmojiConverter.ConvertRealEmojisToSprites` unguarded (`ReactionPillView.cs:35`), and the converter walks the string with `char.ConvertToUtf32` (`UnicodeEmojiConverter.cs:71/89/106`), which **throws `ArgumentException` on a lone/unpaired surrogate**. A malformed reaction-emoji payload from tapi throws AFTER the `[D2-view]` log printed (`MessageItemView.cs:4661`), kills the repaint, **aborts the remaining `OnMessageReactionsChanged` subscribers, and kills the `SyncLatestMessages` coroutine mid-loop** (the event is invoked from inside it at `ChatManager.cs:1746/1858`). The next poll restarts the coroutine 3s later, but the data already matches → `SameReactions` → silent forever. See WR-01 for the hardening.
2. **RectMask2D cull-state loss.** The repro is cross-device: the reaction can land while the owner has the target bubble scrolled outside the viewport (`canvasRenderer.cull == true`; the pill's Image and label are `m_Maskable: 1` per `MessageTextOutgoing.prefab:4650`). A TMP text change applied while culled that is not re-pushed on uncull leaves exactly this stale pill; the passive dirty-mark is the vulnerable form, `ForceMeshUpdate` the reliable one.
3. **TMP sprite-submesh churn.** The pill's glyphs are TMP sprite tags spread across 33 separate sprite assets, so an emoji change (👍→❤️) typically swaps `TMP_SubMeshUI` objects (each with its own CanvasRenderer) rather than the parent text mesh — the class of update most sensitive to same-frame canvas/batch churn, and precisely what `ForceMeshUpdate` flushes deterministically.

**Fix (all in scoped files; WhatsApp byte-identical — same data rendered, only the mesh flush is forced; the WhatsApp live path reaches the same handler via `HandleReactionEvent`, so it self-heals identically):**

```csharp
// MessageItemView.cs — HandleReactionsChanged
private void HandleReactionsChanged(MessageViewModel changed)
{
    if (currentVm == null || changed == null) return;
    if (currentVm.messageId != changed.messageId) return;

    Debug.Log($"[D2-view] reactions changed id={changed.messageId} n={(changed.reactions?.Count ?? 0)}");

    RenderReactions();
    StartCoroutine(ForceRebuildRoutine());
    // D2-view round 4: the event is ONE-SHOT (SameReactions dedup) — harden the repaint the
    // same way the bar-dismiss path does, one frame later so it lands clear of any same-frame
    // canvas/cull/submesh churn. Idempotent; WhatsApp visual byte-identical.
    StartCoroutine(RefreshReactionsNextFrame());
}

private IEnumerator RefreshReactionsNextFrame()
{
    yield return null;
    RefreshReactionsVisual();   // RenderReactions + pill ForceReRender (SetAllDirty + ForceMeshUpdate)
}
```

For the round-5 device pass, also upgrade the `[D2-view]` log to discriminate the candidates: one frame after render, log `reactionPill.gameObject.activeSelf`, `label.text.Length`, and `label.canvasRenderer.cull` (still id + counts only — no content). If the pill logs healthy state and correct text while the screen shows stale, the residual is below CanvasRenderer and candidate 2/3 territory; if the follow-up log never prints, candidate 1 (exception) is confirmed via logcat.

**EditMode testability:** the wiring itself is not EditMode-testable (mesh/coroutine level). The candidate-1 hardening IS — see WR-01. A humble-object seam (e.g., extracting "should the handler force a re-render" into a pure policy) is possible but low-value; recommend device verification with the upgraded log instead.

## Warnings

### WR-01: One-shot reaction repaint chain is exception-unsafe — an unguarded converter throw aborts sibling subscribers and kills the sync cycle

**Files:** `Assets/Scripts/UI/ReactionPillView.cs:35` (unguarded converter call), `Assets/Scripts/UI/MessageItemView.cs:4666` (handler), `Assets/Scripts/Main/ChatManager.cs:1746/1858` (event invoked from inside the network coroutine), `Assets/Scripts/Chat/UnicodeEmojiConverter.cs:71,89,106` (`char.ConvertToUtf32` throws on lone surrogates)

**Issue:** `OnMessageReactionsChanged` is multicast to every bound `MessageItemView` (`MessageItemView.cs:468`) and is invoked from inside `SyncLatestMessages` / `GetMessagesRoutine` / `ValidateCachePageAgainstServer`. Any subscriber exception (a) skips all remaining bubbles' handlers and (b) terminates the invoking coroutine mid-loop — for `SyncLatestMessages` that also skips the brand-new-message diff, cache save, and `OnLiveMessagesReceived` for that cycle. The most plausible thrower in the reaction chain is `ConvertRealEmojisToSprites` on a malformed emoji string: `char.ConvertToUtf32(input, i)` raises `ArgumentException` when `input[i]` is an unpaired surrogate (the codebase's own `CodePointHex` at `ChatManager.cs:1696-1707` shows the surrogate-pair-aware idiom the converter's lookahead loop does not fully follow — its `i + length` advances can land mid-pair on malformed input). Combined with the one-shot event (CR-01), one bad payload = one permanent stale pill + one aborted sync cycle.

**Fix (two layers, either sufficient, both cheap):**

1. Harden the converter walk — before each `char.ConvertToUtf32(input, idx)`, verify `!char.IsSurrogate(input[idx]) || char.IsSurrogatePair(input, idx)`; otherwise emit the char raw and advance by 1.
2. Guard the pill render:

```csharp
// ReactionPillView.Render
string sprites;
try { sprites = UnicodeEmojiConverter.ConvertRealEmojisToSprites(raw, MissingEmojiMode.Hide); }
catch (System.Exception e)
{
    Debug.LogError($"[ReactionPill] emoji convert failed: {e.Message}");
    sprites = "";   // pill shows count-only rather than killing the event chain
}
```

**EditMode testability:** YES — feed `"\uD83D"` (lone high surrogate) and `"a\uDC00b"` (lone low surrogate) to `ConvertRealEmojisToSprites` and assert no-throw + passthrough; both run headless in the existing suite.

## Info

### IN-01: [NEW D16 pointer] Late-channel auth never stamps a sync window — the cover can't show because the window doesn't exist

**File:** `Assets/Scripts/UI/SyncingView.cs:60-62` (correct consumer), `Assets/Scripts/Main/Manager.cs:1478-1497` (the ONLY stamp sites), `Assets/Scripts/Main/Manager.cs:1690` (`ShowAuthSuccess` settings-flow branch), `Assets/Scripts/Main/Manager.cs:2787-2789` (`GetCreateTelegramWorkflow` → `CreateTelegramWorkflowFromEdit`)

`SyncingView` is NOT the defect site: its `OnEnable` catch-up correctly resumes any per-channel window via `IsChannelSyncing(CurrentBotId, ActiveChannel, out untilMs)`, and `HandleSyncing` re-themes per channel. The gap is upstream — `{bot}WhatsappSyncUntil` / `{bot}TelegramSyncUntil` are written ONLY in the bot-creation flow (`CreateBotFromForm`, gated on the creation-time `useWhatsapp`/`useTelegram`). Adding Telegram to an existing WhatsApp bot completes through `ShowAuthSuccess`'s `!isCreatingBot` branch / `CreateTelegramWorkflowFromEdit`, and no key is ever stamped — `IsChannelSyncing` reads `"0"` → no window → no cover (the documented 08-19 deviation, now device-confirmed as D16). **Round-5 fix location:** stamp `openBot.name + "TelegramSyncUntil"` (mirroring `Manager.cs:1490-1497`) at the late-auth success point for the Telegram channel. Note the symmetric WhatsApp case (Telegram-first bot later adds WhatsApp) has the same missing stamp — fixing it changes existing WhatsApp behavior (a cover would newly appear on late WA auth), so that half needs an explicit owner decision rather than a silent byte-identical claim.

### IN-02: [NEW D15 pointer] WhatsApp reaction removal can only be ingested as a NEW unseen reaction message — the seen-id path drops it, and the parser drops target-less events

**File:** `Assets/Scripts/Main/ChatManager.cs:663-671` (live ingest gated on `seenMessageIds.Add(raw.id)`), `ChatManager.cs:725-747` (seen-id branch has NO WhatsApp reaction handling — the reaction refresh at 743 is Telegram-gated), `ChatManager.cs:1202-1205` (`ValidateCachePageAgainstServer` routes reaction raws through `HandleReactionEvent` regardless of seen-state — but for WhatsApp this pass runs only on cache-drain pagination, never on the live poll), `Assets/Scripts/Chat/ReactionParser.cs:32` (empty `stanzaId` → event dropped), `Assets/Scripts/Chat/ReactionStore.cs:65-70` (removal semantics EXIST and are correct once an `IsRemoval` event reaches the store)

The removal machinery is implemented end-to-end (`ReactionParser` maps empty body → `IsRemoval`; `ReactionStore.ApplyToMessage` removes by `reactorKey`). The failure must therefore be at ingest: either (a) Wappi re-emits the removal under the SAME message id as the original reaction (dropped at `ChatManager.cs:663` — only `ValidateCachePageAgainstServer` would ever process it, and only during pagination), (b) Wappi emits no removal raw at all in `messages/get` (platform limit, like the reactions live-only transport already documented), or (c) the removal raw arrives without `stanzaId` (dropped at `ReactionParser.cs:32`). **Round-5 investigation pointer:** temporarily log every `type=="reaction"` raw in `SyncLatestMessages` — including seen-id hits — with `id`/`stanzaId`/`body`-emptiness to identify which shape a WA removal takes; the fix then targets exactly one of the three sites above. EditMode-testable once the shape is known (feed the captured raw through `ReactionParser.FromRaw` + `ReactionStore.ApplyToMessage`).

### IN-03: The 08-22 heal coroutine dies silently when `Hide()` fires from the swipe-back path

**File:** `Assets/Scripts/Chat/ReactionBarController.cs:157,160-164`, `Assets/Scripts/Chat/SwipeToBack.cs:332-336`

`SwipeToBack` fires `OnSlideOutComplete` (→ `Hide()` → `StartCoroutine(RefreshSourceNextFrame)`) and then deactivates the messages panel in the same stack (`SwipeToBack.cs:336`). The controller lives on that panel, so the just-started coroutine is killed before its `yield return null` resumes — the heal silently never runs. Currently benign: re-opening any chat re-binds all rows (fresh `RenderReactions` with correct data), so the stale pill can't be seen again before the re-bind heals it. Worth a guard anyway so the invariant doesn't depend on `MessageListView.Clear` behavior: `if (isActiveAndEnabled) StartCoroutine(...); else view.RefreshReactionsVisual();` (the immediate fallback is safe — on this path the row is about to be hidden with the panel). Not EditMode-testable (lifecycle).

### IN-04: `[D2-view]` compiled log is deliberate but unbounded and channel-global — schedule removal after UAT

**File:** `Assets/Scripts/UI/MessageItemView.cs:4659-4661`

The log is intentionally compiled (not `#if UNITY_EDITOR`) for device confirmation and correctly capped to id + count (T-08-22-01). It fires on every reaction change on BOTH channels, including production WhatsApp. Fine for the UAT window; tag it for removal (or `#if` gating) in the phase-close cleanup so it doesn't ship in the store build. If CR-01's fix is adopted, extend it per the diagnostic suggestion there for exactly one more round, then remove both together.

### IN-05: Test gap — `EmptyStateReasonPolicy` "raw NoBots is never demoted" invariant is unpinned

**File:** `Assets/Tests/Editor/Chat/ChannelTabStateResolverTests.cs:35-62`, `Assets/Scripts/Main/WhatsAppTabState.cs:37-40`

The 6 policy tests cover promotion, identity, agreement, and the null-resolver case, but not the fourth quadrant: `Effective(NoBotsExist, BotHasNoWhatsApp)` — a raw NoBots event while the resolver momentarily disagrees. The implementation correctly returns `NoBotsExist` (the condition requires `raw != NoBotsExist`), but nothing pins it — a future refactor to "resolver always wins" would pass all 6 existing tests while re-opening a variant of D12. One-line EditMode test:
`Assert.AreEqual(EmptyStateReason.NoBotsExist, EmptyStateReasonPolicy.Effective(EmptyStateReason.NoBotsExist, EmptyStateReason.BotHasNoWhatsApp));`
Minor placement note: `EmptyStateReasonPolicy` lives in `WhatsAppTabState.cs`, which is now three unrelated seams in one file — fine for v1, worth a rename/split when the file next churns.

---

_Reviewed: 2026-07-20T15:37:53Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
