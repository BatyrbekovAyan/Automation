using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Paints a catalog card's avatar square from the item's own name.
    ///
    /// Owns both colours outright and therefore carries NO <see cref="ThemedColor"/>
    /// on either graphic: ThemedColor is [DisallowMultipleComponent] and repaints
    /// on every Theme.Changed, so a second owner would flatten the monogram back
    /// to a plain Surface square (same reason PromptSuggestionChip and
    /// FieldWellFocusBorder paint themselves).
    ///
    /// Lives on the card rather than in the two card views because
    /// ProductCardView and ServiceCardView are line-for-line twins with no
    /// shared base type — one component beats a second copy of the logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemCardMonogram : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TextMeshProUGUI letter;

        private string key = string.Empty;

        /// <summary>Called from the card view's Name setter — the single entry
        /// point every write path (load, add, sheet commit) already goes through.</summary>
        public void Bind(string name)
        {
            key = name ?? string.Empty;
            if (letter != null) letter.text = MonogramPalette.LetterFor(key);
            Paint();
        }

        private void OnEnable()
        {
            Theme.Changed += Paint;
            Paint();
        }

        private void OnDisable()
        {
            Theme.Changed -= Paint;
        }

        private void Paint()
        {
            if (background != null)
                background.color = MonogramPalette.Background(key, Theme.Color(ThemeRole.Surface));
            if (letter != null)
                letter.color = MonogramPalette.Ink(key, Theme.Color(ThemeRole.InkPrimary));
        }
    }
}
