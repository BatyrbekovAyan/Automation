using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
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
            // TMP_InputField on iOS doesn't reliably fire onSelect when the
            // SAME input is re-tapped after being deselected (EventSystem
            // activates the field and brings the keyboard back up, but the
            // onSelect UnityEvent never invokes). Result: our isFocused flag
            // stays stale-false, Blur() later sees !isFocused and early-
            // returns without firing Blurred — the sheet misses the dismissal
            // signal and lags through the slow debounce path.
            //
            // Poll input.isFocused each frame. When it flips to true behind
            // our back, synthesize the select we didn't get. (input.isFocused
            // is an EventSystem.currentSelectedGameObject comparison — cheap.)
            if (input == null) return;
            if (input.isFocused && !isFocused)
            {
                Debug.Log($"[KB] {name}.Update SYNTHESIZED Select (input.isFocused true, isFocused false) f={Time.frameCount}");
                HandleSelect(null);
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
            if (isFocused)
            {
                Debug.Log($"[KB] {name}.HandleSelect SKIPPED (already isFocused=true) f={Time.frameCount}");
                return;
            }
            isFocused = true;
            focusValue = input.text;
            OnFocused();
            if (scrim != null)
                scrim.Show(GetComponent<RectTransform>(), () => Blur(commit: true));
            Debug.Log($"[KB] {name}.Selected firing f={Time.frameCount}");
            Selected?.Invoke(this);
        }

        private void HandleEndEdit(string _) => Blur(commit: true);

        public void Blur(bool commit)
        {
            if (!isFocused)
            {
                Debug.Log($"[KB] {name}.Blur SKIPPED (isFocused=false) f={Time.frameCount}");
                return;
            }
            isFocused = false;

            var current = input.text;
            if (commit && current != focusValue)
                OnCommitted.Invoke(current);

            input.DeactivateInputField();
            if (scrim != null && scrim.IsShowing) scrim.Hide();
            OnBlurred();
            Debug.Log($"[KB] {name}.Blurred firing f={Time.frameCount} inputIsFocused={input.isFocused}");
            Blurred?.Invoke(this);
        }

        /// <summary>Overridable hook for EditableTextArea to hide header etc.</summary>
        protected virtual void OnFocused() { }
        protected virtual void OnBlurred() { }
    }
}
