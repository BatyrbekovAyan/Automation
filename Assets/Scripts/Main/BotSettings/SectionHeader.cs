using TMPro;
using UnityEngine;

namespace Automation.BotSettingsUI
{
    public class SectionHeader : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI labelText;
        public string Text { get => labelText.text; set => labelText.text = value; }
    }
}
