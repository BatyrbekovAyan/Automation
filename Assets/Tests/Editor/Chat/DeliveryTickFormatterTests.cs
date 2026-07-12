using NUnit.Framework;

public class DeliveryTickFormatterTests
{
    [Test]
    public void GetSprite_None_ReturnsNull()
    {
        Assert.IsNull(DeliveryTickFormatter.GetSprite(DeliveryStatus.None));
    }

    [Test]
    public void GetSprite_Pending_ReturnsClockTag()
    {
        Assert.AreEqual("<sprite name=\"tick_pending\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Pending));
    }

    [Test]
    public void GetSprite_Sent_ReturnsSingleTickTag()
    {
        Assert.AreEqual("<sprite name=\"tick_sent\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Sent));
    }

    [Test]
    public void GetSprite_Delivered_ReturnsDoubleTickTag()
    {
        Assert.AreEqual("<sprite name=\"tick_double\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Delivered));
    }

    [Test]
    public void GetSprite_Read_ReturnsBlueDoubleTickTag()
    {
        Assert.AreEqual("<sprite name=\"tick_double_blue\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Read));
    }

    [Test]
    public void GetSprite_Failed_ReturnsWarningEmojiThenFailedTag()
    {
        // ⚠️ warning emoji (fully-qualified "26a0-fe0f"), scaled down, in front of the tick_failed (refresh) glyph.
        Assert.AreEqual("<size=90%><sprite name=\"26a0-fe0f\"></size><sprite name=\"tick_failed\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Failed));
    }

    [TestCase("sent",      DeliveryStatus.Sent)]
    [TestCase("SENT",      DeliveryStatus.Sent)]
    [TestCase("Sent",      DeliveryStatus.Sent)]
    [TestCase("delivered", DeliveryStatus.Delivered)]
    [TestCase("read",      DeliveryStatus.Read)]
    [TestCase("  Sent  ", DeliveryStatus.Sent)]
    // Telegram (tapi) delivery states: pending → clock, undelivered/error → failed tick.
    [TestCase("pending",     DeliveryStatus.Pending)]
    [TestCase("PENDING",     DeliveryStatus.Pending)]
    [TestCase("undelivered", DeliveryStatus.Failed)]
    [TestCase("error",       DeliveryStatus.Failed)]
    [TestCase(" Error ",     DeliveryStatus.Failed)]
    public void ParseWappiString_KnownValue_ReturnsMatchingEnum(string raw, DeliveryStatus expected)
    {
        Assert.AreEqual(expected, DeliveryTickFormatter.ParseWappiString(raw));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("unknown_status_value")]
    [TestCase("   ")]
    public void ParseWappiString_UnknownOrEmpty_ReturnsNone(string raw)
    {
        Assert.AreEqual(DeliveryStatus.None, DeliveryTickFormatter.ParseWappiString(raw));
    }
}
