using NUnit.Framework;

/// <summary>S5 (audit stage 5): interface markup never reaches the model; user characters always do.</summary>
public class TmpTextToPlainTests
{
    [Test]
    public void SpriteTags_BecomeTheEmojiTheyStandFor()
    {
        string rendered = UnicodeEmojiConverter.ConvertRealEmojisToSprites("привет 👍🏻 как дела 😂");
        Assert.AreEqual("привет 👍🏻 как дела 😂", TmpTextToPlain.Convert(rendered));
    }

    [Test]
    public void ZwjSequences_AndKeycaps_RoundTrip()
    {
        foreach (string s in new[] { "👨‍👩‍👧 семья", "1️⃣ первый", "❤️ да", "🇰🇿 Астана" })
            Assert.AreEqual(s, TmpTextToPlain.Convert(UnicodeEmojiConverter.ConvertRealEmojisToSprites(s)), s);
    }

    [Test]
    public void LayoutTags_AndInvisibleSeparators_AreDropped()
    {
        Assert.AreEqual("цена 100", TmpTextToPlain.Convert("\u200Bцена<space=0.12em> 100\u200B"));
        Assert.AreEqual("см. site.kz/x", TmpTextToPlain.Convert("см. <link=\"https://site.kz/x\">site.kz/x</link>"));
        Assert.AreEqual("a\u200Db", TmpTextToPlain.Convert("a\u200Db"), "a ZERO-WIDTH JOINER is emoji content, never a separator");
    }

    [Test]
    public void LiteralAngleBrackets_UrlsAndUnicode_AreKept()
    {
        Assert.AreEqual("если a < b и b > c", TmpTextToPlain.Convert("если a < b и b > c"));
        Assert.AreEqual("<sprite=\"Other\" index=3>", TmpTextToPlain.Convert("<sprite=\"Other\" index=3>"), "an unknown tag shape is user text");
        Assert.AreEqual("https://kaspi.kz/shop?x=1&y=<2>", TmpTextToPlain.Convert("https://kaspi.kz/shop?x=1&y=<2>"));
        Assert.AreEqual("Сәлем! Бағасы қанша?", TmpTextToPlain.Convert("Сәлем! Бағасы қанша?"));
    }

    [Test]
    public void MalformedSpriteName_IsNotInvented()
    {
        Assert.AreEqual("<sprite name=\"zz\">", TmpTextToPlain.Convert("<sprite name=\"zz\">"));
        Assert.AreEqual("<sprite name=\"d800\">", TmpTextToPlain.Convert("<sprite name=\"d800\">"), "a lone surrogate is not a scalar");
        Assert.AreEqual("", TmpTextToPlain.Convert(null));
        Assert.AreEqual("", TmpTextToPlain.Convert(""));
    }
}
