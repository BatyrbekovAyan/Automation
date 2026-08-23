using System;
using NUnit.Framework;

public class TrialLedgerTests
{
    string _stored; DateTime _now;

    [SetUp] public void Seams()
    {
        _stored = ""; _now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        TrialLedger.Load = _ => _stored;
        TrialLedger.Save = (_, v) => _stored = v;
        TrialLedger.UtcNow = () => _now;
    }
    [TearDown] public void Reset() => TrialLedger.ResetSeamsForTests();

    [Test] public void Fresh_install_has_full_trial_not_started()
    {
        Assert.IsFalse(TrialLedger.HasStarted);
        Assert.AreEqual(5, TrialLedger.DaysLeft());
        Assert.IsFalse(TrialLedger.IsExpired);
    }

    [Test] public void Start_stamps_once_and_counts_down()
    {
        TrialLedger.StartIfNeeded();
        var first = _stored;
        _now = _now.AddDays(2.5); TrialLedger.StartIfNeeded();
        Assert.AreEqual(first, _stored, "второй Start не перезаписывает");
        Assert.AreEqual(3, TrialLedger.DaysLeft());   // floor(2.5)=2 прошло
    }

    [Test] public void Expires_after_day_5()
    {
        TrialLedger.StartIfNeeded();
        _now = _now.AddDays(5.01);
        Assert.AreEqual(0, TrialLedger.DaysLeft());
        Assert.IsTrue(TrialLedger.IsExpired);
    }

    [Test] public void Clock_rollback_does_not_extend_past_five_days()
    {
        TrialLedger.StartIfNeeded();
        _now = _now.AddDays(-100);
        Assert.AreEqual(5, TrialLedger.DaysLeft());
        Assert.IsFalse(TrialLedger.IsExpired);
    }

    [Test] public void The_bots_header_pill_appears_only_after_the_first_channel_auth_starts_the_clock()
    {
        // Pre-auth grace: EntitlementPolicy already grants Trial, but nothing has started
        // the clock, so the header pill stays hidden (BotsPageRows.TrialPill's own rule).
        PlanTier before = EntitlementPolicy.EffectiveTier(PlanTier.None, TrialLedger.HasStarted, TrialLedger.IsExpired);
        Assert.AreEqual(PlanTier.Trial, before);
        Assert.IsFalse(BotsPageRows.TrialPill(before, TrialLedger.HasStarted, TrialLedger.DaysLeft()).Visible);

        // …exactly what a confirmed channel authorization now calls (Task 15a, Part A).
        TrialLedger.StartIfNeeded();

        PlanTier after = EntitlementPolicy.EffectiveTier(PlanTier.None, TrialLedger.HasStarted, TrialLedger.IsExpired);
        var pill = BotsPageRows.TrialPill(after, TrialLedger.HasStarted, TrialLedger.DaysLeft());
        Assert.IsTrue(pill.Visible, "пилюля появляется органически, без ручной правки PlayerPrefs");
        Assert.AreEqual("Пробный · 5 дн.", pill.Text);
    }
}
