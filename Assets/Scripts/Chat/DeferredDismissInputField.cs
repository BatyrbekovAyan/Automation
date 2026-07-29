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

    // ── serialized keyboard activations ──────────────────────────────
    // Two TMP activations close together put two async TouchScreenKeyboard
    // text-sets in flight on Android's single native IME session; whichever
    // lands LAST is then polled back by the OTHER field's ingestion, so text
    // jumps between the two rapidly tapped fields (device repro: "double-tap
    // but on two different fields" — either direction, any starting field).
    // Spacing activations out keeps at most ONE native set in flight, and a
    // tap burst activates only the last-selected field.
    private static float lastActivationTime = float.NegativeInfinity;
    private const float ActivationSpacingSeconds = 0.25f;
    private Coroutine pendingActivation;

    // Every live instance, for the single-focus invariant below.
    private static readonly System.Collections.Generic.List<DeferredDismissInputField> liveInputs =
        new System.Collections.Generic.List<DeferredDismissInputField>();

    protected override void OnEnable()
    {
        base.OnEnable();
        liveInputs.Add(this);
    }

    // Device trace f3678 (iOS): with two fields TMP-focused at once — the old
    // one's deferred release lags a couple of frames behind the new one's
    // activation — BOTH poll the ONE shared native keyboard buffer, and the
    // stale field ingests the fresh field's text (the cross-field copy).
    // Before this field activates by ANY path, force every other instance to
    // silently stop (SilentCaretStop: no keyboard dismiss, m_SoftKeyboard
    // nulled so it stops polling). At most one field ever reads the buffer.
    private void ReleaseOtherFocusedInputs()
    {
        foreach (var other in liveInputs)
        {
            if (other == null || other == this) continue;
            if (!other.isFocused && other.m_SoftKeyboard == null) continue;
            KbTrace.Log($"{Who()} RELEASE-OTHER {other.Who()} (focused={other.isFocused})");
            other.dismissPending = false;
            other.SilentCaretStop();
        }
    }

    // TMP activates directly from OnPointerClick — a path the OnSelect gate
    // never sees (device trace: FOCUS=True while the activation was still
    // deferred). Route it through the same rules: while a deferred
    // activation is pending, the click must not activate; otherwise enforce
    // the single-focus invariant first.
    public override void OnPointerClick(PointerEventData eventData)
    {
        if (pendingActivation != null)
        {
            KbTrace.Log($"{Who()} POINTER-CLICK swallowed (activation deferred)");
            return;
        }
        ReleaseOtherFocusedInputs();
        base.OnPointerClick(eventData);
    }

    // ── [KB] diagnostic state (see KbTrace) ──────────────────────────
    private string traceLastText;
    private string traceLastKbText;
    private bool traceLastFocus;

    private string Who() =>
        transform.parent != null ? $"{transform.parent.name}/{name}" : name;

    public override void OnDeselect(BaseEventData eventData)
    {
        KbTrace.Log($"{Who()} DESELECT (dismissPending) sel={KbTrace.Sel()}");
        dismissPending = true;
    }

    public override void OnSelect(BaseEventData eventData)
    {
        dismissPending = false;

        var wait = ActivationSpacingSeconds - (Time.unscaledTime - lastActivationTime);
        if (wait <= 0f)
        {
            KbTrace.Log($"{Who()} SELECT->activate text='{KbTrace.T(text)}'");
            lastActivationTime = Time.unscaledTime;
            ReleaseOtherFocusedInputs();
            base.OnSelect(eventData);
            return;
        }

        KbTrace.Log($"{Who()} SELECT->deferred {wait:F3}s text='{KbTrace.T(text)}'");
        if (pendingActivation != null) StopCoroutine(pendingActivation);
        pendingActivation = StartCoroutine(ActivateAfterSpacing(wait));
    }

    private System.Collections.IEnumerator ActivateAfterSpacing(float wait)
    {
        yield return new WaitForSecondsRealtime(wait);
        pendingActivation = null;

        // Superseded: the user has tapped yet another field during the wait —
        // only the final selection of the burst may open a keyboard session.
        var eventSystem = EventSystem.current;
        if (eventSystem == null || eventSystem.currentSelectedGameObject != gameObject)
        {
            KbTrace.Log($"{Who()} deferred-activation SUPERSEDED sel={KbTrace.Sel()}");
            yield break;
        }

        KbTrace.Log($"{Who()} deferred-activation FIRE text='{KbTrace.T(text)}'");
        lastActivationTime = Time.unscaledTime;
        ReleaseOtherFocusedInputs();
        base.OnSelect(new BaseEventData(eventSystem));
    }

    protected override void OnDisable()
    {
        liveInputs.Remove(this);
        if (pendingActivation != null)
        {
            StopCoroutine(pendingActivation);
            pendingActivation = null;
        }
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
        if (KbTrace.Enabled) TraceState();

        if (!dismissPending) return;
        if (IsPointerPressed()) return;

        dismissPending = false;

        var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel != null && sel != gameObject && sel.GetComponent<TMP_InputField>() != null)
        {
            KbTrace.Log($"{Who()} SMOOTH-SWITCH SilentCaretStop -> sel={KbTrace.Sel()}");
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

        KbTrace.Log($"{Who()} REAL-DISMISS base.OnDeselect sel={KbTrace.Sel()}");
        base.OnDeselect(new BaseEventData(EventSystem.current));
    }

    // Logs every mutation of this field's text, its native keyboard-session
    // buffer, and its TMP focus flag — delta-based, so quiet frames log
    // nothing. The KBBUF lines are the smoking gun for the shared-session
    // replay: they show which wrapper's buffer changes to what, and when,
    // relative to TEXT ingestions.
    private void TraceState()
    {
        if (isFocused != traceLastFocus)
        {
            KbTrace.Log($"{Who()} FOCUS={isFocused} text='{KbTrace.T(text)}' sel={KbTrace.Sel()}");
            traceLastFocus = isFocused;
        }

        if (text != traceLastText)
        {
            KbTrace.Log($"{Who()} TEXT '{KbTrace.T(traceLastText)}' -> '{KbTrace.T(text)}' " +
                        $"focused={isFocused} sel={KbTrace.Sel()}");
            traceLastText = text;
        }

        var kbText = m_SoftKeyboard != null ? m_SoftKeyboard.text : null;
        if (kbText != traceLastKbText)
        {
            var active = m_SoftKeyboard != null && m_SoftKeyboard.active;
            KbTrace.Log($"{Who()} KBBUF '{KbTrace.T(traceLastKbText)}' -> '{KbTrace.T(kbText)}' " +
                        $"kbActive={active} focused={isFocused}");
            traceLastKbText = kbText;
        }
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
