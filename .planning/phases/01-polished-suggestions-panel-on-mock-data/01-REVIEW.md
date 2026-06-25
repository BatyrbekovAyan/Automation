---
phase: 01-polished-suggestions-panel-on-mock-data
reviewed: 2026-06-25T00:00:00Z
depth: deep
files_reviewed: 15
files_reviewed_list:
  - Assets/Scripts/Chat/ISuggestionsProvider.cs
  - Assets/Scripts/Chat/SuggestionRequest.cs
  - Assets/Scripts/Chat/SuggestionResult.cs
  - Assets/Scripts/Chat/SuggestionItem.cs
  - Assets/Scripts/Chat/SuggestionStatus.cs
  - Assets/Scripts/Chat/SuggestionSequenceGuard.cs
  - Assets/Scripts/Chat/MockSuggestionsProvider.cs
  - Assets/Scripts/Main/ChatManager.Suggestions.cs
  - Assets/Scripts/Chat/SemiAutoStore.cs
  - Assets/Scripts/UI/SuggestionCard.cs
  - Assets/Scripts/UI/SuggestionsPanel.cs
  - Assets/Scripts/UI/SemiAutoToggle.cs
  - Assets/Editor/SuggestionsPanelBuilder.cs
  - Assets/Scripts/Chat/SuggestionsController.cs
  - Assets/Editor/SuggestionsControllerWirer.cs
findings:
  critical: 0
  warning: 4
  info: 5
  total: 9
status: issues_found
---

# Phase 1: Code Review Report

**Reviewed:** 2026-06-25T00:00:00Z
**Depth:** deep
**Files Reviewed:** 15
**Status:** issues_found

## Summary

Reviewed the full Phase-1 semi-auto reply-suggestions feature at deep depth: the
`ISuggestionsProvider` seam + DTOs, the sequence guard, the mock provider, the
per-chat `SemiAutoStore`, the `ChatManager` accessors, the three views
(`SuggestionsPanel` / `SuggestionCard` / `SemiAutoToggle`), the two Editor builders,
and the `SuggestionsController` mediator. Cross-file analysis confirmed every member
the controller depends on actually exists on `ChatManager` (`OnChatSelected`,
`OnLiveMessagesReceived`, `OnActiveBotChanged`, `CurrentBotId`, `CurrentChatId`),
on `MessagesBottomPanel` (`inputField`), and on `MessageViewModel` (`isIncoming`,
`text`).

**The five hard invariants hold:**

1. **Seam purity** — nothing above the seam references `UnityWebRequest`, n8n, Wappi,
   `JsonConvert`, or any networking type. The mock is named on exactly one line
   (`SuggestionsController.Awake`, L28); Phase 2 swaps that single line.
2. **Never auto-sends / never writes the composer on the automatic path** —
   `HandleLive` (incoming) only calls `IssueRequest` (cards only). `HandleCardTapped`
   is the sole composer writer and never calls Send. Confirmed.
3. **Monotonic-seq + captured-chat + semi-auto guard** —
   `SuggestionSequenceGuard.IsCurrent` + the `_semiAutoOn` short-circuit + captured
   `chatId` correctly discard superseded / chat-switched / opted-out results.
4. **Subscribe/unsubscribe symmetry** — Awake/OnDestroy pair the bot/chat/toggle/panel
   events; OnEnable/OnDisable pair `OnLiveMessagesReceived`. No leaks, no double-subscribe.
5. **Editor builders** use the direct `Nobi.UiRoundedCorners` form and SerializedObject
   ref-wiring, matching the `BotSwitcherSheetBuilder` reference pattern.

The previously-found `SuggestionsController` fixes hold: the inactive-runner
synchronous fallback (`MockSuggestionsProvider.Request` L48), the `OnDisable`
`_requestSeq++` supersede (L64), the `_semiAutoOn` mid-flight opt-out guard
(`OnResult` L137), and the `ResetForNoOpenChat` sticky-chatId handling are all intact.

No critical issues. The findings below are correctness edge-cases and quality
observations, none of which break the seam contract.

## Warnings

### WR-01: Cards container's `SetSiblingIndex` does not survive the empty/error overlays being added after it

**File:** `Assets/Scripts/UI/SuggestionsPanel.cs:81-95` (RenderCards) / `Assets/Editor/SuggestionsPanelBuilder.cs:120-123`
**Issue:** The empty- and error-state overlays are built as direct children of the panel
(`BuildEmptyState`/`BuildErrorState`, parent = `panelGo.transform`) and are created
*after* `CardsContainer`, so they have a higher sibling index and draw on top. That is
correct for the overlay. But `RenderCards` instantiates cards into `cardsContainer`
(child of the panel) while the empty/error overlays are siblings of `cardsContainer`,
not children of it. When `Render` switches `Ok → Error → Ok`, `RenderCards` calls
`Clear()` then re-instantiates — fine — but the `RefreshButton` is also a sibling of
`cardsContainer` created *before* it (`BuildRefreshControl` is called at L101, cards at
L104). The refresh button therefore renders *behind* the cards container. The cards
container's `offsetMax` reserves `RefreshHit + Md` of top inset (L108) so the refresh
button area is visually clear, but the refresh `Button`'s raycast target is a
transparent full-rect Image only `RefreshHit` tall in the top-right — it is not
occluded. This works, but it is fragile: the layering depends entirely on creation
order, with no explicit `SetSiblingIndex` on the refresh button or the overlays.
**Fix:** Make the layering explicit so a future re-order of the build steps can't
silently break tap routing. After building all children, force the intended z-order:
```csharp
// in BuildPanel, after all children exist:
refreshButton.transform.SetAsLastSibling();   // refresh always on top of cards
empty.transform.SetAsLastSibling();
error.transform.SetAsLastSibling();            // states topmost overlay
```

