using NUnit.Framework;

/// <summary>
/// A tap that catches a flying list stops it — it is not a tap on a row (<see cref="ScrollClickBlocker.IsBlocking"/>, read by
/// every chat row). Since «Личные и рабочие чаты» two lists can be on screen at once — the «Личные» folder over the chat list —
/// and both blockers judge the same press in an order Unity does not fix: the first to judge a press starts from «not
/// blocking», and any list that was flying makes it «blocking», so a still list can never clear the catch of a flying one.
/// </summary>
public class ScrollClickBlockerTests
{
    [Test]
    public void OneList_ItsOwnFlightDecides()
    {
        Assert.IsTrue(ScrollClickBlocker.Verdict(soFar: false, firstToJudge: true, flying: true), "caught a flying list");
        Assert.IsFalse(ScrollClickBlocker.Verdict(soFar: true, firstToJudge: true, flying: false),
            "a new press starts afresh — the previous press's catch does not carry over");
    }

    [Test]
    public void TwoLists_AFlyingOneBlocks_WhicheverJudgesFirst()
    {
        bool flyingFirst = ScrollClickBlocker.Verdict(ScrollClickBlocker.Verdict(false, firstToJudge: true, flying: true), firstToJudge: false, flying: false);
        bool stillFirst = ScrollClickBlocker.Verdict(ScrollClickBlocker.Verdict(true, firstToJudge: true, flying: false), firstToJudge: false, flying: true);
        Assert.IsTrue(flyingFirst, "the still list judging second does not clear the flying one's catch");
        Assert.IsTrue(stillFirst, "the flying list judging second still blocks");
        Assert.IsFalse(ScrollClickBlocker.Verdict(ScrollClickBlocker.Verdict(true, firstToJudge: true, flying: false), firstToJudge: false, flying: false),
            "two still lists: a plain tap");
    }

    [Test]
    public void EveryBlocker_JudgesThroughTheVerdict_OncePerPressFrame()
    {
        string update = SourceGuard.MethodBody(SourceGuard.Read("Scripts/Chat/ScrollClickBlocker.cs"), "void Update(");
        StringAssert.Contains("IsBlocking = Verdict(IsBlocking, judgedFrame != frame, flying);", update);
        StringAssert.Contains("judgedFrame = frame;", update, "the first blocker to judge a press frame starts it afresh");
    }
}
