using System.Globalization;
using UnityEngine;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Deterministic avatar colour + letter for a catalog item, so two rows in
    /// the products list are told apart at a glance. Replaces the grey
    /// placeholder square, which carried no information at all — every row got
    /// the same glyph.
    ///
    /// The six hues are the sketch-007 «B2» set and are deliberately NOT theme
    /// roles: they are identity colours for data, like the WhatsApp green, and
    /// must stay put when the palette flips. They are equally deliberately not
    /// in <see cref="Theme.Fixed"/>, which is pinned byte-for-byte by
    /// ThemeFoundationTests.
    ///
    /// Both colours are mixed against the live theme so the square stays
    /// legible in light and dark: the ink ratio is 0.65 rather than the
    /// mockup's 0.72 because 0.72 drops the violet hue to 4.11:1 on the dark
    /// surface, under the 4.5 floor. At 0.65 the worst of the twelve
    /// hue×theme combinations measures 4.65:1.
    /// </summary>
    public static class MonogramPalette
    {
        public static readonly Color[] Hues =
        {
            new Color32(0xE4, 0x57, 0x2E, 0xFF),
            new Color32(0x2E, 0x86, 0xAB, 0xFF),
            new Color32(0x7E, 0x52, 0xA0, 0xFF),
            new Color32(0x1B, 0x99, 0x8B, 0xFF),
            new Color32(0xC1, 0x66, 0x6B, 0xFF),
            new Color32(0x6A, 0x85, 0x32, 0xFF),
        };

        private const float BackgroundMix = 0.22f;
        private const float InkMix = 0.65f;
        private const string EmptyLetter = "•";

        /// <summary>
        /// Stable hue index for a name. A plain character sum would give
        /// anagrams and reorderings the same colour, so the hash is
        /// position-sensitive; unchecked because wrapping is the point.
        /// </summary>
        public static int IndexFor(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return 0;

            unchecked
            {
                uint hash = 0;
                foreach (var character in key) hash = hash * 31u + character;
                return (int)(hash % (uint)Hues.Length);
            }
        }

        /// <summary>
        /// First visible character, uppercased. Enumerated as a TEXT ELEMENT,
        /// not as a char: an emoji or any astral glyph is a surrogate pair, and
        /// key[0] would render half of it.
        /// </summary>
        public static string LetterFor(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return EmptyLetter;

            var enumerator = StringInfo.GetTextElementEnumerator(name.Trim());
            return enumerator.MoveNext()
                ? ((string)enumerator.Current).ToUpperInvariant()
                : EmptyLetter;
        }

        public static Color Background(string key, Color surface) =>
            Color.Lerp(surface, Hues[IndexFor(key)], BackgroundMix);

        public static Color Ink(string key, Color inkPrimary) =>
            Color.Lerp(inkPrimary, Hues[IndexFor(key)], InkMix);
    }
}