### WR-02: `IssueRequest` fires with an empty/null `chatId` when restoring against a no-open-chat state

**File:** `Assets/Scripts/Chat/SuggestionsController.cs:84-95` (RestoreForActiveChat) / `119-133` (IssueRequest)
**Issue:** `HandleToggle` and `RestoreForActiveChat` both call `IssueRequest(null, null)`
without first checking that `ChatManager.Instance.CurrentChatId` is non-empty. The guard
`SuggestionSequenceGuard.IsCurrent` treats `null == null` (and `"" == ""`) as "same chat",
so a result built for an empty chat id will pass the guard and `Render` into the panel.
In normal flow the panel is only `Show()`n when a chat is open, so this is latent — but
`HandleToggle` is reachable any time the toggle is tapped, and `CurrentChatId` is sticky
to the previous bot's chat right after a bot switch (the very case `ResetForNoOpenChat`
exists to defend against). If the toggle were ever tappable in that window, suggestions
would be requested + persisted against a stale chat id. `SemiAutoStore.Key` would also
write `{botId}_semiAuto_` (trailing-empty key) for an empty chat id.
**Fix:** Gate request issuance on a real chat id at the single chokepoint:
```csharp
private void IssueRequest(string steerTowardText, string lastIncomingText)
{
    if (ChatManager.Instance == null || _provider == null) return;
    string chatId = ChatManager.Instance.CurrentChatId;
    if (string.IsNullOrEmpty(chatId)) return;   // no open chat → nothing to suggest
    long seq = ++_requestSeq;
    ...
}
```
And mirror the empty-id guard in `HandleToggle` before `SemiAutoStore.Set`.

### WR-03: Panel metrics are captured once and never recomputed, so a layout/orientation change leaves a stale hidden-Y

