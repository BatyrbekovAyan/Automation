using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Learning B + the percent: the lock is a hint, never a guard — Locked ONLY on the server's positive verdict (a listed bot
/// that sent a `progress`, is not ready, and whose «Авто» never opened). The percent on screen is the best of the phone's
/// memory and the server's number, and the ONE entry point the gate, the sheet and the capsules read is `StateFor`.
/// </summary>
public class AutoReadinessPolicyTests
{
    private const string StateBot = "LearnStateBot";

    [SetUp] public void SetUp() { UsageStore.ResetSeamsForTests(); LearnProgressStore.Clear(StateBot); }
    [TearDown] public void TearDown() { UsageStore.ResetSeamsForTests(); LearnProgressStore.Clear(StateBot); }

    /// <summary>A pre-percent row (no `progress`): what an older server sends.</summary>
    private static AutoReadinessEntry Entry(string key, int matches, int total, bool ready) =>
        new AutoReadinessEntry { botKey = key, matches = matches, total = total, chats = 3, days = 2, ready = ready };

    private static AutoReadinessEntry Row(int? progress, bool ready = false) =>
        new AutoReadinessEntry { botKey = StateBot, progress = progress, ready = ready, unlockAt = 70 };

    /// <summary>Applies one snapshot the way UsageClient does: through the wire parser, then the store.</summary>
    private static void ApplyRow(string fields) =>
        UsageStore.Apply(UsageStore.Parse("{\"success\":true,\"plan\":\"trial\",\"readiness\":[{\"botKey\":\"" + StateBot + "\"," + fields + "}]}"));

