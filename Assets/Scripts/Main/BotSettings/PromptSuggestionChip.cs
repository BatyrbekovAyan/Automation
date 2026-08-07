using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// One suggestion pill. The glyph is an Image + sprite, never a TMP
    /// character — TMP-drawn icons do not render in this project.
    /// </summary>
    public class PromptSuggestionChip : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Image plusGlyph;
        // Two rotated bars, not a sprite: the project ships no monochrome tick
        // and a tinted green PNG cannot be re-tinted per theme role.
        [SerializeField] private GameObject tickGlyph;
        [SerializeField] private Image background;
        [SerializeField] private Image outline;
        [SerializeField] private Button button;

        private PromptSuggestion suggestion;
        private Action<PromptSuggestion> pressed;
        private bool added;

        public PromptSuggestion Suggestion => suggestion;

        private void Awake()
        {
            if (button != null) button.onClick.AddListener(HandlePressed);
        }

        // This component owns every colour that varies with the added state, so
        // these graphics carry NO ThemedColor binding — two owners would fight
        // and a theme switch would repaint an added chip back to Surface.
        private void OnEnable()
        {
            Theme.Changed += ApplyColors;
            ApplyColors();
        }

        private void OnDisable() => Theme.Changed -= ApplyColors;

        public void Bind(PromptSuggestion value, Action<PromptSuggestion> onPressed)
        {
            suggestion = value;
            pressed = onPressed;
            if (label != null) label.text = value.ShortLabel;
        }

        public void SetAdded(bool value)
        {
            added = value;
            ApplyColors();
        }

        private void ApplyColors()
        {
            var fill = added ? Theme.Color(ThemeRole.AccentSoft) : Theme.Color(ThemeRole.Surface);
            if (background != null) background.color = fill;

            // The ring is the Button's targetGraphic and the chip's only raycast
            // target — never disable it, or an added chip stops accepting the tap
            // that would remove it. It hides by matching the fill instead.
            if (outline != null) outline.color = added ? fill : Theme.Color(ThemeRole.Border);

            if (label != null)
                label.color = added
                    ? Theme.Color(ThemeRole.InkSecondary)
                    : Theme.Color(ThemeRole.InkPrimary);

            if (plusGlyph != null)
            {
                plusGlyph.enabled = !added;
                plusGlyph.color = Theme.Color(ThemeRole.AccentText);
            }

            if (tickGlyph == null) return;
            tickGlyph.SetActive(added);
            var tick = Theme.Color(ThemeRole.PositiveInk);
            foreach (var bar in tickGlyph.GetComponentsInChildren<Image>(true)) bar.color = tick;
        }

        private void HandlePressed() => pressed?.Invoke(suggestion);
    }
}
