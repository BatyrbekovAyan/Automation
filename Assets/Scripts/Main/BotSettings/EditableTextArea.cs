using UnityEngine;
using UnityEngine.Events;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Multi-line variant of EditableField used for Business description
    /// and Prompt fields. Raises OnFullScreenFocusRequested so BotSettings
    /// can hide the header + tab bar (matching BotSettings.cs:743-747).
    /// </summary>
    public class EditableTextArea : EditableField
    {
        public UnityEvent OnFullScreenFocusRequested = new UnityEvent();
        public UnityEvent OnFullScreenFocusReleased = new UnityEvent();

        protected override void OnFocused() => OnFullScreenFocusRequested.Invoke();
        protected override void OnBlurred() => OnFullScreenFocusReleased.Invoke();
    }
}
