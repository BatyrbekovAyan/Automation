# Sheet Drag-to-Dismiss — Design

## Goal

Make the grabber pills on the app's bottom sheets honest: drag down on the grabber zone to dismiss the sheet, finger-follow with proportional backdrop fade, snap back if released early. Applied to all four sheets — `Sheet_BotSwitcher`, `AttachSheet`, and (extension, same day) the `ProductEditSheet`/`ServiceEditSheet` `ItemEditSheet` instances in BotSettings, which had no grabber at all and receive both the pill and the behavior. (Inventory 2026-06-12: no other grabber-like elements exist; `ChatsSearchBar/Pill` is an input background, `Bot.prefab/Handle` is a toggle knob.)

## Non-goals

- No velocity/flick detection — distance threshold only (25% of sheet height).
- No drag-to-open or partial detents.
- No drag from the content area (cards list / tile row) — the zone is the grabber/header strip only, so it never fights the bot switcher's `ScrollRect`.
- No prefab changes — both sheets live in the scene.

## Architecture

### `Assets/Scripts/UI/SheetDragDismiss.cs` (new, shared)

`IBeginDragHandler/IDragHandler/IEndDragHandler` MonoBehaviour, modeled on the house `SwipeToClose` (public `UnityEvent` + follow-finger pattern) but sheet-specific:

- `[SerializeField] RectTransform panel` — the sheet panel to move.
- `[SerializeField] CanvasGroup backdropGroup` — optional; alpha scales with drag progress.
- `[SerializeField] float dismissFraction = 0.25f`, `snapBackSeconds = 0.2f`.
- `public UnityEvent onDismiss` — wired (persistently, via `UnityEventTools`) to the sheet's existing `Close()`.

Behavior:

- **BeginDrag**: ignore if `DOTween.IsTweening(panel)` (prevents grabbing mid open/close — killing `BotSwitcherSheet`'s open tween would strand its `isAnimating` flag). Capture shown Y, pointer Y, `rootCanvas.scaleFactor` (screen px → canvas units), backdrop base alpha.
- **Drag**: `offset = min(0, Δpointer/scale)` — downward only, panel follows finger. Backdrop alpha = base × (1 − offset/panelHeight).
- **EndDrag**: dragged > `panelHeight × dismissFraction` → `onDismiss` (both sheets' `Close()` already tween from the current position, so the dismissal animation is free). Else snap back: `DOAnchorPosY(shownY)` + backdrop refade, both `SetLink`ed.

### `AttachSheet.cs` — one-line guard fix

Its slide tweens are lambda-based `DOTween.To` with no target, so `DOTween.IsTweening(panel)` can't see them. Add `.SetTarget(_rt)` to both slide tweens so the mid-animation drag guard works for it too. (`BotSwitcherSheet` uses `DOAnchorPosY`, already target-linked.)

### `Assets/Editor/SheetDragDismissWirer.cs` (new) — `Tools/Sheets/Wire Drag Dismiss`

Surgical wirer (house pattern: `BotSettingsSwipeWirer`) — does NOT rebuild either sheet, so the freshly committed attach-sheet redesign is untouched:

- Finds `BotSwitcherSheet` (panel = its `Panel` child, zone height 172 = grabber 72 + title 100) and `AttachSheet` (panel = its own RT, zone height 96 = grabber 72 + gap 24) in the scene, inactive included.
- For each: destroys any existing `DragZone`, creates a transparent full-width `DragZone` Image (raycastTarget on, color alpha 0) anchored to the panel's top as the **last sibling** (wins raycasts over the grabber/title beneath it; `LayoutElement.ignoreLayout` opts it out of the bot switcher panel's `VerticalLayoutGroup`).
- Adds `SheetDragDismiss`, wires `panel`/`backdropGroup` via `SerializedObject` and `onDismiss → Close()` via `UnityEventTools.AddVoidPersistentListener`.
- Idempotent; errors loudly if either sheet is missing.

`BotSwitcherSheetBuilder`'s success log gains a reminder to re-run the wirer after a full sheet rebuild (the rebuild drops the zone). `AttachSheetBuilder` likewise sweeps its sheet on rebuild — same reminder applies but its builder is not modified beyond nothing (left untouched).

### ItemEditSheet extension (BotSettings product/service edit sheets)

Both `ItemEditSheet` instances live in `Assets/Prefabs/BotSettings.prefab`, so the wirer gains a prefab pass (`PrefabUtility.LoadPrefabContents` → modify → `SaveAsPrefabAsset`; instances inherit):

- **Grabber pill added** (they had none): same metrics as the other sheets (108×12, radius 6, `(0.78, 0.78, 0.80)`), centered 24 units below the sheet top — fits in the pre-existing 60-unit padding above the title, no layout shift.
- **DragZone**: height 160 (grabber strip 60 + title 100); the `Fields` container starts at 190, so inputs are untouched. `panel = sheetRoot`, `backdropGroup = scrimBehindGroup`.
- **`onDismiss → ItemEditSheet.Dismiss()` (new public method), NOT `Hide()`**: the scrim-tap path (`MaybeHide`) discards a newly added never-committed card on dismissal (`OnProductDeleted`/`OnServiceDeleted`); a bare `Hide()` from drag would leave a phantom blank card. `Dismiss()` extracts exactly those semantics; `MaybeHide` now delegates to it after its scrim-specific iOS reroute guard. Keyboard handling comes free: `Hide()` force-blurs all fields, and the sheet's keyboard-lift tweens are target-linked so the `IsTweening` drag guard covers them.
- Re-running `Tools/Rebuild Bot Settings Prefabs` rebuilds the edit sheets without grabber/zone — re-run the wirer afterwards, same as with the other two builders.

## Files touched

| File | Change |
| --- | --- |
| `Assets/Scripts/UI/SheetDragDismiss.cs` | New shared drag component. |
| `Assets/Scripts/Chat/AttachSheet.cs` | `.SetTarget(_rt)` on the two slide tweens. |
| `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs` | New public `Dismiss()` (discard-new-card semantics); `MaybeHide` delegates to it. |
| `Assets/Editor/SheetDragDismissWirer.cs` | New surgical wirer menu item; prefab pass for the two edit sheets (grabber pill + zone). |
| `Assets/Editor/BotSwitcherSheetBuilder.cs` | Success-log reminder to re-run the wirer. |
| `Assets/Scenes/Main.unity` | Two `DragZone` objects added by the wirer. |
| `Assets/Prefabs/BotSettings.prefab` | Grabber pill + `DragZone` on both edit sheets (wirer-generated). |

## Risks / things to watch

- **Multi-touch mid-drag close** (second finger taps backdrop while dragging): `Close()` runs, zone deactivates with the sheet, `OnEndDrag` may not fire — `dragging` resets on next BeginDrag; tweens are `SetLink`ed. Benign.
- **Wirer ordering**: must re-run after any future `Tools/Bot Switcher/Build Sheet` or attach-sheet rebuild. Logged in the wirer and the bot switcher builder.
- **Zone vs ScrollRect**: zone covers only the top strip; list drags below it are untouched.

## Verification

Compile + EditMode suite green (test bridge), wirer run via MCP, scene saved, then user GREEN: drag either grabber — sheet follows finger downward only, backdrop lightens, release past ~quarter height dismisses, early release snaps back; backdrop tap and item taps still work; bot switcher list still scrolls.
