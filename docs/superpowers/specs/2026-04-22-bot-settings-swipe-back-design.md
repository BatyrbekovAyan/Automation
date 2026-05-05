# Bot Settings — Swipe-Back Gesture

**Date:** 2026-04-22
**Scope:** `Assets/Scripts/Main/BotSettings.cs`, new `Assets/Scripts/Main/SwipeToBackBotSettings.cs`, new editor auto-wire script, `Assets/Prefabs/BotSettings.prefab`

## Motivation

Bot Settings currently opens and closes via a plain `GameObject.SetActive()` toggle
(`Bot.OpenSettings()` at `Bot.cs:73`, `BotSettings.OnBackPressed()` at
`BotSettings.cs:213`). The WhatsApp chat messages page already has a polished
iOS-style swipe-right-to-go-back gesture with parallax on the background (see
`Assets/Scripts/Chat/SwipeToBack.cs`). The bot-settings flow should feel
equivalent — swipe-right to dismiss, with BotsPage easing back into view behind
the dismissing panel, plus a matching slide-in animation when the page opens.

## Goals

- Swipe-right anywhere on the BotSettings page dismisses it back to BotsPage, with
  parallax on BotsPage (BotsPage sits at `-30% screenWidth` while BotSettings
  covers the full screen, and eases back to `0` as BotSettings slides out right).
- Match chat's physics exactly: `parallaxStrength = 0.3`, `snapSpeed = 10`,
  `slowSwipeThreshold = 0.4`, `flickVelocityThreshold = 1000 px/s`, same custom
  lerp with `1500 px/s` minimum speed floor.
- Opening `Bot.OpenSettings()` uses the same animation in reverse: BotSettings
  slides in from the right, BotsPage eases from `0` to `-30%` then (after
  deactivation) reset to `0`.
- Vertical scrolling inside the currently active tab must still work — the
  gesture only engages when the initial drag is more horizontal than vertical
  and directed rightward.
- Committing the swipe must still run the existing `OnBackPressed()` logic
  (revert unsaved edits from PlayerPrefs via `Manager.Instance.CloseSettings()`),
  not just hide the panel.
- Prefab wiring is automated via an editor script (same pattern as
  `Assets/Editor/BotSettingsRebuilder.cs`).

## Non-Goals

- No changes to chat `SwipeToBack.cs` — it stays as-is. We do not generalize it.
- No edge-swipe restriction (chat doesn't have one either — full-panel swipe is
  the app convention).
- No DOTween dependency added for this feature — reuse the manual-lerp pattern
  from chat.
- No changes to tab-switching animation or intra-tab scrolling behavior.
- No Android system back-button wiring in this pass (existing back button
  already handles that; out of scope).

## Architecture

### New component: `SwipeToBackBotSettings`

Mirrors `SwipeToBack.cs` in structure but scoped to the bot-settings flow. Lives
at `Assets/Scripts/Main/SwipeToBackBotSettings.cs`. Implements the same four
drag interfaces: `IInitializePotentialDragHandler`, `IBeginDragHandler`,
`IDragHandler`, `IEndDragHandler`.

Serialized fields:
- `RectTransform botSettingsPanelToSlide` — the panel that slides (the
  BotSettings root itself).
- `RectTransform botsPagePanel` — the background page (the BotsPage root
  RectTransform) that receives the parallax offset.
- Physics fields: `parallaxStrength`, `snapSpeed`, `slowSwipeThreshold`,
  `flickVelocityThreshold` — same defaults as chat.

Key differences from chat:
- No `chatScrollRect` field. The active ScrollRect is looked up at drag-begin
  time via `BotSettings.Instance.CurrentTabScrollRect` (see below).
- No `bottomTabPanel` field — the bot-settings flow's "behind" surface is just
  BotsPage.
- `UnityEvent onSwipeComplete` is replaced by a direct call to
  `BotSettings.Instance.OnBackPressed()` on commit. This ensures the existing
  revert-unsaved-edits logic fires.
- Exposes public `SlideInFromRight(Action onComplete = null)` and
  `SlideOutToBotsPage(bool instant = false)` methods so `Bot.OpenSettings()` and
  `OnBackPressed()` can drive the animation programmatically, not just via
  drag.

### Change: `BotSettings.cs`

- Add a public read-only property `ScrollRect CurrentTabScrollRect { get; }`
  that returns the vertical ScrollRect for whichever tab is currently active
  (General / Business / Products / Services / Prompts). Internal detail: each
  tab root already has a ScrollRect; we cache them or look up by the active tab
  enum. If no ScrollRect is active (e.g. during transition), returns `null`.
- Modify `OnBackPressed()` so that instead of calling
  `BotSettingsParent.transform.parent.gameObject.SetActive(false)` directly, it
  (1) runs the existing revert-PlayerPrefs step, (2) calls
  `BotsPage.Instance.gameObject.SetActive(true)` **before** the animation starts
  so the background is visible for parallax, and (3) calls
  `SwipeToBackBotSettings.Instance.SlideOutToBotsPage()` which animates the
  slide and then deactivates the BotSettings parent at the end of the coroutine.
- Guard against re-entry: if a snap coroutine is already running, subsequent
  `OnBackPressed()` calls are no-ops.