**File:** `Assets/Scripts/UI/SuggestionsPanel.cs:49-55` (CaptureMetrics) / `126-146`
**Issue:** `CaptureMetrics` latches `_metricsReady = true` on the first `OnEnable`/`Show`
and computes `_hiddenY = _restY - rt.rect.height`. It then early-returns forever
(`if (_metricsReady ...) return;`). If `OnChatSelected` fires while the controller (and
therefore the panel's parent `MessagesPanel`) is inactive — which the code explicitly
documents happens ~300 ms before `SlideInToMessages` activates the panel — `Show()` runs
`gameObject.SetActive(true)` + `CaptureMetrics()` while the panel is still inactive in
hierarchy and *may not yet have had a layout pass*. The panel has a fixed builder height
(`PanelHeight = 880`, `sizeDelta.y` set directly), so `rt.rect.height` is correct here
and this is currently safe. But the latch means if the panel height ever changes (e.g. a
future dynamic state or a safe-area adjustment), `_hiddenY` is never recomputed and the
slide-out would under/over-travel, leaving a sliver on screen or over-hiding.
**Fix:** Recompute on each `Show`/`Hide` instead of latching, or re-capture when the
rect height changes:
```csharp
private void CaptureMetrics()
{
    if (rt == null) return;
    _restY = rt.anchoredPosition.y;          // note: re-reading anchoredPosition.y after a
    _hiddenY = _restY - rt.rect.height;      // mid-slide Show would capture a transient Y —
    _metricsReady = true;                    // see WR-04; capture _restY only when at rest.
}
```
(Coordinate this with WR-04 — `_restY` must be the resting position, not a mid-tween value.)

### WR-04: `Show()` re-reads `_restY` indirectly via a stale latch but `Hide()`/`Show()` mid-tween can strand the panel off-rest

**File:** `Assets/Scripts/UI/SuggestionsPanel.cs:126-146`
**Issue:** `Show()` kills the active tween, snaps `anchoredPosition` to `_hiddenY`, then
tweens to `_restY`. `Hide()` tweens to `_hiddenY` with an `OnComplete` that calls
`SetActive(false)`. Because metrics are latched at first capture (WR-03), `_restY` is the
position recorded on the *first* show. This is fine as long as the panel's rest Y never
changes. However, there is an interaction with rapid toggle: tapping the semi-auto toggle
off then on quickly calls `Hide()` (starts a tween to `_hiddenY` with an `OnComplete`
deactivate) and then `Show()`. `Show()` does `_slideTween?.Kill()` (kills the hide tween,
so its `OnComplete` deactivate never runs — good) then `gameObject.SetActive(true)` and
snaps to `_hiddenY`. The CanvasGroup alpha is mid-fade-out from `Hide()` (DOFade to 0,
0.20s) and `Show()` starts a *new* DOFade to 1 over 0.25s on the same CanvasGroup — DOTween
will run both tweens on the same target unless killed. `Show()` does not kill the alpha
tween (only `_slideTween`), so two competing alpha tweens can briefly co-exist.
**Fix:** Kill all tweens on the rect *and* the canvas group at the start of both
`Show()` and `Hide()`:
```csharp
public void Show()
{
    gameObject.SetActive(true);
    CaptureMetrics();
    _slideTween?.Kill();
    if (canvasGroup != null) canvasGroup.DOKill();   // cancel any in-flight fade
    if (rt != null) { rt.DOKill(); rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, _hiddenY); }
    if (canvasGroup != null) { canvasGroup.alpha = 0f; canvasGroup.DOFade(1f, 0.25f); }
    if (rt != null) _slideTween = rt.DOAnchorPosY(_restY, 0.25f).SetEase(Ease.OutCubic);
}
```
Apply the symmetric `canvasGroup.DOKill()` in `Hide()`.

## Info

### IN-01: `MockSuggestionsProvider.RespondAfterLatency` has no cancellation, so a fast chat-switch leaves a stale coroutine resolving after the guard already moved on

**File:** `Assets/Scripts/Chat/MockSuggestionsProvider.cs:56-60`
**Issue:** When the runner is active, the latency coroutine runs to completion and invokes
the callback after `WaitForSeconds`. If the user switches chat or toggles off during the
1 s window, the callback still fires — but `OnResult` correctly discards it via the
`_semiAutoOn` short-circuit and the seq/chat guard, so this is purely a wasted invocation,
not a correctness bug. Noted only because the live Phase-2 provider must replicate the
same "always-callback, never auto-cancel" contract or the controller's guard assumptions
change. No fix required for Phase 1; flag in the Phase-2 provider contract notes.

### IN-02: `simulateError`/`simulateEmpty`/`simulateOutOfOrder`/`forcedEchoSeq` are public mutable fields on a shipped provider

**File:** `Assets/Scripts/Chat/MockSuggestionsProvider.cs:32-35`
**Issue:** The adversarial-path controls are `public` fields with no `[System.NonSerialized]`
or test-only gating. They are intentionally test-driven (per the docstring) and harmless
in the mock, but as public mutable state on a class that is `new`-ed in production
(`SuggestionsController.Awake`), they read as production surface area. Since the controller
never flips them, behavior is unaffected.
**Fix:** Optional — group them under an explicit `// TEST ONLY` region or expose them via
an internal setter so production code can't accidentally toggle the error path. Low priority.

### IN-03: `SemiAutoStore` orphaned-key growth on bot delete is documented-accepted but unbounded

**File:** `Assets/Scripts/Chat/SemiAutoStore.cs:9-11` (docstring) / `23`
**Issue:** Keys are `{botId}_semiAuto_{chatId}` and the docstring explicitly accepts
orphaned keys on bot delete (no enumeration/cleanup this milestone). PlayerPrefs has no
prefix-delete, so over many bots/chats these accumulate. Accepted by design; recording so
it isn't lost — a future cleanup hook on bot delete (or a key-registry) would bound it.
No action this phase.

### IN-04: `SuggestionsPanel.Clear()` mutates `_cards` while not the source of truth for badge/top-state across re-renders

**File:** `Assets/Scripts/UI/SuggestionsPanel.cs:113-122`
**Issue:** `Clear()` correctly unsubscribes each card's `OnTapped` before `Destroy`, so
there is no dangling-delegate leak (good — this is the kind of symmetry the rules require).
Minor: `RenderCards` calls `Clear()` (which already sets `emptyState`/`errorState` inactive
via its own path? no — `Clear()` only touches `_cards`). `RenderCards` then separately
sets empty/error inactive at L83-84 *before* `Clear()` at L85. The ordering is harmless but
slightly redundant with `RenderEmpty`/`RenderError`, which also call `Clear()`. No bug;
purely a readability note.
**Fix:** None required. Consider a single private `ResetStates()` helper that hides
empty+error+skeletons and clears cards, called at the top of each `Render*` method, to
remove the repeated `SetActiveSafe(..., false)` pairs.

### IN-05: `SuggestionCard.Setup` does not null-guard `replyText`/`intentLabel`/`cardButton`

**File:** `Assets/Scripts/UI/SuggestionCard.cs:23-35`
**Issue:** `Setup` guards `item == null` and `recommendedBadge != null`, but dereferences
`replyText.text`, `intentLabel.text`, and `cardButton.onClick` with no null check. These
are builder-wired serialized refs so they are non-null in the shipped prefab, and a missing
ref would (correctly) surface as a loud `NullReferenceException` at the checkpoint rather
than silently. Consistent with the rest of the codebase's "wired refs are trusted"
convention, so this is acceptable.
**Fix:** Optional defensive guard only if cards are ever instantiated from a partially-wired
template:
```csharp
if (replyText != null) replyText.text = item.text;
if (intentLabel != null) intentLabel.text = item.intentLabel;
```

---

_Reviewed: 2026-06-25T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
