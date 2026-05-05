# Bot Settings Edit Sheet — Keyboard-Done Descent Sync

**Date:** 2026-04-23
**Scope:** `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs`, `Assets/Scripts/Main/BotSettings/EditableField.cs`
**Affects:** Products edit sheet, Services edit sheet (both use `ItemEditSheet`)

## Problem

When the user taps the on-screen keyboard's **Done / return** key while editing a Product or Service field, the edit card stays at its lifted position for ~150–400 ms after the keyboard begins descending. The keyboard is almost off-screen before the card even starts moving. Card and keyboard should descend together.

The in-app **Done button** (which commits and closes the sheet) is unaffected — it already calls `Hide()` immediately, kicking off the slide-off in parallel with keyboard dismissal.

## Root Cause

`ItemEditSheet.Update()` (`ItemEditSheet.cs:96-138`) decides when to drop the sheet based on a polled OS keyboard-height signal:

1. iOS: `TouchScreenKeyboard.visible` doesn't flip false until the dismissal animation completes (~250 ms).
2. Android: JNI height shrinks gradually but stays >100 px most of the way down.
3. After the height reads 0, a **0.15 s `keyboardDownConfirmSeconds` debounce** must elapse before `heldKeyboardHeight` is zeroed.
4. Only then does `Update()` retween toward `baselineY`.

The 0.15 s debounce exists for a real reason (`ItemEditSheet.cs:108-122`): during a field-to-field tap, the OS height reading briefly blips to 0 even though the keyboard stays visible. Removing it reintroduces visible dips on field switches.

## Solution

Distinguish "user dismissed the keyboard" from "field-switch height blip" using **Unity UI focus state** — which is deterministic within one frame — as a second, faster signal that bypasses the height-debounce.

`EditableField.Blur()` already runs whenever a field's `onEndEdit` fires (which TMP_InputField emits on keyboard Done). The sheet just needs to know about it.

### Changes

#### 1. `EditableField.cs` — expose a blur event

Add a public event fired from `Blur()`:

```csharp
public event Action Blurred;

public void Blur(bool commit)
{
    if (!isFocused) return;
    isFocused = false;

    var current = input.text;
    if (commit && current != focusValue)
        OnCommitted.Invoke(current);

    input.DeactivateInputField();
    if (scrim != null && scrim.IsShowing) scrim.Hide();
    OnBlurred();
    Blurred?.Invoke();
}
```

The existing protected `OnBlurred()` virtual hook stays — `Blurred` is a separate public surface for outside listeners (the sheet).

#### 2. `ItemEditSheet.cs` — react to blur with a one-frame focus check

**Subscribe in `Awake()`** (after the existing button wiring):

```csharp
nameField.Blurred += HandleFieldBlurred;
priceField.Blurred += HandleFieldBlurred;
descField.Blurred += HandleFieldBlurred;
```

**Unsubscribe in `OnDestroy()`** (new method):

```csharp
private void OnDestroy()
{
    if (nameField != null) nameField.Blurred -= HandleFieldBlurred;
    if (priceField != null) priceField.Blurred -= HandleFieldBlurred;
    if (descField != null) descField.Blurred -= HandleFieldBlurred;
}
```

**Handler with one-frame focus check:**

```csharp
private Coroutine pendingDismissCheck;

private void HandleFieldBlurred()
{
    if (mode != SheetMode.Shown) return;
    if (pendingDismissCheck != null) StopCoroutine(pendingDismissCheck);
    pendingDismissCheck = StartCoroutine(CheckDismissalNextFrame());
}

private IEnumerator CheckDismissalNextFrame()
{
    yield return null;  // let same-frame field-switch select() handlers run
    pendingDismissCheck = null;

    // If another field grabbed focus, this was a field-switch, not a dismissal.
    // Let the existing height-debounce path keep the sheet up.
    if (GetFocusedField() != null) yield break;

    // Explicit dismissal: bypass the 0.15 s height-debounce immediately so the
    // sheet starts descending in sync with the keyboard.
    heldKeyboardHeight = 0f;
    lastPositiveKeyboardTime = float.NegativeInfinity;
    // Update() will pick this up next frame and retween toward baselineY using
    // the existing keyboardFollowDuration / OutQuad path.
}
```

The coroutine pattern (rather than a `bool flag + Update() check`) is used because we want the check to run exactly once one frame after the blur, not on every Update tick.

`StopCoroutine` on the previous handle prevents stacking: if multiple fields blur in the same tap (rare, but possible if a field-switch chain happens), only the most recent check runs.

#### 3. Tuning notes

- The `keyboardDownConfirmSeconds = 0.15 s` debounce stays untouched — it remains the safety net for non-Done dismissal paths (back-scrim outside-tap, OS hide via gesture, focus loss without our blur path).
- `keyboardFollowDuration = 0.1 s` and `Ease.OutQuad` already match the keyboard's descent feel; no change needed.
- If the descent still trails slightly on iOS, the existing `Update()` retween will handle it with the same easing — the difference is just where the tween *starts*.

## Why not Option B (TouchScreenKeyboard.status)

Considered: poll `inputField.touchScreenKeyboard.status` for `Done`/`Canceled`. Rejected because TMP_InputField's `touchScreenKeyboard` reference is opaque — null on multi-line fields with "Hide Mobile Input" enabled, and inconsistent across Android keyboard implementations. Focus state is more reliable.

## Why not Option C (shrink the debounce)

Considered: drop `keyboardDownConfirmSeconds` from 0.15 s to ~0.03 s. Rejected because (a) it reintroduces field-switch dips that the debounce was added to fix, and (b) it doesn't help iOS, where the lag is dominated by `TouchScreenKeyboard.visible` not flipping false until the dismissal animation completes — not by the debounce duration.

## Risk & Edge Cases

| Case | Behavior |
|---|---|
| User taps keyboard Done | Field blurs → 1-frame check finds no focused field → debounce bypassed → sheet descends with keyboard. ✅ fixed |
| User taps another field | Field A blurs → 1-frame check finds field B focused → no-op → existing Update path keeps sheet lifted. ✅ unchanged |
| User taps the back-scrim (outside) | `FocusScrim` triggers `Blur(commit: true)` → same fast path. ✅ improved (was using debounce) |
| User taps in-app Done button | `Commit()` → `Hide()` → sheet slides off. ✅ unchanged |
| Sheet shown but no field ever focused | No blur event → no handler runs → existing Update path governs. ✅ safe |
| Sheet `Hide()` mid-dismissal-check | `OnDestroy` is not called (sheet is reused), but `mode` becomes `Hiding` → handler's `mode != SheetMode.Shown` guard skips. ✅ safe |

## Files Touched

- `Assets/Scripts/Main/BotSettings/EditableField.cs` — add `Blurred` event, fire from `Blur()`
- `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs` — subscribe to blur events, add coroutine handler, add `OnDestroy` cleanup, add `using System.Collections;` if missing

No prefab changes, no inspector changes, no new SerializeFields.

## Testing

Manual on device (Android primary, iOS secondary):

1. Open Bot Settings → Products → tap a product card → edit sheet slides up with keyboard.
2. Tap a field, type, press the **Done/return key on the keyboard**. ✓ Sheet starts descending the same moment as the keyboard.
3. Tap field A, then immediately tap field B (without dismissing). ✓ Sheet stays lifted, no dip.
4. Tap field, then tap the dim area outside the sheet (back-scrim). ✓ Sheet starts descending immediately.
5. Tap field, then tap the in-app Done button. ✓ Sheet slides fully off-screen as before.
6. Repeat 1–5 for Services.
