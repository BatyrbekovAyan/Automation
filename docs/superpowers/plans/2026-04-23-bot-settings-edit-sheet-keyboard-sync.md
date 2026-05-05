# Bot Settings Edit Sheet — Keyboard-Done Descent Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the user taps the on-screen keyboard's Done/return key while editing a Product or Service field, the edit card descends in sync with the keyboard instead of lagging ~150–400 ms behind.

**Architecture:** Add a public `Blurred` event on `EditableField`, fired from `Blur()`. `ItemEditSheet` subscribes to all three fields' `Blurred` events. On each blur, a one-frame coroutine checks whether another field grabbed focus; if not, the sheet zeroes its held keyboard height immediately, bypassing the existing 0.15 s height-debounce. The existing `Update()` retween path then drops the sheet on the next frame using the same `keyboardFollowDuration` / OutQuad easing it already uses for follow moves. The 0.15 s debounce stays in place as a safety net for non-Done dismissal paths and field-switch height blips.

**Tech Stack:** Unity 6000.3.9f1, C#, TMP_InputField, DOTween (already in `ItemEditSheet`), Unity Coroutines.

**Testing approach:** This is a Unity mobile UI feature. There is no automated test harness in the project — verification is manual in the Unity Editor (compile check) and on device (Android primary, iOS secondary). Each task ends with a compile check and any applicable Play-mode/device check.

**Spec:** `docs/superpowers/specs/2026-04-23-bot-settings-edit-sheet-keyboard-sync-design.md`

---

## File Structure

- **Modify:** `Assets/Scripts/Main/BotSettings/EditableField.cs` — add `public event Action Blurred`, fire it from `Blur()` after `OnBlurred()` runs. One responsibility unchanged: own the focus/blur lifecycle and notify outside listeners.
- **Modify:** `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs` — subscribe to the three fields' `Blurred` events in `Awake`, unsubscribe in a new `OnDestroy`, add `HandleFieldBlurred` and `CheckDismissalNextFrame` coroutine, add `using System.Collections;`.

No prefab changes, no inspector changes, no new SerializeFields, no asset/scene edits.

---

## Task 1: Add `Blurred` event to `EditableField`

**Why:** Give `ItemEditSheet` a deterministic, Unity-UI-driven signal for "this field just blurred". Has to land before Task 2 because the sheet subscribes to it.

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings/EditableField.cs:24` (add event), `:71-83` (fire in `Blur`)

- [ ] **Step 1: Add the event field**

Open `Assets/Scripts/Main/BotSettings/EditableField.cs`. Find the `OnCommitted` declaration:

```csharp
        [Serializable] public class StringEvent : UnityEvent<string> { }
        public StringEvent OnCommitted = new StringEvent();
```

Add a new line directly below it:

```csharp
        [Serializable] public class StringEvent : UnityEvent<string> { }
        public StringEvent OnCommitted = new StringEvent();

        // Fires after Blur() runs (keyboard Done, outside-tap, programmatic blur).
        // ItemEditSheet listens to this to bypass its keyboard-height debounce when
        // the user explicitly dismisses the keyboard, so the sheet descends in sync.
        public event Action Blurred;
```

`System` (which provides `Action`) is already imported at the top of the file (`using System;`), so no new import is needed.

- [ ] **Step 2: Fire the event from `Blur()`**

Find the `Blur` method (currently `EditableField.cs:71-83`):

```csharp
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
        }
```

Replace it with:

```csharp
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

`Blurred` fires *after* `OnBlurred()` so subclass hooks still see the pre-notification state if they need it. The early `if (!isFocused) return` guard prevents double-fires.

- [ ] **Step 3: Verify it compiles**

Switch to the Unity Editor. Wait for the script reload to finish. Open the Console window. Confirm there are no red compile errors.

Expected: clean reload, no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/BotSettings/EditableField.cs
git commit -m "feat(bot-settings): add Blurred event to EditableField"
```

---

## Task 2: Subscribe `ItemEditSheet` to the blur events with one-frame focus check

**Why:** This is the actual fix. The sheet listens for blurs, distinguishes "user dismissed keyboard" from "user switched fields" using a one-frame focus check, and bypasses the slow height-debounce in the dismissal case.

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs:1-4` (imports), `:88-94` (subscribe in `Awake`), new `OnDestroy`, new `HandleFieldBlurred` + `CheckDismissalNextFrame` coroutine.

- [ ] **Step 1: Add the `System.Collections` import**

Open `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs`. The current imports are:

