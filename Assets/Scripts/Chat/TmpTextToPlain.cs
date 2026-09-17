using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// S5 (audit stage 5): the model must see the message the client typed, not the interface's
/// markup. Chat text is stored render-ready (<see cref="UnicodeEmojiConverter"/> turns every
/// emoji into <c>&lt;sprite name="hex[-hex…]"&gt;</c> wrapped in zero-width spaces, with
/// <c>&lt;space=…&gt;</c> gaps), so the suggestions payload converts it back: sprite tags become
/// the emoji they stand for, layout tags disappear, invisible separators go, and everything
/// else — including a literal <c>&lt;</c> in a price comparison or a URL — is kept verbatim.
/// Pure; pinned by TmpTextToPlainTests.
/// </summary>
public static class TmpTextToPlain
{
    // TMP sprite tag as the converter emits it: <sprite name="1f44d-1f3fb"> (hex code points joined by '-').
    private static readonly Regex SpriteTag = new Regex("<sprite\\s+name=\"([0-9a-fA-F]+(?:-[0-9a-fA-F]+)*)\"\\s*/?>", RegexOptions.Compiled);
    // Layout-only tags the app itself inserts around sprites or links; never user content.
    private static readonly Regex LayoutTag = new Regex("</?(?:space|link|noparse|nobr|indent|line-height|voffset|cspace|mspace|pos|width|margin|align)(?:=[^>]*)?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Convert(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string s = SpriteTag.Replace(text, m => Decode(m.Groups[1].Value));
        s = LayoutTag.Replace(s, "");
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            // zero-width space / left-to-right & right-to-left marks / BOM: rendering aids, never content.
            if (c == '\u200B' || c == '\u200E' || c == '\u200F' || c == '\uFEFF') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>"1f44d-1f3fb" → 👍🏻. A part that is not a valid scalar keeps the tag text so nothing is invented.</summary>
    internal static string Decode(string hexParts)
    {
        var sb = new StringBuilder();
        foreach (string part in hexParts.Split('-'))
        {
            if (!int.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp)
                || cp < 0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
                return "<sprite name=\"" + hexParts + "\">";
            sb.Append(char.ConvertFromUtf32(cp));
        }
        return sb.ToString();
    }
}
