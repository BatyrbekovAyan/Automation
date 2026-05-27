# Deferred Keyboard Dismiss (Finger-Up)

**Date**: 2026-05-27
**Status**: Approved, awaiting implementation plan
**Scope**: new `Assets/Scripts/Chat/DeferredDismissInputField.cs`, edit `Assets/Scripts/Chat/MessagesBottomPanel.cs`, one-time `MonoScript` swaps on input-field prefabs and scene instances, `Navigation.Mode.None` set on chat-panel buttons.

## 1. Problem

Unity's default `TMP_InputField` deselects on `PointerDown` of any non-input target. On iOS this triggers the OS `resignFirstResponder` cascade the moment the finger lands, before the user has even released the tap. Concrete failure mode that motivated this work: with the keyboard up, tapping the chat panel's `+` button causes the OS keyboard to start sliding down on finger-down, then the AttachSheet briefly slides up on the click, then closes again on release because the EventSystem / animation race produces a "tapped elsewhere" result. The user sees the sheet flicker.

The deeper issue is broader than the AttachSheet. The whole app's input-field dismissal happens at the wrong moment in the gesture. Tapping a button that wants to *replace* the keyboard with something else (an attach sheet, a picker), tapping into another input field, or starting a scroll all share the same root problem: the keyboard is yanked away on finger-down, before the gesture's destination is known.

A recent commit ([a8fda3b](Assets/Scripts/Chat/MessagesBottomPanel.cs)) fixed the `+` button case by setting `Navigation.Mode.None` on it. The working copy currently has that fix reverted, but even with it restored, the underlying behavior — keyboard dismissal on `PointerDown` — applies to every other tap target in the app.

## 2. Goal

Move keyboard dismissal from `PointerDown` to `PointerUp` for every `TMP_InputField` in the project. After the change:

- Tapping anywhere outside the focused input field defers dismissal until the finger is released.
- Tapping directly into another input field is treated as a smooth focus-switch — the keyboard never animates down between fields.
- Explicit programmatic dismissals (`DeactivateInputField` calls from app code) keep their immediate-dismiss semantics.
- The `+`-button AttachSheet bug disappears as a side effect.

## 3. Scope

**In scope**

- `DeferredDismissInputField : TMP_InputField` — subclass that overrides `OnDeselect` / `OnSelect` / `OnDisable` and adds an `Update` that polls the Input System for finger release.
- `MonoScript` reference swaps on every `TMP_InputField` instance in the project (scene + prefabs) → `DeferredDismissInputField`.
- Restore `Navigation.Mode.None` on `MessagesBottomPanel.attachButton`, add the same to `micButton` and `sendButton`.

**Out of scope**

- Interactive drag-down keyboard dismissal (WhatsApp/iMessage style where the keyboard follows the finger as the user drags through it). After-scroll dismissal on `PointerUp` is in scope; interactive drag dismissal is not.
- Changing `EditableField.Blur` / `ChatSearchBar.Hide` / `AttachSheet.Open`'s explicit `DeactivateInputField` calls — they bypass `OnDeselect` and remain immediate.
- Replacing or subclassing Unity's `InputSystemUIInputModule`.
- The `KeepKeyboardOpenRoutine` in `MessagesBottomPanel.OnSendClicked` — left in place as defensive belt-and-suspenders; with the new flow it should rarely fire, but costs nothing.

## 4. Architecture

The dismissal-timing fix lives at the level where the state actually exists: the input field itself. Each `TMP_InputField` instance is replaced with `DeferredDismissInputField`, which owns its own `dismissPending` flag and decides — on the next frame when the finger is no longer pressed — whether to complete the dismissal or skip it (smooth-switch).

No scene-level coordinator, no `InputModule` override, no static singleton. The field's lifecycle is its own.

```
PointerDown on non-input target
  └── EventSystem.SetSelectedGameObject(newTarget)
        └── A.OnDeselect (DeferredDismissInputField)
              └── dismissPending = true   ← keyboard stays up

PointerUp (next frame or later)
  └── A.Update sees !IsPointerPressed && dismissPending
        ├── currentSelectedGameObject is another TMP_InputField?
        │     └── yes → smooth switch, clear pending, no dismiss
        └── otherwise → base.OnDeselect → DeactivateInputField → keyboard down
```

