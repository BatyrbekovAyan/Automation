using System.Collections.Generic;
using NUnit.Framework;

/// <summary>Learning B: the lock is a hint, never a guard — Locked ONLY on the server's positive verdict.</summary>
public class AutoReadinessPolicyTests
{
    [SetUp] public void SetUp() => UsageStore.ResetSeamsForTests();
    [TearDown] public void TearDown() => UsageStore.ResetSeamsForTests();

    private static AutoReadinessEntry Entry(string key, int matches, int total, bool ready) =>
        new AutoReadinessEntry { botKey = key, matches = matches, total = total, chats = 3, days = 2, ready = ready };

    [Test]
    public void NullEntry_IsOpen_AndProgressZero()
    {
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(null), "no snapshot / no row / old server → never locked");
        Assert.AreEqual(0, AutoReadinessPolicy.Progress(null));
    }

    [Test]
    public void ReadyFalse_IsLocked_ReadyTrue_IsOpen()
    {
        Assert.AreEqual(AutoReadinessVerdict.Locked, AutoReadinessPolicy.Verdict(Entry("Bot0", 7, 12, false)));
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(Entry("Bot0", 12, 15, true)));
        Assert.AreEqual(AutoReadinessVerdict.Locked, AutoReadinessPolicy.Verdict(Entry("Bot0", 0, 0, false)), "a brand-new bot starts locked");
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
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(AutoReadinessStore.Current("Bot0")));
    }
}
