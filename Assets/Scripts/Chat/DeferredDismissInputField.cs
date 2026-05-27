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
            if (EventSystem.current != null)
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
