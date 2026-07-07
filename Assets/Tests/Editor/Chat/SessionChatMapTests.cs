using System.Collections.Generic;
using NUnit.Framework;

public class SessionChatMapTests
{
    [Test] public void ResolvesProfileToBotName()
    {
        var map = new Dictionary<string, string> { { "wa123", "Bot0" }, { "wa999", "Bot2" } };
        Assert.AreEqual("Bot2", SessionChatMap.ResolveBotName(map, "wa999"));
    }

    [Test] public void UnknownProfileReturnsNull()
        => Assert.IsNull(SessionChatMap.ResolveBotName(new Dictionary<string, string>(), "nope"));

    [Test] public void NullInputsSafe()
    {
        Assert.IsNull(SessionChatMap.ResolveBotName(null, "x"));
        Assert.IsNull(SessionChatMap.ResolveBotName(new Dictionary<string, string>(), null));
    }
}
