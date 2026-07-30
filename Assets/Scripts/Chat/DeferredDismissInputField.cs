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
/// Two invariants added for the iOS shared-keyboard saga (cross-field text
/// duplication + stuck keyboard, both device-trace-proven):
///  • SINGLE FOCUS — every activation path (OnSelect AND TMP's direct
///    OnPointerClick path, which bypasses OnSelect) first silently releases
///    every other focused instance, so at most ONE field ever reads the one
///    shared native keyboard buffer. Activation is always immediate: a
///    deferred-activation scheme was tried and became the main source of
///    ownerless keyboards (its adopter could be superseded away).
///  • OWNED KEYBOARD — a smooth switch abandons the OS keyboard without
///    closing it (that is what keeps it up across switches); the reference
///    is parked, the next activation adopts it, and a watchdog (or
///    OnDisable) closes it when no adopter arrives, so the keyboard can
///    never be stranded on screen.
///
/// Explicit programmatic dismissals (DeactivateInputField from AttachSheet,
/// EditableField.Blur, ChatSearchBar, etc.) bypass OnDeselect entirely and
/// keep their immediate-dismiss semantics.
/// </summary>
[DefaultExecutionOrder(-50)]
public class DeferredDismissInputField : TMP_InputField
{
    private bool dismissPending;
    private float dismissPendingSince;
    // Rapid taps can leak a stuck "pressed" flag out of the Input System on
    // iOS; without a timeout that would block a pending dismissal forever
    // (keyboard visibly stuck, Back unable to close it).
    private const float DismissPointerTimeoutSeconds = 1.5f;

    // Every live instance, for the single-focus invariant and the watchdog.
    private static readonly System.Collections.Generic.List<DeferredDismissInputField> liveInputs =
        new System.Collections.Generic.List<DeferredDismissInputField>();

    // ── orphaned-keyboard bookkeeping ────────────────────────────────
    // SilentCaretStop abandons the OS keyboard on the assumption the newly
    // tapped field adopts it. If no adopter arrives (the tap burst ends
    // outside any field), nothing references the keyboard and nothing could
    // ever close it. Park on abandon, adopt on activation, close on timeout.
    private static TouchScreenKeyboard orphanedKeyboard;
    private static float orphanedAtTime;
    private static int orphanCheckFrame = -1;
    private const float OrphanGraceSeconds = 0.35f;

    // Temporary diagnostic for the stuck-keyboard hunt — keyboard lifecycle
    // events only (no per-frame spam). Strip once device-confirmed.
    private const bool TraceKeyboard = true;

    private static void Trace(string message)
    {
#pragma warning disable CS0162
        if (TraceKeyboard) Debug.Log($"[KB f{Time.frameCount}] {message}");
#pragma warning restore CS0162
    }

    private string Who() =>
        transform.parent != null ? $"{transform.parent.name}/{name}" : name;

    protected override void OnEnable()
    {
        base.OnEnable();
        liveInputs.Add(this);
    }

    protected override void OnDisable()
    {
        liveInputs.Remove(this);

        // Teardown with an orphan still parked: no Update will run to
        // watchdog it, so close it now.
        if (orphanedKeyboard != null)
        {
            Trace($"{Who()} OnDisable closes orphaned keyboard");
            orphanedKeyboard.active = false;
            orphanedKeyboard = null;
        }

        if (dismissPending)
        {
            dismissPending = false;
            if (EventSystem.current != null)
                base.OnDeselect(new BaseEventData(EventSystem.current));
        }
        base.OnDisable();
    }

    public override void OnDeselect(BaseEventData eventData)
    {
        dismissPending = true;
        dismissPendingSince = Time.unscaledTime;
    }

    // NOTE: activation must NOT clear the parked keyboard ("adopt") here.
    // Activation is a promise — the actual keyboard-open happens a LateUpdate
    // later, and a rapid next tap can cancel it before it ever opens. The
    // device trace showed exactly that: clearing on the promise discarded the
    // only handle to the still-visible keyboard, leaving it unkillable. The
    // watchdog performs the adoption instead, once a field's focus has
    // MATERIALIZED (see CloseOrphanIfAbandoned).
    public override void OnSelect(BaseEventData eventData)
    {
        dismissPending = false;
        ReleaseOtherFocusedInputs();
        Trace($"{Who()} activate (select)");
        base.OnSelect(eventData);
    }