### Change: `Bot.cs` (OpenSettings)

`Bot.OpenSettings()` at `Bot.cs:73` currently does:
```
BotsPage.Instance.gameObject.SetActive(false);
Manager.BotSettingsParentStatic.transform.parent.gameObject.SetActive(true);
```

New flow:
```
Manager.BotSettingsParentStatic.transform.parent.gameObject.SetActive(true);
// BotsPage stays active underneath during the animation so parallax is visible.
SwipeToBackBotSettings.Instance.SlideInFromRight(() =>
{
    BotsPage.Instance.gameObject.SetActive(false);
    // Reset BotsPage parallax offset back to 0 for next time.
});
```

BotsPage is only deactivated once the slide-in finishes, so the parallax on the
background is visible throughout the opening transition.

### New editor script: `BotSettingsSwipeWirer`

Lives at `Assets/Editor/BotSettingsSwipeWirer.cs`. Menu item under
`Tools/Bot Settings/Wire Swipe Back`. When invoked:
1. Loads the `BotSettings.prefab` and the scene's BotsPage.
2. Adds `SwipeToBackBotSettings` to the BotSettings prefab root if missing.
3. Assigns `botSettingsPanelToSlide` to the prefab's own RectTransform.
4. Assigns `botsPagePanel` to the scene's BotsPage RectTransform (via
   `GameObject.Find` on the known hierarchy path, same pattern
   `BotSettingsRebuilder` uses).
5. Saves the prefab and marks the scene dirty.

## Data Flow

**Drag (user-initiated):**

1. Finger goes down on BotSettings → `OnInitializePotentialDrag` delegates to
   `CurrentTabScrollRect`.
2. Finger moves → `OnBeginDrag` decides horizontal vs. vertical. If horizontal
   and rightward, we engage swipe mode, disable `CurrentTabScrollRect.vertical`,
   ensure BotsPage is active, and stop any running snap.
3. `OnDrag` moves BotSettings by `eventData.delta.x / canvas.scaleFactor`,
   clamped to `>= 0`. BotsPage x is set to
   `-maxOffset + (maxOffset * progress)` where `progress = newX / screenWidth`.
4. `OnEndDrag` checks flick velocity (`dragDistanceX / dragDuration > 1000`
   AND `dragDistanceX > 20`) OR distance past 40% → commit. Otherwise snap back.
5. Commit coroutine animates to `x = screenWidth`, then calls
   `BotSettings.Instance.OnBackPressed()` which handles the revert-and-swap.
   To avoid recursion (OnBackPressed would itself try to animate), the
   coroutine sets an `isAnimating` flag that `OnBackPressed` checks — when
   true, `OnBackPressed` runs the revert step and deactivates BotSettings
   directly without starting a second animation. Snap-back coroutine animates
   to `x = 0` and restores BotsPage to `x = 0`.

**Programmatic open/close:**

- `SlideInFromRight(onComplete)`: sets panel x to `screenWidth` and BotsPage x
  to `0`, snaps to `(0, -maxOffset)` over the same lerp. Calls `onComplete`
  when done.
- `SlideOutToBotsPage()`: snaps panel x to `screenWidth` and BotsPage x to `0`,
  deactivates BotSettings parent at the end.

## Error Handling

- If `BotsPage.Instance` is null (unlikely but possible during scene load) the
  swipe component no-ops the parallax write but still animates the foreground.
- If `CurrentTabScrollRect` returns null (no active tab), skip the
  `vertical = false` step; horizontal drag still works.
- If `SwipeToBackBotSettings.Instance` is null when `Bot.OpenSettings()` runs
  (e.g. prefab not yet wired), fall back to the current instant `SetActive`
  behavior with a `Debug.LogWarning` telling the developer to run the wire
  editor script.

## Testing

Manual, in Unity Editor Game view at 1080x2400:

- Open Bot Settings from BotsPage → expect slide-in from right, BotsPage eases
  left then disappears.
- Slow swipe-right <40% → snap back to 0, BotsPage returns to 0.
- Slow swipe-right >40% → commit, OnBackPressed runs, unsaved edits revert.
- Fast flick right with small distance → commit (velocity path).
- Vertical scroll inside General/Business/Products/Services/Prompts tabs still
  works (swipe-down should not engage back gesture).
- Tap-back button still works (uses SlideOutToBotsPage programmatically).
- Open & close repeatedly — no drift in BotsPage `anchoredPosition.x` (should
  always return to 0 between sessions).

## Risks

- **Tab ScrollRect discovery.** If a future tab is added without updating the
  `CurrentTabScrollRect` switch, vertical scrolling inside that tab will be
  momentarily frozen during a horizontal mis-drag. Mitigated by a single
  switch/enum mapping in `BotSettings.cs` that's easy to extend.
- **Prefab wiring drift.** The editor auto-wire script must be re-run if the
  BotSettings prefab root changes. Same pattern as existing rebuilders — low
  risk, but documented in the script's summary comment.
- **Canvas scale factor.** `eventData.delta.x / canvas.scaleFactor` assumes a
  single root Canvas, same assumption chat's implementation makes. Safe for
  this project's single-scene architecture.
