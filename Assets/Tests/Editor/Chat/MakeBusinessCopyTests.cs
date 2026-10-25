using NUnit.Framework;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.5, §4.5): the «Сделать чат рабочим?» sheet says what the assistant will do once the chat
/// is working. With «Авто» on and the client's message waiting it answers that message — but only while it is fresh
/// (<see cref="ChatAudienceRule.FreshHours"/>), so the small note under the text explains that an older one is skipped.
/// With «Авто» on and the owner's message last it simply answers what comes next; with «Авто» off it only suggests.
/// </summary>
public class MakeBusinessCopyTests
{
    [Test]
    public void For_AutoFresh()
    {
        (string body, string note) = MakeBusinessCopy.For(autoOn: true, lastIsClient: true, lastAgeHours: 2);
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoFresh, body, "the waiting message is answered right away");
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessStaleNote, note, "…and the note says why an older one would not be");
    }

    [Test]
    public void For_AutoStaleHasTheNote()
    {
        (string body, string note) = MakeBusinessCopy.For(autoOn: true, lastIsClient: true, lastAgeHours: 30);
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh, body, "a message older than a day is not answered — new ones are");
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessStaleNote, note, "the note explains the waiting message is skipped");
    }

    [Test]
    public void For_AutoOwnerLastNoNote()
    {
        (string body, string note) = MakeBusinessCopy.For(autoOn: true, lastIsClient: false, lastAgeHours: 1);
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh, body, "nobody waits for an answer — the assistant takes the next message");
        Assert.IsNull(note, "nothing is skipped, so there is nothing to explain");
    }

    [Test]
    public void For_Manual()
    {
        foreach (bool lastIsClient in new[] { true, false })
        foreach (double age in new[] { 0.5, 30.0 })
        {
            (string body, string note) = MakeBusinessCopy.For(autoOn: false, lastIsClient, age);
            Assert.AreEqual(PersonalChatsCopy.MakeBusinessManual, body, "«Авто» is off: the assistant suggests, the owner sends");
            Assert.IsNull(note, "the owner answers himself — no message is answered or skipped");
        }
    }

    [Test]
    public void For_TheLineBetweenFreshAndStale_IsFreshHours()
    {
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoFresh,
            MakeBusinessCopy.For(true, true, ChatAudienceRule.FreshHours).Body, "a message exactly a day old is still answered");
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh,
            MakeBusinessCopy.For(true, true, ChatAudienceRule.FreshHours + 0.01).Body, "a moment older is not");
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh,
            MakeBusinessCopy.For(true, true, double.NaN).Body, "an age nobody could compute is never fresh");
    }

    [Test]
    public void AgeHours_FromTheLastMessage_AndAnUnknownTimeIsNeverFresh()
    {
        const long now = 1_800_000_000;
        Assert.AreEqual(2.0, MakeBusinessCopy.AgeHours(now - 2 * 3600, now), 1e-9);
        Assert.AreEqual(0.5, MakeBusinessCopy.AgeHours(now - 1800, now), 1e-9);
        Assert.AreEqual(double.PositiveInfinity, MakeBusinessCopy.AgeHours(0, now), "the list gave no time for the last message");
        Assert.AreEqual(double.PositiveInfinity, MakeBusinessCopy.AgeHours(-5, now));
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh,
            MakeBusinessCopy.For(true, true, MakeBusinessCopy.AgeHours(0, now)).Body, "no time → no promise of an answer");
    }
}
