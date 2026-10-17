using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// One day of the bot's schedule (Part B «расписание бота»): a pill «Пн» … «Вс», ON when the bot answers by itself
    /// that day (within the window), OFF otherwise. Built by Tools/BotSettings/Add Schedule Section (BotScheduleBuilder).
    ///
    /// The chip OWNS its two-state colours through Theme.Color + Theme.Changed (the PromptSuggestionChip precedent), so
    /// its graphics carry NO ThemedColor — two owners would repaint a selected chip back to Surface on a theme switch.
    /// The ring is the Button's targetGraphic and the chip's only raycast target: it is RECOLOURED, never disabled — an
    /// off chip must still take the tap that turns its day back on.
    /// </summary>
    public class DayChip : MonoBehaviour
    {
        [SerializeField] private Image ring;
        [SerializeField] private Image face;
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Button button;
        /// <summary>0 = Пн … 6 = Вс — the ScheduleModel day bit.</summary>
        [SerializeField] private int day;

        private bool selected;

        /// <summary>The chip's day — raised on a tap; the owner of the mask decides what it means.</summary>
        public event Action<int> Pressed;

        public int Day => day;
        public bool Selected => selected;

        private void Awake()
        {
            if (button != null) button.onClick.AddListener(HandlePressed);
        }

        private void OnEnable()
        {
            Theme.Changed += ApplyColors;
            ApplyColors();
        }

        private void OnDisable() => Theme.Changed -= ApplyColors;

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandlePressed);
        }

        public void SetSelected(bool value)
        {
            selected = value;
            ApplyColors();
        }

        private void ApplyColors()
        {
            var fill = selected ? Theme.Color(ThemeRole.AccentFill) : Theme.Color(ThemeRole.Surface);
            if (face != null) face.color = fill;
            if (ring != null) ring.color = selected ? fill : Theme.Color(ThemeRole.Border);   // selected: hides by matching
            if (label != null)
                label.color = selected ? Theme.Color(ThemeRole.AccentOnFill) : Theme.Color(ThemeRole.InkSecondary);
        }

        private void HandlePressed() => Pressed?.Invoke(day);
    }
}
