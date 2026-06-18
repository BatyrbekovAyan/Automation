using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;

public class WrappableLinkFormatterTests
{
    // A monster Google-image-search URL like the one that triggered the bug report —
    // ~500 chars, far past TMP's 128-char rich-text tag buffer.
    private const string LongUrl =
        "https://www.google.com/search?sca_esv=864165414b9a37ad&rlz=1CDGOYI_enKZ848KZ848" +
        "&hl=en-US&sxsrf=AE3TifNAujKk1NGfJxwdpbfSJSG4Ivtd3w:1749990436416&udm=2&fbs=AIIj" +
        "pHxU7SXXniUZfeShr2fp4giZ1Y6MJ25_tmWlTc7uy4KIeoJTKjrFjVxydQWqI2NcOhZufN78Vg3E82Q" +
        "OGfwkmnQG99vEJ38VY9pb4_NEIS3gdddJHS&q=%D0%BA%D0%BE%D0%BC%D0%BF%D1%83%D1%82%D0%B5" +
        "%D1%80&sa=X&ved=2ahUKEwjVvMqytvONAxWEU1UIHfylKncQtKgLegQIERAB&biw=428&bih=751&dpr=3#imgrc=PeHpw7N1AH1mkM";

    private static IEnumerable<string> RichTextTags(string s)
    {
        foreach (Match m in Regex.Matches(s, "<[^>]*>"))
            yield return m.Value;
    }

    // ── The regression: every generated tag must fit TMP's parser buffer ──

    [Test]
    public void Format_LongUrl_EveryGeneratedTag_FitsTmpBuffer()
    {
        var map = new Dictionary<string, string>();
        string formatted = WrappableLinkFormatter.Format(LongUrl, map);

        foreach (string tag in RichTextTags(formatted))
        {
            // inner content is between '<' and '>' — that's what TMP reads into m_htmlTag[128].
            int inner = tag.Length - 2;
            Assert.Less(inner, WrappableLinkFormatter.TmpRichTextTagBuffer,
                $"Tag overflows TMP buffer and will render as literal text: {tag}");
        }
    }

    [Test]
    public void Format_LongUrl_UsesShortNumericId_NotTheUrl()
    {
        var map = new Dictionary<string, string>();
        string formatted = WrappableLinkFormatter.Format(LongUrl, map);

        StringAssert.Contains("<link=\"0\">", formatted);
        Assert.IsFalse(formatted.Contains("<link=\"http"),
            "URL must not be embedded in the <link> attribute.");
    }

    // ── The map carries the real URL for click resolution ──

    [Test]
    public void Format_RecordsRawUrl_InMap()
    {
        var map = new Dictionary<string, string>();
        WrappableLinkFormatter.Format(LongUrl, map);

        Assert.AreEqual(1, map.Count);
        Assert.AreEqual(LongUrl, map["0"], "Map must hold the exact raw URL (no zero-width spaces).");
    }

    [Test]
    public void Format_MultipleUrls_GetDistinctIds()
    {
        var map = new Dictionary<string, string>();
        string formatted = WrappableLinkFormatter.Format(
            "see https://a.com/x and https://b.com/y here", map);

        Assert.AreEqual(2, map.Count);
        Assert.AreEqual("https://a.com/x", map["0"]);
        Assert.AreEqual("https://b.com/y", map["1"]);
        StringAssert.Contains("<link=\"0\">", formatted);
        StringAssert.Contains("<link=\"1\">", formatted);
    }

    // ── Display stays WhatsApp-faithful: the full URL is the visible text ──

    [Test]
    public void Format_DisplayText_PreservesFullUrl()
    {
        var map = new Dictionary<string, string>();
        string formatted = WrappableLinkFormatter.Format(LongUrl, map);

        // strip tags and zero-width spaces → what the user actually reads.
        string visible = Regex.Replace(formatted, "<[^>]*>", "").Replace("\u200B", "");
        Assert.AreEqual(LongUrl, visible);
    }

    [Test]
    public void Format_KeepsSurroundingText()
    {
        var map = new Dictionary<string, string>();
        string formatted = WrappableLinkFormatter.Format("hi https://x.com bye", map);

        StringAssert.StartsWith("hi ", formatted);
        StringAssert.EndsWith(" bye", formatted);
    }

    // ── Edge cases ──

    [Test]
    public void Format_PlainText_ReturnsUnchanged_AndEmptyMap()
    {
        var map = new Dictionary<string, string>();
        string formatted = WrappableLinkFormatter.Format("no links here", map);

        Assert.AreEqual("no links here", formatted);
        Assert.AreEqual(0, map.Count);
    }

    [TestCase(null)]
    [TestCase("")]
    public void Format_NullOrEmpty_ReturnsInput_AndClearsMap(string input)
    {
        var map = new Dictionary<string, string> { { "stale", "x" } };
        string formatted = WrappableLinkFormatter.Format(input, map);

        Assert.AreEqual(input, formatted);
        Assert.AreEqual(0, map.Count, "Map must be cleared even for null/empty input.");
    }

    [Test]
    public void Format_ClearsStaleEntries_OnReuse()
    {
        var map = new Dictionary<string, string>();
        WrappableLinkFormatter.Format("https://first.com", map);
        WrappableLinkFormatter.Format("https://second.com", map);

        Assert.AreEqual(1, map.Count);
        Assert.AreEqual("https://second.com", map["0"]);
    }
}
