# Suggestions Slot: Pull-Down Dismiss from the Thread — Design

**Date:** 2026-08-19
**Status:** Approved (owner answered 3 questions, this doc)
**Scope:** Messages screen, suggestions-slot interaction model (sketch-005 winner E). Adds a
SECOND way to collapse the slot, next to the existing 42u drag handle. Client-only — no n8n, no
API, no scene edit.

## Problem

Today the suggestions slot («Вместе» panel, and the native keyboard that shares its region) can
only be collapsed by dragging the 42u `SuggestionSlotDragHandle` strip that sits directly under
the composer. The owner wants the WhatsApp/Telegram gesture as well:

> «когда скроллишь страницу сообщений … и в момент, когда доходишь до composer, composer
> начинает уходить вниз»

i.e. iOS `UIScrollView.keyboardDismissMode = .interactive`: while dragging the thread downward,
once the finger reaches the composer's top edge the composer (and the slot under it) tracks the
finger down.

This is a deliberate amendment to the LOCKED 005-E model, whose spec currently reads «`collapsed`
… is reachable ONLY via the handle». The owner is the one asking; the locked doc gets updated
rather than worked around.

## Owner decisions (2026-08-19)

1. **Scope of the gesture** — it dismisses BOTH tenants: the suggestions panel AND the native
   keyboard. Over the keyboard the slot must end up **Collapsed**, not hand the slot back to the
   panel (an explicit amendment of model E's `KeyboardDismissed → Panel` rule).
2. **Release rule** — the existing half-way detent snap, PLUS velocity: a fast downward flick
   collapses even when the slot has not travelled past half.
3. **Symmetry** — the velocity rule is added to the existing drag handle too, so the two entries
   into the same mechanic feel identical.

## Constraint that shapes the design

Unity cannot move the native iOS keyboard with the finger — `TouchScreenKeyboard` exposes no
interactive dismissal. The keyboard can only be dismissed, after which it plays its own ~250 ms
animation. So the gesture is interactive (1:1) for the PANEL and one-shot for the KEYBOARD. This
asymmetry is intrinsic to the platform, not a shortcut.

## Where the gesture is captured

Three candidate hook points were considered. The choice is forced by hard evidence:

**Rejected — a sibling `IDragHandler` component on the `Scroll` GameObject.** Bubbles carry
`SwipeToReply`, which forwards a vertical drag with a **typed direct call**
(`_scroll?.OnDrag(e)`, SwipeToReply.cs:75), not through `ExecuteEvents`. `DragShield` does the
same, and `SwipeToBack`'s left-band forwarding resolves `GetEventHandler<IDragHandler>` to the
BUBBLE (SwipeToReply is itself a drag handler) and then lands in that same typed call. A sibling
component on `Scroll` would therefore only ever see drags that start in the gaps BETWEEN bubbles
— i.e. dead over most of the thread. This is the project's recorded «never identify a gesture by
its raycast» trap.

**Rejected — polling `Pointer.current` in `SuggestionsController.Update`** (the `PumpThreadTap`
pattern). Immune to every forwarding quirk and needs no scene change, but the gesture then has no
idea who owns the drag, so every veto uGUI already computed (reaction bar, photo viewer,
fling-stop, back-swipe) would have to be re-derived by hand.

**Chosen — a plain C# event on `SnappyFlickScrollRect`.** Every path — direct hit, `SwipeToReply`
forward, `DragShield` forward, `SwipeToBack` forward — terminates in
`ScrollRect.OnBeginDrag/OnDrag/OnEndDrag`. That is the one true choke point. The component is
already on the `Scroll` GameObject and already overrides two of the three methods, so:

- **zero scene edits** — no builder, no wirer, no `Main.unity` save (the project has a recorded
  history of parallel sessions clobbering the scene);
- the recognizer receives the real `PointerEventData` (position, pointerId);
- `SnappyFlickScrollRect` learns nothing about slots — it raises three neutral drag events.

## Components

| File | Role | Approx. size |
|---|---|---|
| `Assets/Scripts/Main/SnappyFlickScrollRect.cs` (edit) | Raise `DragBegan` / `DragMoved` / `DragEnded` (`Action<PointerEventData>`) from its drag overrides; `OnDrag` gains an override that calls base then raises. Knows nothing about the slot. | ~15 lines |
| `Assets/Scripts/Chat/SuggestionSlotPullDown.cs` (new, pure static) | The engage predicate: has the finger crossed the composer's top edge, and is the gesture eligible at all. Fully unit-testable. | ~60 lines |
| `Assets/Scripts/Chat/SlotPullDownRecognizer.cs` (new, plain C#, NOT a MonoBehaviour) | Subscribes to the scroll's drag events. Owns `engaged` / `heightAtEngage` / `pointerId` / the velocity sampler; asks the controller for live geometry through `Func<>` providers exactly the way `SuggestionSlotDragHandle` does. Emits the SAME `Grabbed` / `Dragged(height)` / `Released(height, velocity)` plus a one-shot `KeyboardPullDown`. | ~120 lines |
| `Assets/Scripts/Chat/DragVelocitySampler.cs` (new, pure) | Finger velocity from (position, time) samples with an injectable clock. Shared by the handle and the recognizer. | ~40 lines |
| `SuggestionSlotDetents.SnapWithFlick(...)` (new method) | Velocity-aware snap; falls back to the existing `Snap` when there is no flick. | ~25 lines |
| `SuggestionSlotInput.PullDownDismiss` (new enum value) | The keyboard branch: `Keyboard → Collapsed + BlurField`, inert everywhere else, in BOTH reply modes. | ~10 lines |

**Deliberately untouched:** `Main.unity`, `SuggestionsPanel`, `KeyboardAwarePanel`,
`ScrollTopInsetCompensator`, `SuggestionSlotSwap`, `SuggestionSlotGestures`.

**No new height math.** «the finger dropped by Δ, so the slot is `heightAtEngage + Δ` clamped to
`[0, heightAtEngage]`» is exactly the existing
`SuggestionSlotGestures.HeightFromDrag(grab, delta, max)`, non-finite guards included. The new
gesture is a second SOURCE of the handle's events, not a second mechanic.

## Controller wiring

`SuggestionsController` gains proxying only. It resolves the scroll the same way it already
resolves the thread: `_threadInset.GetComponent<SnappyFlickScrollRect>()` (`_threadInset` itself
comes from `_keyboardMover.GetComponentInChildren<ScrollTopInsetCompensator>(true)`), so there is
no new `[SerializeField]` and nothing to stamp into the scene.

The recognizer's events land on the handle's EXISTING handlers:

- `Grabbed` → `HandleDragGrabbed`
- `Dragged` → `HandleDragMoved`
- `Released` → `HandleDragReleased`
- `KeyboardPullDown` → `ApplySlotInput(SuggestionSlotInput.PullDownDismiss)`

Two edits to existing code:

- `HandleDragReleased(float finalCanvasPx, float velocityCanvasPxPerSec)` — second argument, and
  it calls `SnapWithFlick` instead of `Snap`. `SuggestionSlotDragHandle.Released` gains the same
  second argument (owner decision 3), fed by the shared `DragVelocitySampler`.
- `CancelSlotDrag()` and `OnDisable()` additionally reset the recognizer; `OnDestroy()` detaches
  it from the scroll's events.

## Data flow

```
finger on the thread
 └─ SwipeToReply / DragShield / SwipeToBack / direct hit
     └─ ScrollRect.OnBeginDrag|OnDrag|OnEndDrag        ← the one choke point
         └─ SnappyFlickScrollRect.DragBegan|Moved|Ended
             └─ SlotPullDownRecognizer   (SuggestionSlotPullDown.ShouldEngage)
                 ├─ panel branch:    Dragged(h) → HandleDragMoved
                 │                     → KeyboardAwarePanel.VirtualBottomInset
                 │                     + SuggestionsPanel.SetSlotHeightLive
                 └─ keyboard branch: once per gesture → ApplySlotInput(PullDownDismiss)
                                       → blur; slot stays Collapsed
```

## Behaviour rules

### Panel branch (slot holds the panel — `Panel` or `Expanded`)

1. **Threshold** — the composer's top edge (top of `BottomPanel`; it rides the applied inset).
   Above it the finger scrolls the thread as usual and the slot does not move.
2. **Engage** — on the crossing frame, capture `heightAtEngage = AppliedBottomInset` and the
   finger's screen Y. There is no jump by construction: at that instant `fingerY == composerTop`,
   so the computed height equals the current one.
3. **Track** — `HeightFromDrag(heightAtEngage, fingerDelta, max: heightAtEngage)`. The ceiling is
   the engage height on purpose: the gesture may only shrink the slot and restore it, never grow
   it past where it started. Pure dismissal, as on iOS.
4. **The thread keeps scrolling** — the `ScrollRect` is not touched at all. The thread viewport
   grows from the bottom by exactly what the slot gives up (`ScrollTopInsetCompensator` pins the
   scroll's top edge), so the message under the finger stays under the finger while history is
   revealed below. This is the WhatsApp/Telegram behaviour.
5. `KeyboardAwarePanel.TrackInsetImmediately` is held for the whole engagement so the panel moves
   1:1 with no SmoothDamp lag — identical to the handle.

### Keyboard branch (slot holds the native keyboard; both reply modes)

6. Crossing the threshold fires ONCE per gesture: blur the composer field, mark the slot
   `Collapsed`. The panel must NOT come back — this is the amendment to `KeyboardDismissed → Panel`.
7. The rest of the touch is inert. The keyboard leaves on its own native animation and the
   composer follows IT, not the finger. Release snaps nothing.
8. Works in «Авто» too: `ResolveAuto` already maps `KeyboardDismissed → Collapsed`; there simply
   was no gesture invoking it. The recognizer is therefore NOT gated on `_semiAutoOn` (unlike
   `PumpThreadTap`).

Implementation note: because the controller sets `_slotState = Collapsed` itself, `Update`'s
level-triggered `!kbVisible && _slotState == Keyboard` block declines by itself — no extra guard
and no inset hold is needed here, since the composer coming down IS the intent.

### Flick-aware snap (shared by the handle and the new gesture)

9. A flick counts when `|velocity| >= FlickVelocity` AND the slot actually travelled
   `>= MinFlickTravel`. The travel minimum is mandatory: without it a fast scroll that merely
   grazed the composer's edge would kill the panel.
10. Flick down → `Collapsed`. Flick up → the tallest available detent not above the gesture's
    ceiling — `Expanded` for the handle, the engage height (i.e. «restore») for the pull-down.
    No flick → the existing half-way `Snap`, unchanged. Signature:

    ```csharp
    SlotDetent SnapWithFlick(float draggedCanvasPx, float standardCanvasPx,
                             float expandedCanvasPx, float velocityCanvasPxPerSec,
                             float travelCanvasPx, float ceilingCanvasPx)
    ```

    where `travelCanvasPx = |draggedCanvasPx - heightAtGrabCanvasPx|` (how far the SLOT moved,
    not the finger) and `ceilingCanvasPx` is the gesture's own drag ceiling — the Expanded detent
    for the handle, `heightAtEngage` for the pull-down.
11. Starting constants, living in the pure seam and tuned on device:
    `FlickVelocityCanvasPxPerSec = 2200` (≈730 CSS px/s), `MinFlickTravelCanvasPx = 60` (20 CSS px).

### Vetoes — the gesture does not engage at all when

- the slot is already `Collapsed` (nothing to dismiss);
- the «+» attach sheet, the photo viewer, or the reaction bar is open;
- the chat is still opening (`SlotOpenAllowed` — the same gate that already holds the auto-show);
- a back-swipe is in progress (`SwipeToBack.IsSliding`).

### Non-goals

- The gesture never RAISES the slot. Upward remains the thread tap, the ✦ key, and an incoming
  message. (A press always starts inside the thread, whose rect bottom sits ~396u above the
  composer's top edge, so the first crossing is always downward — an upward engagement cannot
  occur by construction.)
- It does not fire from the suggestion cards (they own their own scroll and the handle).
- It does not touch the «+» attach sheet's own tenancy.
- An incoming message cannot move the slot mid-gesture: `ApplySlotInput` already returns early
  while `_draggingSlot` is set.

## Edge cases

- **Pointer lost without `OnEndDrag`** (chat closed, screen deactivated): the recognizer is reset
  from `SuggestionsController.OnDisable` and from `CancelSlotDrag()`, so the slot is never
  stranded mid-gesture with `TrackInsetImmediately` latched on — the same protection the handle
  already has in its own `OnDisable`.
- **«+» sheet opens mid-gesture**: `Update`'s existing
  `if (_draggingSlot) { if (!AttachOpen) return; CancelSlotDrag(); }` branch already gives the
  sheet priority; the engagement is abandoned with no snap.
- **Second finger**: the recognizer filters on the `pointerId` captured at `DragBegan`.
- **Broken pointer frame** (non-finite coordinates): `HeightFromDrag` already guarantees the
  behaviour; `ShouldEngage` returns false.
- **Keyboard appears/disappears mid panel-gesture**: `Update` already refuses to read a
  finger-driven inset as a tenant change while `_draggingSlot`.

## Risks (named, not pre-solved)

1. **`LateUpdate` ordering.** `ScrollTopInsetCompensator.LateUpdate` trims the scroll's top and
   clamps content, while `ScrollRect.LateUpdate` runs inertia/elasticity. Until now the inset only
   moved under tweens and the keyboard, never under an active drag; now both change in the same
   frame. Thread jitter during the collapse is possible. The fix, if it shows, is a
   `[DefaultExecutionOrder]` on the compensator — applied only after seeing it on device.
2. **The threshold sits high.** With the panel open the composer's top edge is ~984 canvas units
   from the bottom — roughly the lower 40% of the screen — so many ordinary «scroll back through
   history» gestures will reach it. The half-way rule plus the flick travel minimum are the only
   guards; if it feels aggressive on device, the two constants move, not the architecture.
3. **The Editor cannot reproduce the native keyboard.** The keyboard branch is only exercisable
   through `KeyboardAwarePanel`'s simulated keyboard; real verification is an iOS device pass.

## Testing

EditMode, all against pure seams (`Assets/Tests/Editor/Chat/`):

- **`SuggestionSlotPullDownTests`** — above the threshold does not engage · the crossing frame
  engages · a second crossing does not re-engage · non-finite coordinates do not engage · each
  veto (Collapsed, attach sheet, unsettled chat, back-swipe) does not engage · **continuity**: at
  zero delta the height equals the engage height, i.e. no jump.
- **`DragVelocitySamplerTests`** — velocity under an injected clock · zero dt · a single sample ·
  reset.
- **`SuggestionSlotDetentsTests`** (additions) — flick down collapses even above half · flick down
  below the travel minimum falls back to `Snap` · flick up with the handle's ceiling yields
  `Expanded` · flick up with the pull-down's ceiling (= standard) yields `Standard` · with no
  flick the result is byte-identical to `Snap`.
- **`SuggestionSlotStateMachineTests`** (additions) — `PullDownDismiss`: `Keyboard → Collapsed`
  with `BlurField` in both modes · inert in every other state · inert for an out-of-range cast.

Run: `Tools/run-tests-headless.sh` with the Editor closed, or the `Temp/claude/run-tests.trigger`
bridge with it open.

**Not coverable by tests — goes to the device pass:** the 1:1 feel (the Editor takes the
`ApplyInstant` path, where `TrackInsetImmediately` is invisible), thread jitter, the two
constants, and the whole keyboard branch.

## Documentation to update on landing

The locked 005-E spec states «`collapsed` … is reachable ONLY via the handle»; that changes.

- `.claude/skills/sketch-findings-automation/references/suggestions-panel.md` — the second entry
  into Collapsed, and the `KeyboardDismissed → Panel` amendment for this gesture.
- `CLAUDE.md`, the suggestions-slot block — the gesture and its traps alongside the existing six.
- Project memory: `project_suggestions_panel_redesign.md`.
