using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// TMP_InputField subclass that defers keyboard dismissal from PointerDown to
/// PointerUp. The default TMP behavior calls DeactivateInputField the moment
/// EventSystem deselects the field — on iOS this fires resignFirstResponder
/// on finger-down, before the user has even released the tap, causing the
/// keyboard to slide down mid-gesture.
///
/// This subclass overrides OnDeselect to mark a pending dismiss and waits
/// for the Input System to report no finger pressed. If the new selection
/// is another TMP_InputField (focus-switch), the pending dismiss is cleared
/// and no animation runs.
///
/// Explicit programmatic dismissals (DeactivateInputField from AttachSheet,
/// EditableField.Blur, ChatSearchBar, etc.) bypass OnDeselect entirely and
/// keep their immediate-dismiss semantics.
/// </summary>
[DefaultExecutionOrder(-50)]
public class DeferredDismissInputField : TMP_InputField
{
    private bool dismissPending;

    // ── activation keyboard-text sync ────────────────────────────────
    // Android's shared IME session can deliver the PREVIOUS field's buffer
    // into a freshly activated field: TouchScreenKeyboard.Open sets the
    // native text asynchronously, and until the set lands the native getter
    // still returns the old session's text — which TMP's LateUpdate then
    // ingests into this field ("text copied between fields" device bug).
    // Heuristic guards at the wrapper layer missed it (the restore can
    // arrive late or in chunks), so this kills it at the source: from
    // activation until the native keyboard ECHOES this field's own text,
    // that text is authoritative — any foreign ingestion is undone and the
    // session buffer overwritten, every frame. Update runs before TMP's
    // LateUpdate ingestion, so the stomp always wins the frame.
    private bool syncingKeyboardText;
    private string keyboardSyncTarget;
    private float keyboardSyncStart;
    private bool wasFocusedLastFrame;
    private const float KeyboardSyncTimeoutSeconds = 1f;

    public override void OnDeselect(BaseEventData eventData)
    {
        dismissPending = true;
    }

    public override void OnSelect(BaseEventData eventData)
    {
        dismissPending = false;
        // Capture the sync target BEFORE activation opens/reuses the native
        // keyboard: stale-buffer ingestion can land in the SAME LateUpdate
        // that activates the field — the focused-edge fallback in Update
        // would then capture the already-corrupted text as the "truth".
        BeginKeyboardSync();
        base.OnSelect(eventData);
    }

    protected override void OnDisable()
    {
        if (dismissPending)
        {
            dismissPending = false;
            if (EventSystem.current != null)
                base.OnDeselect(new BaseEventData(EventSystem.current));
        }
        base.OnDisable();
    }

    private void Update()
    {
        SyncKeyboardTextOnActivation();

        if (!dismissPending) return;
        if (IsPointerPressed()) return;

        dismissPending = false;

        var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel != null && sel != gameObject && sel.GetComponent<TMP_InputField>() != null)
        {
            // Smooth-switch: another TMP_InputField has taken focus (and on
            // iOS is now driving the shared hidden UITextField). We must NOT
            // call base.OnDeselect — its DeactivateInputField path sets
            // m_SoftKeyboard.active = false, and Unity's iOS plugin
            // (KeyboardOnScreen singleton) resigns first-responder on that
            // shared text field, which dismisses the OS keyboard the newly
            // focused field is now using. Keeping the keyboard up is the
            // whole point of this branch.
            //
            // But we also can't leave the field as-is: m_AllowInput stays
            // true, the caret blink coroutine keeps running, and the
            // deselected field visibly blinks alongside the newly focused
            // one — two (or three) carets blink at once. SilentCaretStop
            // mimics the in-component bookkeeping side of DeactivateInputField
            // while skipping the OS-keyboard side.
            SilentCaretStop();
            return;
        }

        base.OnDeselect(new BaseEventData(EventSystem.current));
    }

    private void BeginKeyboardSync()
    {
        // The field's text at this moment is the truth the native session
        // must converge to. Captured once — comparing against live `text`
        // would chase our own corrections after a foreign ingestion.
        syncingKeyboardText = true;
        keyboardSyncTarget = text;
        keyboardSyncStart = Time.unscaledTime;
    }

    private void SyncKeyboardTextOnActivation()
    {
        var focusedNow = isFocused;
        // Fallback for programmatic activations that never route through
        // OnSelect. Skipped when OnSelect already captured (its capture is
        // earlier and therefore pristine).
        if (focusedNow && !wasFocusedLastFrame && !syncingKeyboardText)
            BeginKeyboardSync();
        wasFocusedLastFrame = focusedNow;

        if (!syncingKeyboardText) return;

        // The user cannot meaningfully type before the keyboard has even
        // synced, so holding the target text authoritative here is safe; the
        // timeout covers the Editor / hardware keyboards where m_SoftKeyboard
        // never appears.
        if (!focusedNow || Time.unscaledTime - keyboardSyncStart > KeyboardSyncTimeoutSeconds)
        {
            syncingKeyboardText = false;
            return;
        }
        if (m_SoftKeyboard == null) return; // not open yet — keep waiting

        if (m_SoftKeyboard.text == keyboardSyncTarget)
        {
            syncingKeyboardText = false; // native echoed our text — hand over
            return;
        }

        if (text != keyboardSyncTarget)
            text = keyboardSyncTarget;            // undo stale ingestion
        m_SoftKeyboard.text = keyboardSyncTarget; // overwrite the stale session buffer
    }

    private static bool IsPointerPressed()
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) return true;
        if (Pointer.current != null && Pointer.current.press.isPressed) return true;
        return false;
    }

    // Stops this field's caret rendering and blink without dismissing the OS
    // keyboard. Used by the smooth-switch branch where another input has
    // taken first responder on iOS's shared hidden UITextField.
    //
    // Step-by-step alignment with TMP_InputField.DeactivateInputField:
    //   1. Null m_SoftKeyboard (protected — accessible in subclass) so the
    //      base's `if (m_SoftKeyboard != null) { m_SoftKeyboard.active = false; ... }`
    //      branch is skipped. Without this, the OS keyboard dismisses.
    //   2. DeactivateInputField() sets m_AllowInput = false unconditionally,
    //      which terminates the caret blink coroutine (its loop checks
    //      m_AllowInput). It also sets m_SelectionStillActive = true on the
    //      way out.
    //   3. ReleaseSelection() clears m_SelectionStillActive — without this,
    //      OnFillVBO's guard `if (!isFocused && !m_SelectionStillActive)`
    //      keeps painting the caret quad at its last position on every
    //      canvas rebuild (same root cause as the ghost-caret bug that
    //      EditableField.ForceBlur addresses on sheet close). ReleaseSelection
    //      also fires SendOnEndEdit, which lets EditableField.HandleEndEdit →
    //      Blur sync wrapper state via the existing event path. Order
    //      matters: ReleaseSelection MUST run after DeactivateInputField,
    //      since DeactivateInputField re-sets m_SelectionStillActive=true.
    private void SilentCaretStop()
    {
        m_SoftKeyboard = null;
        DeactivateInputField();
        ReleaseSelection();
    }
}