```csharp
using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
```

Add `System.Collections` so the coroutine compiles:

```csharp
using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
```

- [ ] **Step 2: Add the pending-check field next to the other private state**

Find the existing private state block (around `ItemEditSheet.cs:54-65`):

```csharp
        private Canvas canvas;
        private float baselineY;
        private bool isShown;
        private EditableField lastFocusedField;
        private SheetMode mode = SheetMode.Hidden;
        private float heldKeyboardHeight;
        private float lastPositiveKeyboardTime;
        // Tracks the target the active sheet-position tween is aiming at, so
        // we only kill-and-reissue a tween when the target actually shifts —
        // otherwise every Update would churn the tween and progress resets.
        private float activeTargetY = float.NaN;
        private Tween activePosTween;
```

Append a new line at the end of that block:

```csharp
        // Coroutine handle for the one-frame post-blur check. We hold it so a
        // rapid second blur cancels the first check (only the most recent matters).
        private Coroutine pendingDismissCheck;
```

- [ ] **Step 3: Subscribe in `Awake()`**

Find the existing `Awake()` (around `ItemEditSheet.cs:76-94`) and locate its tail end where the buttons are wired:

```csharp
            doneButton.onClick.AddListener(Commit);
            deleteButton.onClick.AddListener(() => PopupUI.Show(deleteConfirmPopup));
            PopupUI.WireFingerUp(deleteConfirmYes, ConfirmDelete);
            PopupUI.WireFingerUp(deleteConfirmNo, () => PopupUI.Hide(deleteConfirmPopup));
            if (scrimBehindFinger != null)
                scrimBehindFinger.OnRealRelease += Hide;
        }
```

Replace that closing block with:

```csharp
            doneButton.onClick.AddListener(Commit);
            deleteButton.onClick.AddListener(() => PopupUI.Show(deleteConfirmPopup));
            PopupUI.WireFingerUp(deleteConfirmYes, ConfirmDelete);
            PopupUI.WireFingerUp(deleteConfirmNo, () => PopupUI.Hide(deleteConfirmPopup));
            if (scrimBehindFinger != null)
                scrimBehindFinger.OnRealRelease += Hide;

            // Subscribe to field blur events so an explicit keyboard-Done dismissal
            // can bypass the 0.15 s height-debounce and descend in sync with the
            // keyboard. See HandleFieldBlurred for the focus-check logic.
            if (nameField != null) nameField.Blurred += HandleFieldBlurred;
            if (priceField != null) priceField.Blurred += HandleFieldBlurred;
            if (descField != null) descField.Blurred += HandleFieldBlurred;
        }
```

- [ ] **Step 4: Add `OnDestroy` for unsubscribe**

Add a new method directly after `Awake()` (before `Update()`):

```csharp
        private void OnDestroy()
        {
            if (nameField != null) nameField.Blurred -= HandleFieldBlurred;
            if (priceField != null) priceField.Blurred -= HandleFieldBlurred;
            if (descField != null) descField.Blurred -= HandleFieldBlurred;
        }
```

- [ ] **Step 5: Add `HandleFieldBlurred` and the coroutine**

Add these two methods at the end of the class, directly after `ConfirmDelete()`:

```csharp
        // Called when any of the three EditableFields fires its Blurred event.
        // Schedules a one-frame check: if no field gains focus, the user
        // dismissed the keyboard (Done key, outside-tap), so we drop the sheet
        // immediately instead of waiting for the OS keyboard-height debounce.
        private void HandleFieldBlurred()
        {
            if (mode != SheetMode.Shown) return;
            if (pendingDismissCheck != null) StopCoroutine(pendingDismissCheck);
            pendingDismissCheck = StartCoroutine(CheckDismissalNextFrame());
        }

        private IEnumerator CheckDismissalNextFrame()
        {
            // Yield one frame so any same-frame field-switch select() handlers
            // can mark the next field focused before we read focus state.
            yield return null;
            pendingDismissCheck = null;

            // If we're no longer Shown (e.g., Hide() ran during the wait),
            // skip — the sheet is already animating off.
            if (mode != SheetMode.Shown) yield break;

            // If another field grabbed focus, this was a field-switch, not a
            // dismissal. Leave the existing height-debounce path in charge.
            if (GetFocusedField() != null) yield break;

            // Explicit dismissal: bypass the 0.15 s height-debounce so Update()
            // retweens toward baselineY on the next tick using the existing
            // keyboardFollowDuration / OutQuad path.
            heldKeyboardHeight = 0f;
            lastPositiveKeyboardTime = float.NegativeInfinity;
        }
```

