using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The phone's own memory of a bot's learning: the best percent the server ever reported and whether «Авто» ever opened.
/// Runs against the real PlayerPrefs under test-only bot names (the house pattern — see ScheduleModelTests).
/// </summary>
public class LearnProgressStoreTests
{
    private const string Bot = "LearnStoreBot";
    private const string OtherBot = "LearnStoreOtherBot";

    [SetUp]
    [TearDown]
    public void WipeTestBots()
    {
        LearnProgressStore.Clear(Bot);
        LearnProgressStore.Clear(OtherBot);
    }

    private static AutoReadinessEntry Row(string botKey, int? progress, bool ready = false) =>
        new AutoReadinessEntry { botKey = botKey, progress = progress, ready = ready };

    private static UsageSnapshot Snapshot(params AutoReadinessEntry[] rows) =>
        new UsageSnapshot { success = true, readiness = new List<AutoReadinessEntry>(rows) };

    [Test]
    public void Absorb_KeepsTheBestPercent_AndNeverForgetsAnUnlock()
    {
        Assert.IsNull(LearnProgressStore.Peak(Bot), "nothing remembered yet");
        Assert.IsFalse(LearnProgressStore.Unlocked(Bot));

        LearnProgressStore.Absorb(Snapshot(Row(Bot, 56)));
        Assert.AreEqual(56, LearnProgressStore.Peak(Bot));
        Assert.IsFalse(LearnProgressStore.Unlocked(Bot), "not ready → not unlocked");

        LearnProgressStore.Absorb(Snapshot(Row(Bot, 75, ready: true)));
        Assert.AreEqual(75, LearnProgressStore.Peak(Bot));
        Assert.IsTrue(LearnProgressStore.Unlocked(Bot));

        LearnProgressStore.Absorb(Snapshot(Row(Bot, 63)));   // the window got worse
        Assert.AreEqual(75, LearnProgressStore.Peak(Bot), "the best percent never falls");
        Assert.IsTrue(LearnProgressStore.Unlocked(Bot), "an unlock is never forgotten");
    }

    [Test]
    public void Absorb_IgnoresRowsWithoutProgress_AndClampsGarbage()
    {
        LearnProgressStore.Absorb(Snapshot(Row(Bot, null, ready: true)));   // an older server's row — not data, not even its `ready`
        Assert.IsNull(LearnProgressStore.Peak(Bot));
        Assert.IsFalse(LearnProgressStore.Unlocked(Bot));

        LearnProgressStore.Absorb(Snapshot(Row(Bot, 130)));
        Assert.AreEqual(100, LearnProgressStore.Peak(Bot), "an impossible high value is clamped to 100");

        LearnProgressStore.Clear(Bot);
        LearnProgressStore.Absorb(Snapshot(Row(Bot, -5)));
        Assert.AreEqual(0, LearnProgressStore.Peak(Bot), "an impossible negative value is clamped to 0 — and 0 is still a memory");
    }

    [Test]
    public void Absorb_AZeroAfterNothing_IsRemembered_ButNeverLowersAnything()
    {
        LearnProgressStore.Absorb(Snapshot(Row(Bot, 0)));
        Assert.AreEqual(0, LearnProgressStore.Peak(Bot), "the server did list the bot at 0 %");

        LearnProgressStore.Absorb(Snapshot(Row(Bot, 31)));
        LearnProgressStore.Absorb(Snapshot(Row(Bot, 0)));
        Assert.AreEqual(31, LearnProgressStore.Peak(Bot));
    }

    [Test]
    public void Absorb_RemembersEveryListedBot_AndTouchesNoOtherOne()
    {
        LearnProgressStore.Absorb(Snapshot(Row(Bot, 40), Row(OtherBot, 90, ready: true)));
        Assert.AreEqual(40, LearnProgressStore.Peak(Bot));
        Assert.AreEqual(90, LearnProgressStore.Peak(OtherBot));
        Assert.IsFalse(LearnProgressStore.Unlocked(Bot), "one bot's ready never unlocks its neighbour");
        Assert.IsTrue(LearnProgressStore.Unlocked(OtherBot));

        LearnProgressStore.Absorb(Snapshot(Row(Bot, 10)));   // a snapshot that does not list OtherBot leaves it alone
        Assert.AreEqual(90, LearnProgressStore.Peak(OtherBot));
        Assert.IsTrue(LearnProgressStore.Unlocked(OtherBot));
    }

    [Test]
    public void Absorb_NothingToRead_IsANoOp_NeverThrows()
    {
        Assert.DoesNotThrow(() => LearnProgressStore.Absorb(null));
        Assert.DoesNotThrow(() => LearnProgressStore.Absorb(new UsageSnapshot { success = true }));   // an old server: readiness null
        Assert.DoesNotThrow(() => LearnProgressStore.Absorb(Snapshot(null, Row("", 50), Row(null, 50))));   // a null row, rows without a bot key
        Assert.IsNull(LearnProgressStore.Peak(Bot));
        Assert.IsNull(LearnProgressStore.Peak(""));
        Assert.IsNull(LearnProgressStore.Peak(null));
        Assert.IsFalse(LearnProgressStore.Unlocked(""));
        Assert.IsFalse(LearnProgressStore.Unlocked(null));
        Assert.IsFalse(PlayerPrefs.HasKey(LearnProgressStore.PeakSuffix), "a row without a bot key never writes under a bare suffix");
        Assert.IsFalse(PlayerPrefs.HasKey(LearnProgressStore.UnlockedSuffix));
    }

    [Test]
    public void Clear_RemovesBothKeys_UnderTheAgreedNames()
    {
        Assert.AreEqual("LearnPeak", LearnProgressStore.PeakSuffix);
        Assert.AreEqual("LearnUnlocked", LearnProgressStore.UnlockedSuffix);

        LearnProgressStore.Absorb(Snapshot(Row(Bot, 80, ready: true)));
        Assert.AreEqual(80, PlayerPrefs.GetInt("LearnStoreBotLearnPeak", -1));
        Assert.AreEqual(1, PlayerPrefs.GetInt("LearnStoreBotLearnUnlocked", -1));

        LearnProgressStore.Clear(Bot);
        Assert.IsFalse(PlayerPrefs.HasKey("LearnStoreBotLearnPeak"));
        Assert.IsFalse(PlayerPrefs.HasKey("LearnStoreBotLearnUnlocked"));
        Assert.IsNull(LearnProgressStore.Peak(Bot));
        Assert.IsFalse(LearnProgressStore.Unlocked(Bot));

        Assert.DoesNotThrow(() => LearnProgressStore.Clear(null));
        Assert.DoesNotThrow(() => LearnProgressStore.Clear(""));
    }
}
