using TMPro;
using UnityEngine;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Display-only variant of <see cref="EditableField"/> used for the
    /// WhatsApp / Telegram number cards on the Bot Settings General tab.
    ///
    /// Shows the phone number as a bold centered label inside a card that is
    /// itself a Button. Tapping opens the "really change number?" popup —
    /// there is no TMP_InputField and no keyboard focus path. Reads / writes
    /// go through <see cref="Value"/> so all existing BotSettings / Manager
    /// code that assigns <c>WhatsappNumberField.Value = "..."</c> keeps
    /// working.
    /// </summary>
    public class NumberDisplayField : EditableField
    {
        [SerializeField] private TextMeshProUGUI displayText;

        public override string Value
        {
            get => displayText != null ? displayText.text : string.Empty;
            set { if (displayText != null) displayText.text = value ?? string.Empty; }
        }
    }
}
