using NUnit.Framework;

public class WappiRecipientTests
{
    [Test] public void StripsCUsSuffixForOneToOne()
        => Assert.AreEqual("79995579399", WappiRecipient.FromChatId("79995579399@c.us"));

    [Test] public void PreservesGroupId()
        => Assert.AreEqual("120363012345@g.us", WappiRecipient.FromChatId("120363012345@g.us"));

    [Test] public void PassesThroughBareId()
        => Assert.AreEqual("79995579399", WappiRecipient.FromChatId("79995579399"));

    [Test] public void NullAndEmptyAreSafe()
    {
        Assert.IsNull(WappiRecipient.FromChatId(null));
        Assert.AreEqual("", WappiRecipient.FromChatId(""));
    }
}
