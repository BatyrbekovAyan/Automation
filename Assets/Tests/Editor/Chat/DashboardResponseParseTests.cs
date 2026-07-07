using NUnit.Framework;

public class DashboardResponseParseTests
{
    [Test] public void ParsesOutcomesAndFlags()
    {
        string json = "{\"success\":true,\"classified\":2,\"truncated\":true,\"outcomes\":[" +
            "{\"profileId\":\"p1\",\"chatId\":\"7701@c.us\",\"outcome\":\"order_collected\"," +
            "\"summary\":\"101 роза\",\"outcomeAt\":1700000000000,\"lastMessageAt\":1700000005000}]}";
        var r = DashboardResponse.Parse(json);
        Assert.IsTrue(r.success);
        Assert.IsTrue(r.truncated);
        Assert.AreEqual(1, r.outcomes.Count);
        Assert.AreEqual(OutcomeStatus.OrderCollected, r.outcomes[0].Status);
        Assert.AreEqual("7701@c.us", r.outcomes[0].chatId);
        Assert.AreEqual(1700000005000L, r.outcomes[0].lastMessageAt);
    }

    [Test] public void UnknownOutcomeIdMapsToUnknown()
        => Assert.AreEqual(OutcomeStatus.Unknown, OutcomeStatusMap.FromId("nonsense"));

    [Test] public void NullOrGarbageJsonIsSafe()
    {
        Assert.IsNull(DashboardResponse.Parse(null));
        Assert.IsNull(DashboardResponse.Parse("not json"));
    }
}
