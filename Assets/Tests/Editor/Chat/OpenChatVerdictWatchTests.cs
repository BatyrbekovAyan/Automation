using NUnit.Framework;

/// <summary>
/// «Личные и рабочие чаты»: the open chat's verdict asks (<see cref="OpenChatVerdictWatch"/>). An incoming message in an open
/// person chat the app holds no verdict for asks for a list read at once; the server writes its decision some seconds later,
/// so the chat asks again 7 and 15 seconds after the message — at most three asks per message, the gaps kept after a pause —
/// and stops at once when the chat is decided, closed, replaced by another or another profile is active. The ChatManager
/// wiring is pinned in PersonalChatCueTests.
/// </summary>
public class OpenChatVerdictWatchTests
{
    private const string Profile = "watch-wa-profile";
    private const string Chat = "77000000040@c.us";

    private static bool Look(OpenChatVerdictWatch watch, float now, bool open = true, bool awaits = true,
                             string chat = Chat, string profile = Profile) =>
        watch.ShouldAsk(profile, chat, open, awaits, now);

    [Test]
    public void AsksAtTheMessage_ThenSevenAndFifteenSecondsLater_NeverAFourthTime()
    {
        CollectionAssert.AreEqual(new[] { 0f, 7f, 15f }, OpenChatVerdictWatch.AskAtSeconds);
        Assert.AreEqual(3, OpenChatVerdictWatch.MaxAsks);

        var watch = new OpenChatVerdictWatch();
        watch.Arm(Profile, Chat, 100f);
        Assert.IsTrue(Look(watch, 100f), "the message's own ask");
        Assert.IsFalse(Look(watch, 101f));
        Assert.IsFalse(Look(watch, 106.9f), "nothing before seven seconds");
        Assert.IsTrue(Look(watch, 107f), "still undecided seven seconds after the message: ask again");
        Assert.IsFalse(Look(watch, 108f));
        Assert.IsFalse(Look(watch, 114.9f));
        Assert.IsTrue(Look(watch, 115f), "and fifteen seconds after it");
        Assert.IsFalse(watch.IsArmed, "every ask made: the watch stops");
        Assert.IsFalse(Look(watch, 130f), "never a fourth ask for one message");
    }

    [Test]
    public void AfterAPause_TheAsksKeepTheirGaps_NoBurst()
    {
        var watch = new OpenChatVerdictWatch();
        watch.Arm(Profile, Chat, 0f);
        Assert.IsTrue(Look(watch, 60f), "the first look after a pause asks");
        Assert.IsFalse(Look(watch, 61f), "the next ask keeps its gap from the one just made");
        Assert.IsTrue(Look(watch, 67f));
        Assert.IsFalse(Look(watch, 74f));
        Assert.IsTrue(Look(watch, 75f));
        Assert.IsFalse(watch.IsArmed);
    }

    [Test]
    public void ANewIncomingMessage_StartsTheScheduleOver()
    {
        var watch = new OpenChatVerdictWatch();
        watch.Arm(Profile, Chat, 0f);
        Assert.IsTrue(Look(watch, 0f));
        Assert.IsTrue(Look(watch, 7f));
        watch.Arm(Profile, Chat, 10f);
        Assert.IsTrue(Look(watch, 10f), "the new message's own ask");
        Assert.IsFalse(Look(watch, 15f), "the old schedule is gone");
        Assert.IsTrue(Look(watch, 17f));
        Assert.IsTrue(Look(watch, 25f));
        Assert.IsFalse(Look(watch, 40f));
    }

    [TestCase("decided", TestName = "Stops_WhenTheChatIsDecided")]
    [TestCase("closed", TestName = "Stops_WhenTheChatCloses")]
    [TestCase("another chat", TestName = "Stops_WhenAnotherChatOpens")]
    [TestCase("another profile", TestName = "Stops_WhenTheBotOrChannelSwitches")]
    public void StopsAtOnce_AndStaysStopped(string what)
    {
        var watch = new OpenChatVerdictWatch();
        watch.Arm(Profile, Chat, 0f);
        Assert.IsTrue(Look(watch, 0f));
        bool asked = what switch
        {
            "decided" => Look(watch, 7f, awaits: false),
            "closed" => Look(watch, 7f, open: false),
            "another chat" => Look(watch, 7f, chat: "77000000041@c.us"),
            _ => Look(watch, 7f, profile: "watch-tg-profile"),
        };
        Assert.IsFalse(asked, $"{what}: no ask");
        Assert.IsFalse(watch.IsArmed, $"{what}: the watch stops");
        Assert.IsFalse(Look(watch, 15f), $"{what}: nothing more is asked, even with the chat back as it was");
    }

    [Test]
    public void NoProfileOrNoChat_ArmsNothing()
    {
        var watch = new OpenChatVerdictWatch();
        watch.Arm(null, Chat, 0f);
        Assert.IsFalse(watch.IsArmed);
        watch.Arm(Profile, "", 0f);
        Assert.IsFalse(watch.IsArmed);
        Assert.IsFalse(watch.ShouldAsk(null, "", true, true, 0f));
    }
}