    [Test]
    public void NullEntry_IsOpen_AndProgressZero()
    {
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(null, false), "no snapshot / no row / old server → never locked");
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(null, true));
        Assert.AreEqual(0, AutoReadinessPolicy.Progress(null));
    }

    [Test]
    public void Verdict_LocksOnlyAListedBotWithProgress_NotReady_NeverUnlocked()
    {
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(null, false));
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(Row(progress: null, ready: false), false));
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(Row(progress: 75, ready: true), false));
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(Row(progress: 56, ready: false), true));
        Assert.AreEqual(AutoReadinessVerdict.Locked, AutoReadinessPolicy.Verdict(Row(progress: 0, ready: false), false), "a brand-new bot starts locked");
    }

    [Test]
    public void Verdict_AnOlderServersRow_WithoutProgress_IsOpen()
    {
        // The pre-percent server judged `ready` by its own 10/20 rule and sent no `progress` — with the percent redesign that row means
        // "no data", exactly like an unlisted bot: the lock must not come from a number the new rule did not compute.
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(Entry("Bot0", 7, 12, false), false));
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(Entry("Bot0", 0, 0, false), false));
    }

    [Test]
    public void DisplayPercent_IsTheBestOfMemoryAndServer_Clamped0To100()
    {
        Assert.AreEqual(75, AutoReadinessPolicy.DisplayPercent(Row(progress: 56), 75));
        Assert.AreEqual(81, AutoReadinessPolicy.DisplayPercent(Row(progress: 81), 75));
        Assert.AreEqual(100, AutoReadinessPolicy.DisplayPercent(Row(progress: 130), null));
        Assert.AreEqual(0, AutoReadinessPolicy.DisplayPercent(Row(progress: -5), null));
        Assert.AreEqual(44, AutoReadinessPolicy.DisplayPercent(Row(progress: null), 44));
        Assert.AreEqual(44, AutoReadinessPolicy.DisplayPercent(null, 44));
        Assert.IsNull(AutoReadinessPolicy.DisplayPercent(null, null));
        Assert.AreEqual(100, AutoReadinessPolicy.DisplayPercent(Row(progress: 20), 250), "a garbage memory is clamped too");
    }

    [Test]
    public void Progress_IsMatchesClampedToTheWindow()
    {
        Assert.AreEqual(7, AutoReadinessPolicy.Progress(Entry("Bot0", 7, 12, false)));
        Assert.AreEqual(AutoReadinessPolicy.Window, AutoReadinessPolicy.Progress(Entry("Bot0", 25, 20, true)), "the server's window bounds the number we print");
        Assert.AreEqual(0, AutoReadinessPolicy.Progress(Entry("Bot0", -3, 0, false)), "a garbage negative never prints");
        Assert.AreEqual(20, AutoReadinessPolicy.Window);
        Assert.AreEqual(10, AutoReadinessPolicy.MinMatches);
    }

    [Test]
    public void Store_FindsTheBotsRow_ByBotKey_AndNothingElse()
    {
        UsageStore.Apply(new UsageSnapshot { success = true, readiness = new List<AutoReadinessEntry> { Entry("Bot0", 3, 5, false), Entry("Bot1", 11, 14, true) } });
        Assert.AreEqual(3, AutoReadinessStore.Current("Bot0").matches);
        Assert.IsTrue(AutoReadinessStore.Current("Bot1").ready);
        Assert.IsNull(AutoReadinessStore.Current("Bot7"), "a bot the server did not list");
        Assert.IsNull(AutoReadinessStore.Current(""), "no key");
        Assert.IsNull(AutoReadinessStore.Current(null));
    }

    [Test]
    public void Store_WithoutSnapshot_OrWithoutTheField_IsNull_AndOpen()
    {
        Assert.IsNull(AutoReadinessStore.Current("Bot0"), "no snapshot yet");
        UsageStore.Apply(new UsageSnapshot { success = true });   // an old server: no `readiness` field → null list
        Assert.IsNull(AutoReadinessStore.Current("Bot0"));
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(AutoReadinessStore.Current("Bot0"), false));
    }

    // --- StateFor: the one entry point ----------------------------------------------------

    [Test]
    public void StateFor_CarriesTheServerFacts_AndUnlockSticks()
    {
        // A capped bot: good answers, but the history spans too few chats and days.
        ApplyRow("\"matches\":12,\"total\":14,\"chats\":1,\"days\":1,\"ready\":false,\"bestMatches\":12,"
               + "\"progress\":69,\"unlockAt\":70,\"capped\":true,\"needChats\":2,\"needDays\":1");
        LearningState? first = AutoReadinessPolicy.StateFor(StateBot);
        Assert.IsTrue(first.HasValue);
        Assert.AreEqual(69, first.Value.Percent);
        Assert.AreEqual(70, first.Value.UnlockAt);
        Assert.IsTrue(first.Value.Capped);
        Assert.AreEqual(2, first.Value.NeedChats);
        Assert.AreEqual(1, first.Value.NeedDays);
        Assert.IsTrue(first.Value.Locked);
        Assert.IsFalse(first.Value.Trained);

        // The conditions are met: ready → the lock lifts.
        ApplyRow("\"matches\":12,\"total\":14,\"chats\":3,\"days\":2,\"ready\":true,\"bestMatches\":12,"
               + "\"progress\":75,\"unlockAt\":70,\"capped\":false,\"needChats\":0,\"needDays\":0");
        LearningState? ready = AutoReadinessPolicy.StateFor(StateBot);
        Assert.AreEqual(75, ready.Value.Percent);
        Assert.IsFalse(ready.Value.Locked);

        // The window got worse (the server says "not ready" again): the phone remembers both the best percent and the unlock.
        ApplyRow("\"matches\":8,\"total\":14,\"chats\":3,\"days\":2,\"ready\":false,\"bestMatches\":10,"
               + "\"progress\":63,\"unlockAt\":70,\"capped\":false,\"needChats\":0,\"needDays\":0");
        LearningState? worse = AutoReadinessPolicy.StateFor(StateBot);
        Assert.AreEqual(75, worse.Value.Percent, "the percent never falls");
        Assert.IsFalse(worse.Value.Locked, "an «Авто» that opened once is never locked again");
    }

    [Test]
    public void StateFor_OldServerWithoutProgress_IsNoData()
    {
        ApplyRow("\"matches\":7,\"total\":12,\"chats\":3,\"days\":2,\"ready\":false");
        Assert.IsFalse(AutoReadinessPolicy.StateFor(StateBot).HasValue, "a row without `progress` is an older server — no fill, no lock");
    }

    [Test]
    public void StateFor_AProgressOfZero_IsData_NotNoData()
    {
        ApplyRow("\"ready\":false,\"bestMatches\":0,\"progress\":0,\"unlockAt\":70,\"capped\":false,\"needChats\":3,\"needDays\":2");
        LearningState? state = AutoReadinessPolicy.StateFor(StateBot);
        Assert.IsTrue(state.HasValue, "0 % is a number the server sent; only an ABSENT field is «no data»");
        Assert.AreEqual(0, state.Value.Percent);
        Assert.IsTrue(state.Value.Locked);
    }

    [Test]
    public void StateFor_WithoutARow_FallsBackToThePhonesMemory_AndNeverLocks()
    {
        ApplyRow("\"ready\":false,\"bestMatches\":9,\"progress\":56,\"unlockAt\":70,\"capped\":false,\"needChats\":0,\"needDays\":0");
        Assert.IsTrue(AutoReadinessPolicy.StateFor(StateBot).Value.Locked, "premise: the server listed the bot and it is locked");

        UsageStore.Apply(UsageStore.Parse("{\"success\":true,\"readiness\":[]}"));   // a later snapshot that does not list the bot
        LearningState? state = AutoReadinessPolicy.StateFor(StateBot);
        Assert.IsTrue(state.HasValue);
        Assert.AreEqual(56, state.Value.Percent, "the memory speaks when the server does not");
        Assert.IsFalse(state.Value.Locked, "memory alone never locks");
        Assert.AreEqual(0, state.Value.UnlockAt);
        Assert.IsFalse(state.Value.Capped);
        Assert.AreEqual(0, state.Value.NeedChats);
        Assert.AreEqual(0, state.Value.NeedDays);
    }

    [Test]
    public void StateFor_BeforeTheFirstSnapshot_ShowsTheMemory_AnOfflineStart()
    {
        LearnProgressStore.Absorb(new UsageSnapshot { success = true, readiness = new List<AutoReadinessEntry> { Row(63) } });
        UsageStore.ResetSeamsForTests();   // a cold start: no snapshot yet, the PlayerPrefs memory is all there is
        Assert.IsNull(UsageStore.Current);

        LearningState? state = AutoReadinessPolicy.StateFor(StateBot);
        Assert.IsTrue(state.HasValue);
        Assert.AreEqual(63, state.Value.Percent);
        Assert.IsFalse(state.Value.Locked);
    }

    [Test]
    public void StateFor_NothingKnown_OrNoKey_IsNoData()
    {
        Assert.IsFalse(AutoReadinessPolicy.StateFor(StateBot).HasValue, "no snapshot, no memory");
        Assert.IsFalse(AutoReadinessPolicy.StateFor("").HasValue);
        Assert.IsFalse(AutoReadinessPolicy.StateFor(null).HasValue);
    }

    [Test]
    public void LearningState_IsTrainedFrom100()
    {
        Assert.IsFalse(new LearningState(99, 70, false, 0, 0, false).Trained);
        Assert.IsTrue(new LearningState(100, 70, false, 0, 0, false).Trained);
    }
}
