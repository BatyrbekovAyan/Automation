using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>One catalog row in the sheet: checkbox + the suggestion's full text.</summary>
    public class PromptSuggestionRowView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Image checkboxFill;
        // Same two-rotated-bars tick the chip uses — see PromptSuggestionChip.
        [SerializeField] private GameObject checkboxTick;
        [SerializeField] private Button button;

        private PromptSuggestion suggestion;
        private Action<PromptSuggestion> toggled;

        public PromptSuggestion Suggestion => suggestion;

        private void Awake()
        {
            if (button != null) button.onClick.AddListener(() => toggled?.Invoke(suggestion));
        }

        public void Bind(PromptSuggestion value, bool checkedNow, Action<PromptSuggestion> onToggled)
        {
            suggestion = value;
            toggled = onToggled;
            if (label != null) label.text = value.Text;
            SetChecked(checkedNow);
        }

        public void SetChecked(bool value)
        {
            if (checkboxFill != null) checkboxFill.enabled = value;
            if (checkboxTick != null) checkboxTick.SetActive(value);
        }
    }
}