The `Navigation.Mode.None` change on chat-panel buttons is a complementary belt-and-suspenders fix: it prevents `EventSystem.SetSelectedGameObject` from ever being called for those buttons on `PointerDown`, so `A.OnDeselect` doesn't even fire. Without it the defer mechanism still works correctly; with it the EventSystem state stays cleaner (the input field remains the selected object throughout).

## 5. Components

### 5.1 `DeferredDismissInputField`

New file at `Assets/Scripts/Chat/DeferredDismissInputField.cs`. ~60 lines.

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-50)]
public class DeferredDismissInputField : TMP_InputField
{
    private bool dismissPending;

    public override void OnDeselect(BaseEventData eventData)
    {
        dismissPending = true;
    }

    public override void OnSelect(BaseEventData eventData)
    {
        dismissPending = false;
        base.OnSelect(eventData);
    }

    protected override void OnDisable()
    {
        if (dismissPending)
        {
            dismissPending = false;
            base.OnDeselect(new BaseEventData(EventSystem.current));
        }
        base.OnDisable();
    }

    private void Update()
    {
        if (!dismissPending) return;
        if (IsPointerPressed()) return;

        dismissPending = false;

        var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel != null && sel.GetComponent<TMP_InputField>() != null) return;

        base.OnDeselect(new BaseEventData(EventSystem.current));
    }

    private static bool IsPointerPressed()
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) return true;
        if (Pointer.current != null && Pointer.current.press.isPressed) return true;
        return false;
    }
}
```

**Why these specific choices**

- `OnDeselect` is the single Unity-internal path for "EventSystem decided this field is no longer focused." Overriding it captures all and only the automatic-dismiss cases. Explicit `DeactivateInputField` calls go through a different path and bypass this override entirely.
- `OnSelect` cancels a pending dismiss because the field was re-focused before the finger came up.
- `OnDisable` flushes pending dismiss to prevent zombie state across screen changes or panel hides.
- `[DefaultExecutionOrder(-50)]` keeps `Update` ahead of consumers like `EditableField.Update` (default 0) that poll `isFocused`. Avoids a one-frame stale-read on the smooth-switch path.
- `IsPointerPressed` uses the new Input System — same module already imported by `KeyboardAwarePanel`. Touchscreen first (mobile), Pointer fallback (editor mouse). No reliance on `EventSystem.IsPointerOverGameObject` (which needs a pointerId).

### 5.2 Prefab / scene wiring

One-time editor work, no runtime code. Every active `TMP_InputField` in the project gets its `Script` reference (the `MonoScript` field at the top of the component inspector) changed to `DeferredDismissInputField`. Unity's Inspector supports this via right-click → "Change Script" or directly editing the YAML `m_Script` GUID.

**Project-wide TMP_InputField instances** (from `grep` over `Assets/Scripts/`):

| File / surface | Notes |
|---|---|
| Chat input in `MessagesBottomPanel` (Main scene) | The primary motivator |
| `EditableField` prefab in `Assets/Scripts/Main/BotSettings/` | Powers all single-line BotSettings fields |
| `EditableTextArea` prefab | Subclass of EditableField, multi-line |
| `ScrollableInputField` / `ScrollableTextArea` prefabs | BotSettings variants |
| `NumberDisplayField` prefab | BotSettings number variant |
| `ChatSearchBar` input | Search field on chat list |
| `ProfilePage` input fields | User profile inputs |
| `WhatsappCodeTimer` / `TelegramCodeTimer` code-entry inputs | Bot auth flow |
| `ItemEditSheet` name / price / desc fields | Inside BotSettings products/services |
| Any `Manager.cs` Wizard step inputs (Channel, Name, Whatsapp/Telegram auth, Business, etc.) | Onboarding flow |

The implementation plan should enumerate these from the actual scene / prefab YAML, not from this list — the source files reference `TMP_InputField` symbolically, but the *instances* live in `.unity` / `.prefab` files.

### 5.3 `MessagesBottomPanel` button navigation

Restore the regressed code in `OnEnable`:

```csharp
var attachNav = attachButton.navigation;
attachNav.mode = Navigation.Mode.None;
attachButton.navigation = attachNav;

