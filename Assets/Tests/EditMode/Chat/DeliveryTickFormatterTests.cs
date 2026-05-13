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
    public void GetSprite_Failed_ReturnsFailedTag()
    {
        Assert.AreEqual("<sprite name=\"tick_failed\">", DeliveryTickFormatter.GetSprite(DeliveryStatus.Failed));
    }

    [TestCase("sent",      DeliveryStatus.Sent)]
    [TestCase("SENT",      DeliveryStatus.Sent)]
    [TestCase("Sent",      DeliveryStatus.Sent)]
    [TestCase("delivered", DeliveryStatus.Delivered)]
    [TestCase("read",      DeliveryStatus.Read)]
    public void ParseWappiString_KnownValue_ReturnsMatchingEnum(string raw, DeliveryStatus expected)
    {
        Assert.AreEqual(expected, DeliveryTickFormatter.ParseWappiString(raw));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("unknown_status_value")]
    public void ParseWappiString_UnknownOrEmpty_ReturnsNone(string raw)
    {
        Assert.AreEqual(DeliveryStatus.None, DeliveryTickFormatter.ParseWappiString(raw));
    }
}