- [ ] **Step 6: Verify it compiles**

Switch to the Unity Editor. Wait for the script reload. Check the Console for compile errors.

Expected: clean reload, no errors.

- [ ] **Step 7: Editor sanity check (Game view)**

Enter Play mode at the 1080x2400 (mobile) Game view aspect. Open Bot Settings → Products → tap a product card to open the edit sheet. Tap a field. Confirm the sheet still rises (the existing lift path is unchanged) and that no errors appear in the Console.

Expected: no behavior regression in editor; the keyboard-Done sync fix needs a real device to verify (next task).

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Main/BotSettings/ItemEditSheet.cs
git commit -m "fix(bot-settings): sync edit-sheet descent with keyboard on Done"
```

---

## Task 3: On-device manual verification

**Why:** The bug only reproduces on a real OS keyboard. The Unity Editor has no system keyboard; the existing `Update()` polls return 0 in editor builds. We need to verify the fix on Android (primary) and iOS (secondary) and confirm no regressions in field-switch / outside-tap / in-app-Done paths.

**Files:** none modified.

- [ ] **Step 1: Build to Android**

Run from a terminal at the project root:

```bash
Unity -batchmode -nographics -projectPath . -buildTarget Android -quit
```

Or, in Unity Hub: open the project, `File → Build Settings → Android → Build`. Install the resulting `.apk` on a test device.

Expected: build succeeds with no errors related to `EditableField` or `ItemEditSheet`.

- [ ] **Step 2: Test the keyboard-Done sync (the fix)**

On the device:
1. Open the app → My Bots → tap a bot → Settings → Products tab.
2. Tap any product card to open the edit sheet (sheet slides up).
3. Tap the **name** field — keyboard appears, sheet rises with it.
4. Type a character, then tap the keyboard's **Done / return** key.

Expected: the sheet starts moving down on the same frame the keyboard does. They reach the bottom together. No visible "keyboard gone, sheet still up" pause.

If the sheet still lags, re-check Task 2 Steps 3–5 (subscribe, OnDestroy, handler) and the focus-check `yield return null`.

- [ ] **Step 3: Test that field-switch still works (regression check)**

Still in the edit sheet:
1. Tap the **name** field — sheet lifted.
2. Without dismissing the keyboard, tap the **price** field directly.

Expected: sheet stays lifted at the same height; no dip, no flicker. (This confirms the one-frame focus check correctly classifies the blur as a switch, not a dismissal.)

- [ ] **Step 4: Test back-scrim outside-tap**

1. Tap the **description** field — keyboard up, sheet lifted.
2. Tap the dim area outside the sheet (the back-scrim).

Expected: sheet starts descending immediately along with the keyboard. (This path used to depend on the height-debounce too; it benefits from the same fix because `FocusScrim` calls `Blur(commit:true)`.)

- [ ] **Step 5: Test in-app Done button (regression check)**

1. Tap any field, type something.
2. Tap the in-app **Done** button at the bottom of the sheet.

Expected: changes commit, the sheet slides fully off-screen as before, keyboard descends with it. (This path was already correct; verify nothing changed.)

- [ ] **Step 6: Repeat 2–5 for the Services tab**

Open Services → tap a service card → repeat all four scenarios above. The sheet is the same `ItemEditSheet`, so behavior should be identical, but verify because both paths matter.

- [ ] **Step 7: iOS verification (if hardware available)**

Build to iOS via Xcode (`File → Build Settings → iOS → Build`). Install on a test device. Repeat Steps 2–6.

Expected: same behavior. iOS was the worst-affected platform (per the spec, `TouchScreenKeyboard.visible` doesn't flip false until the dismissal animation completes), so the improvement should be most pronounced here.

- [ ] **Step 8: Commit nothing — this task is verification only**

If all manual checks pass, the implementation is complete. The branch is ready for the existing PR / merge workflow.

If any check fails, file the specific scenario as a follow-up bug rather than patching ad-hoc — the spec captures the intended behavior and any deviation deserves a real diagnosis.

---

## Done

After Task 3 passes, the work is complete:
- Keyboard-Done dismissal: card descends with keyboard ✓
- Field-switch: sheet stays lifted, no dip ✓
- Outside-tap: card descends with keyboard ✓
- In-app Done: unchanged ✓
- The 0.15 s height-debounce remains in place as a safety net for any path that doesn't go through `EditableField.Blur()`.
