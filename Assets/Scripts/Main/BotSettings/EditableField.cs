using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Card-styled single-line input. Replaces the legacy Button-with-
    /// child-TMP + hidden TMP_InputField hack.
    ///
    /// On focus, requests FocusScrim to raise this RectTransform above a
    /// dim overlay. On blur (outside-tap, onEndEdit, keyboard-Done) fires
    /// OnCommitted only if the value changed since focus.
    /// </summary>
    public class EditableField : MonoBehaviour
    {
        [SerializeField] protected TextMeshProUGUI labelText;
        [SerializeField] protected TMP_InputField input;
        [SerializeField] protected FocusScrim scrim;

        [Serializable] public class StringEvent : UnityEvent<string> { }
        public StringEvent OnCommitted = new StringEvent();

        // Fires after Blur() runs (keyboard Done, outside-tap, programmatic blur).
        // The argument is the field that just blurred (this), so consumers can
        // distinguish a same-field re-focus from a real field-switch when they
        // observe focus state on a later frame. ItemEditSheet uses this to keep
        // the keyboard-dismissal bypass set even if input.isFocused reads
        // stale-true after DeactivateInputField on second-and-later focus cycles.
        public event Action<EditableField> Blurred;

        // Fires after HandleSelect runs (user tapped the field, programmatic
        // focus). ItemEditSheet listens for this to clear its keyboard-
        // dismissal bypass when the user genuinely re-focuses a field — using
        // a fresh event signal instead of polling input.isFocused, which on
        // iOS reads stale-true for one frame after DeactivateInputField.
        public event Action<EditableField> Selected;

        protected string focusValue;
        protected bool isFocused;

        public virtual string Value
        {
            get => input != null ? input.text : string.Empty;
            set { if (input != null) input.text = value ?? string.Empty; }
        }

        public string Label
        {
            get => labelText != null ? labelText.text : string.Empty;
            set { if (labelText != null) labelText.text = value ?? string.Empty; }
        }

        public bool IsFocused => isFocused;

        public TMP_InputField InputField => input;

        protected virtual void Awake()
        {
            if (input == null) return;
            input.onSelect.AddListener(HandleSelect);
            input.onEndEdit.AddListener(HandleEndEdit);
        }

        private void Update()
        {
            if (input == null) return;

            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            bool esSelectsUs = sel == input.gameObject;

            // Synthesize-Select: TMP_InputField on iOS doesn't reliably fire
            // onSelect when the SAME input is re-tapped after being deselected
            // (EventSystem activates the field and brings the keyboard back
            // up, but the onSelect UnityEvent never invokes). Without this,
            // our isFocused stays stale-false, Blur() later sees !isFocused
            // and early-returns without firing Blurred — the sheet misses the
            // dismissal signal and lags through the slow debounce path.
            //
            // The esSelectsUs guard prevents flip-flopping with the reconcile
            // branch below: after a smooth-switch, m_AllowInput (which backs
            // input.isFocused) stays true on the deselected field because
            // DeferredDismissInputField skipped base.OnDeselect → no
            // DeactivateInputField call. Without the guard, the reconcile
            // branch would clear isFocused and this branch would re-set it
            // every other frame.
            if (input.isFocused && !isFocused && esSelectsUs)
            {
                HandleSelect(null);
            }
            // Smooth-switch reconciliation: EventSystem has moved focus to
            // another TMP_InputField while we still believe we're focused.
            // DeferredDismissInputField intentionally skipped base.OnDeselect
            // on us so the OS keyboard stays up for the new field — but that
            // also skipped DeactivateInputField, so onEndEdit → HandleEndEdit
            // → Blur never fired and our isFocused is stale-true.
            //
            // Reconcile wrapper state with deactivateInput:false. Calling
            // input.DeactivateInputField() here would set m_Keyboard.active =
            // false and dismiss the OS keyboard — the exact behavior
            // DeferredDismissInputField exists to prevent.
            //
            // Gated to "sel is another TMP_InputField" because the outside-
            // tap case (sel is null or a non-input Selectable) is already
            // handled by DeferredDismissInputField.Update → base.OnDeselect
            // → DeactivateInputField → onEndEdit → HandleEndEdit → Blur on
            // pointer release. Timing note: EventSystem has
            // DefaultExecutionOrder = -1000, so B.OnSelect runs (and
            // ItemEditSheet.HandleFieldSelected sets lastSelectedFrame)
            // before any default-order Update, which lets the same-frame
            // guard in HandleFieldBlurred swallow the spurious Blurred we
            // emit here without setting dismissingKeyboard.
            else if (isFocused && !esSelectsUs
                     && sel != null
                     && sel.GetComponent<TMP_InputField>() != null)
            {
                Blur(commit: true, deactivateInput: false);
            }
        }

        protected virtual void OnDestroy()
        {
            if (input == null) return;
            input.onSelect.RemoveListener(HandleSelect);
            input.onEndEdit.RemoveListener(HandleEndEdit);
        }

        private void HandleSelect(string _)
        {
            if (isFocused) return;
            isFocused = true;
            focusValue = input.text;
            OnFocused();
            if (scrim != null)
                scrim.Show(GetComponent<RectTransform>(), () => Blur(commit: true));
            Selected?.Invoke(this);
        }

        private void HandleEndEdit(string _) => Blur(commit: true);

        public void Blur(bool commit) => Blur(commit, deactivateInput: true);

        // deactivateInput:false is for the smooth-switch reconcile path in
        // Update() — the wrapper bookkeeping needs to run but calling
        // input.DeactivateInputField() would dismiss the OS keyboard that
        // the newly focused field is now driving. All other callers go
        // through the public single-arg overload above and keep the original
        // behavior of deactivating the input as part of Blur.
        private void Blur(bool commit, bool deactivateInput)
        {
            if (!isFocused) return;
            isFocused = false;

            var current = input.text;
            if (commit && current != focusValue)
                OnCommitted.Invoke(current);

            if (deactivateInput) input.DeactivateInputField();
            if (scrim != null && scrim.IsShowing) scrim.Hide();
            OnBlurred();
            Blurred?.Invoke(this);
        }

        // Idempotent cleanup for container teardown / re-entry paths.
        //
        // The project's TMP_InputField prefab has `Reset On Deactivation`
        // turned off (m_ResetOnDeActivation = 0 in the prefab YAML). With
        // that flag off, DeactivateInputField sets m_SelectionStillActive
        // = true and intentionally does NOT call ReleaseSelection() to
        // clear it. OnFillVBO's guard is `if (!isFocused && !m_SelectionStillActive)
        // return empty;` — so as long as m_SelectionStillActive stays true,
        // every subsequent canvas rebuild RE-renders the caret quad at
        // its last position. Result: ghost caret painted over the next
        // card's text, no blink (the blink coroutine stops with focus),
        // no input capture (m_AllowInput is false).
        //
        // Calling ReleaseSelection() here clears m_SelectionStillActive
        // and schedules a rebuild via MarkGeometryAsDirty — the next
        // canvas update then outputs an empty caret mesh. Order matters:
        // it MUST run AFTER DeactivateInputField, which would otherwise
        // re-set the flag to true and undo this.
        public void ForceBlur()
        {
            if (input == null) return;

            if (input.isFocused)
                input.DeactivateInputField();

            input.ReleaseSelection();
        }

        protected virtual void OnFocused() { }
        protected virtual void OnBlurred() { }
    }
}
