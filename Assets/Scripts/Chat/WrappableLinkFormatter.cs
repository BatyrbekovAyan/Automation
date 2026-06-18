using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Turns raw URLs inside a message body into TextMeshPro &lt;link&gt; tags that wrap
/// nicely and stay clickable.
///
/// CRITICAL: the URL is NOT embedded in the tag attribute. TMP's rich-text parser
/// has a fixed 128-char buffer per tag (TMP_Text.m_htmlTag = new char[128]); a long
/// URL inside &lt;link="https://..."&gt; overflows it, TMP gives up parsing, and the raw
/// "&lt;link=...&gt;" markup leaks into the bubble as visible text. Instead we emit a tiny
/// numeric id (&lt;link="0"&gt;) and record id -&gt; url in a caller-owned map, so the tag is
/// always ~10 chars regardless of URL length. The full URL still renders as the
/// (wrappable) visible text, WhatsApp-style.
/// </summary>
public static class WrappableLinkFormatter
{
    // TMP's per-tag rich-text buffer. Generated <link> tags must stay well under this.
    public const int TmpRichTextTagBuffer = 128;

    // Zero-width space — lets TMP break a long URL after common symbols.
    private const string Zws = "\u200B";

    // WhatsApp's classic dark teal — reads as "green" against the light bubble bg.
    private const string LinkColor = "#075E54";

    private static readonly Regex UrlRegex =
        new Regex(@"(https?://[^\s]+)", RegexOptions.Compiled);

    /// <summary>
    /// Replaces every URL in <paramref name="text"/> with a TMP link tag carrying a
    /// short numeric id, and populates <paramref name="linkUrlsById"/> with id -> raw
    /// URL so a click handler can resolve the tapped id back to the real URL.
    /// </summary>
    public static string Format(string text, IDictionary<string, string> linkUrlsById)
    {
        linkUrlsById?.Clear();
        if (string.IsNullOrEmpty(text)) return text;

        int counter = 0;
        return UrlRegex.Replace(text, match =>
        {
            string rawUrl = match.Groups[1].Value;
            string id = counter.ToString();
            counter++;

            if (linkUrlsById != null) linkUrlsById[id] = rawUrl;

            string displayUrl = InsertWrapPoints(rawUrl);
            return $"<link=\"{id}\"><color={LinkColor}><u>{displayUrl}</u></color></link>";
        });
    }

    // Insert zero-width spaces after common URL symbols so TMP can break a long URL
    // across lines instead of overflowing the bubble on one unbroken run.
    private static string InsertWrapPoints(string url) =>
        url.Replace("/", "/" + Zws)
           .Replace("?", "?" + Zws)
           .Replace("=", "=" + Zws)
           .Replace("&", "&" + Zws)
           .Replace("-", "-" + Zws)
           .Replace("_", "_" + Zws);
}