    // TMP also activates directly from OnPointerClick, bypassing OnSelect —
    // the single-focus invariant must hold on this path too.
    public override void OnPointerClick(PointerEventData eventData)
    {
        ReleaseOtherFocusedInputs();
        base.OnPointerClick(eventData);
    }

    private void Update()
    {
        CloseOrphanIfAbandoned();

        if (!dismissPending) return;
        if (IsPointerPressed()
            && Time.unscaledTime - dismissPendingSince < DismissPointerTimeoutSeconds) return;

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
            // one. SilentCaretStop mimics the in-component bookkeeping side
            // of DeactivateInputField while skipping the OS-keyboard side.
            Trace($"{Who()} smooth-switch release");
            SilentCaretStop();
            return;
        }

        Trace($"{Who()} real dismiss");
        base.OnDeselect(new BaseEventData(EventSystem.current));
    }

    // Silences every other focused instance so at most one field reads the
    // shared native keyboard buffer (device trace: two simultaneously
    // focused fields cross-ingest each other's text).
    private void ReleaseOtherFocusedInputs()
    {
        foreach (var other in liveInputs)
        {
            if (other == null || other == this) continue;
            if (!other.isFocused && other.m_SoftKeyboard == null) continue;
            Trace($"{Who()} releases {other.Who()}");
            other.dismissPending = false;
            other.SilentCaretStop();
        }
    }

    private static void CloseOrphanIfAbandoned()
    {
        if (orphanedKeyboard == null) return;
        if (Time.frameCount == orphanCheckFrame) return;
        orphanCheckFrame = Time.frameCount;

        // Adoption happens HERE, not at activation: only once a field's focus
        // has MATERIALIZED does the (singleton) OS keyboard truly have an
        // owner whose own reference can dismiss it. Releasing the slot any
        // earlier — e.g. on the activation promise — discards the only
        // handle if that activation gets canceled by the next rapid tap.
        foreach (var field in liveInputs)
        {
            if (field != null && field.isFocused)
            {
                Trace($"orphan adopted ({field.Who()} focused)");
                orphanedKeyboard = null;
                return;
            }
        }

        if (Time.unscaledTime - orphanedAtTime < OrphanGraceSeconds) return;

        Trace("watchdog closes orphaned keyboard");
        orphanedKeyboard.active = false;
        orphanedKeyboard = null;
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
    //   1. Park + null m_SoftKeyboard (protected — accessible in subclass) so
    //      the base's `if (m_SoftKeyboard != null) { m_SoftKeyboard.active = false; ... }`
    //      branch is skipped. Without this, the OS keyboard dismisses. The
    //      parked reference lets the watchdog close the keyboard if no other
    //      field adopts it.
    //   2. DeactivateInputField() sets m_AllowInput = false unconditionally,
    //      which terminates the caret blink coroutine (its loop checks
    //      m_AllowInput). It also sets m_SelectionStillActive = true on the
    //      way out.
    //   3. ReleaseSelection() clears m_SelectionStillActive — without this,
    //      OnFillVBO's guard keeps painting the caret quad at its last
    //      position on every canvas rebuild (same root cause as the
    //      ghost-caret bug EditableField.ForceBlur addresses). It also fires
    //      SendOnEndEdit, which lets EditableField.HandleEndEdit → Blur sync
    //      wrapper state via the existing event path. Order matters:
    //      ReleaseSelection MUST run after DeactivateInputField, since
    //      DeactivateInputField re-sets m_SelectionStillActive = true.
    private void SilentCaretStop()
    {
        if (m_SoftKeyboard != null)
        {
            Trace($"{Who()} parks keyboard");
            orphanedKeyboard = m_SoftKeyboard;
            orphanedAtTime = Time.unscaledTime;
        }
        m_SoftKeyboard = null;
        DeactivateInputField();
        ReleaseSelection();
    }
}