var micNav = micButton.navigation;
micNav.mode = Navigation.Mode.None;
micButton.navigation = micNav;

var sendNav = sendButton.navigation;
sendNav.mode = Navigation.Mode.None;
sendButton.navigation = sendNav;
```

The send button already uses raw `PointerDown` via `EventTrigger`, but Navigation.Mode.None additionally prevents Unity's Selectable from setting the send button as the selected GameObject on press. Defense-in-depth.

## 6. Data Flow

### 6.1 Outside tap → keyboard dismisses on Up

```
1. User taps the message list (or any non-input area).
2. EventSystem.SetSelectedGameObject(target) [or null if m_DeselectOnBackgroundClick]
3. A.OnDeselect runs → dismissPending = true. Keyboard stays up.
4. User releases finger.
5. A.Update sees !IsPointerPressed, currentSelected is non-input → base.OnDeselect → DeactivateInputField → iOS resignFirstResponder → keyboard slides down.
```

### 6.2 Smooth switch (input A → input B)

```
1. User taps input B while A is focused.
2. EventSystem.SetSelectedGameObject(B).
3. A.OnDeselect → dismissPending = true.
4. B.OnSelect (inherited) → B.ActivateInputField → OS keyboard re-targeted to B. Visually no change.
5. User releases finger.
6. A.Update sees currentSelected = B, B has TMP_InputField component → skip dismiss, clear pending.
```

### 6.3 + button tap (AttachSheet)

```
1. User taps + button (Navigation.Mode.None).
2. EventSystem does NOT change selection (Selectable.OnPointerDown's Navigation.Mode != None gate short-circuits).
3. A stays focused. dismissPending stays false.
4. User releases finger.
5. Button.onClick → OnAttachClicked → AttachSheet.Toggle → Open → DeactivateInputField (explicit, immediate).
6. iOS keyboard slides down. AttachSheet's DOTween slides up. Concurrent animations.
```

### 6.4 Send button tap

```
1. User taps Send button (Navigation.Mode.None, EventTrigger.PointerDown wired).
2. EventSystem does not change selection (Mode.None).
3. EventTrigger fires immediately on PointerDown → OnSendClicked → message sent.
4. KeepKeyboardOpenRoutine WaitForEndOfFrame → re-Activate (defensive; should already be selected).
5. Keyboard stays up for next message. No flicker.
```

### 6.5 Hardware Done / programmatic dismiss

```
TMP_InputField.OnEndEdit (when reached via Done key, or onEndEdit listener)
  → input.DeactivateInputField()  (direct call, not via OnDeselect)
  → iOS resignFirstResponder, keyboard down.

dismissPending is irrelevant — it wasn't set.
```

## 7. Edge Cases

| Scenario | Behavior |
|---|---|
| Field A focused, user navigates away (Bot list / different page) before releasing | `OnDisable` runs (panel hidden) → flushes pending → keyboard dismisses immediately. No zombie state. |
| Field A focused, user taps and holds for a long time (e.g., long-press on message list) | `dismissPending` stays true. Keyboard stays up. On release, dismisses. Matches user expectation. |
| Field A focused, user starts a scroll gesture | `PointerDown` on scroll target sets `dismissPending`. During the drag, finger is still pressed → no dismiss. On release, dismiss fires. After-scroll dismissal, matches native iOS messaging. |
| Field A focused, AttachSheet opens (calls `DeactivateInputField` directly), user later taps in chat list | Sheet's call dismisses immediately (no defer). Field A is now deactivated. No `dismissPending` was set. Normal flow resumes. |
| Editor (no Touchscreen) | `IsPointerPressed` falls back to `Pointer.current` (mouse). Mouse release → dismiss. Works in Play Mode. |
| Multiple `DeferredDismissInputField` instances simultaneously enabled | Each owns its own `dismissPending`. Only the previously-focused one's defer matters; on smooth-switch the new field's OnSelect handles re-focus. No cross-instance coordination needed. |
| Field A focused, EventSystem briefly selects null (e.g., a Selectable with m_DeselectOnBackgroundClick semantics) then immediately re-selects A | A.OnDeselect → defer. A.OnSelect → clear pending. No dismiss, no flicker. |

## 8. Verification

On-device on iOS (the primary platform for the bug):

1. Chat input focused, tap `+` → AttachSheet slides up smoothly, does not re-close on release. Keyboard slides down as sheet slides up.
2. Chat input focused, tap on the message list → keyboard stays up during the tap, slides down on release.
3. Bot Settings: focus Name field, tap into Description → keyboard stays put, cursor switches.
4. Type and tap Send → message sends, keyboard stays up, no flicker.
5. Tap mic (with empty input text) → mic behavior preserved, no premature dismissal on Down.
6. Chat input focused, drag-scroll message list → keyboard dismisses on finger release, not on touch-down.
7. Hardware Bluetooth keyboard Done → immediate dismiss as before.
8. Focus chat input, navigate to Bots page (back gesture) → no zombie keyboard.

In editor:

9. Focus chat input (simulated), click outside → caret de-blinks on mouse release, not mouse down.
10. `Keyboard.current.kKey` simulated keyboard toggle in `KeyboardAwarePanel` still works.

Static / code:

11. Project-wide search of `.unity` and `.prefab` files confirms every active `TMP_InputField` has been migrated to `DeferredDismissInputField` (or has an explicit rationale for exclusion).
12. `Navigation.Mode.None` set on `attachButton`, `micButton`, `sendButton` in `MessagesBottomPanel.OnEnable`.

## 9. Risks and Mitigations

**Risk: Future Unity / TMP upgrade breaks the subclass override.**
TMP_InputField is a third-party-ish package (TMPro). Method signatures of `OnDeselect`, `OnSelect`, `OnDisable` could change. *Mitigation*: methods overridden are public/protected virtual in current TMP. If they break in a future TMP, the breakage is loud (compile error), not silent. Cost of fix: small.

**Risk: Some non-chat input field has behavior that depends on immediate `OnDeselect`-triggered dismissal.**
*Mitigation*: Section 7 enumerates every input field instance in the project. Implementation plan should verify each one's `onEndEdit` / `Blur` flow. `EditableField.Blur` and similar paths use direct `DeactivateInputField`, not `OnDeselect`, so they are unaffected.

**Risk: `Update` polling cost for every input field.**
*Mitigation*: Early-out on `!dismissPending` makes the common case a single boolean check. Per-frame cost is negligible. No object allocation in the hot path.

**Risk: Smooth-switch `GetComponent<TMP_InputField>()` allocation.**
*Mitigation*: Called only on the frame the user releases their finger after a defer — not in a hot loop. Cost is one `GetComponent` call per dismissal, which is fine.

**Risk: A button somewhere we didn't enumerate steals selection on Down.**
*Mitigation*: The defer mechanism handles it correctly even without `Navigation.Mode.None` — the visible result is that the input field's selection bounces (briefly the button is "selected" in the EventSystem) but the OS keyboard stays up, then the dismissal completes on Up. Worst case: a non-cosmetic regression in EventSystem state for one frame. Discoverable via QA.

## 10. Implementation Order

Recommended sequence for the implementation plan:

1. Write `DeferredDismissInputField.cs`. Compile-check.
2. Set `Navigation.Mode.None` in `MessagesBottomPanel.OnEnable` (restore + extend the regressed fix).
3. Migrate the chat panel's input field (`MessagesBottomPanel.inputField`) to `DeferredDismissInputField`. Manual test on iOS device: AttachSheet flicker fixed, send-keyboard-persistence preserved.
4. Migrate `EditableField` prefab's input. Manual test: BotSettings smooth-switch, FocusScrim outside-tap still works.
5. Migrate the remaining inputs (ChatSearchBar, ProfilePage, code timers, Manager wizard, etc.). Smoke test each.
6. Final on-device pass against §8 checklist.
