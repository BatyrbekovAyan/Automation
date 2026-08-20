using NUnit.Framework;

public class EntitlementGateTests
{
    [SetUp]
    public void Seams()
    {
        EntitlementGate.ResetSeamsForTests();
        TrialLedger.Load = _ => "";
        TrialLedger.Save = (_, __) => { };
        TrialLedger.UtcNow = () => System.DateTime.UtcNow;
    }

    [TearDown]
    public void Reset()
    {
        EntitlementGate.ResetSeamsForTests();
        TrialLedger.ResetSeamsForTests();
    }

    [Test] public void CountChannels_empty_is_zero()
        => Assert.AreEqual(0, EntitlementGate.CountChannels(new (bool, bool)[0]));

    [Test] public void CountChannels_sums_true_flags()
        => Assert.AreEqual(3, EntitlementGate.CountChannels(new[] { (true, false), (true, true) }));

    [Test] public void CountChannels_all_false_is_zero()
        => Assert.AreEqual(0, EntitlementGate.CountChannels(new[] { (false, false), (false, false) }));

    // No scene loaded in EditMode — Manager.Instance is null. Must degrade to 0, never throw.
    [Test] public void ConnectedChannelCount_with_no_scene_is_zero()
        => Assert.AreEqual(0, EntitlementGate.ConnectedChannelCount());

    [Test] public void Bot_gate_blocks_at_limit_and_requests_paywall()
    {
        EntitlementGate.PurchasedTierSource = () => PlanTier.Start;   // MaxBots=1

        Assert.IsFalse(EntitlementGate.CanCreateBot(1));

        PaywallTrigger? seen = null;
        void Handler(PaywallTrigger t) => seen = t;
        EntitlementGate.OnPaywallRequested += Handler;
        try
        {
            // Mirrors the call-site pattern: guard, then request the paywall on refusal.
            if (!EntitlementGate.CanCreateBot(1))
                EntitlementGate.RequestPaywall(PaywallTrigger.BotLimit);

            Assert.AreEqual(PaywallTrigger.BotLimit, seen);
        }
        finally
        {
            EntitlementGate.OnPaywallRequested -= Handler;   // leaked handlers pollute other tests
        }
    }

    [Test] public void Channel_gate_blocks_at_limit_and_requests_paywall()
    {
        EntitlementGate.PurchasedTierSource = () => PlanTier.Trial;   // MaxChannels=3

        Assert.IsFalse(EntitlementGate.CanConnectChannel(3));

        PaywallTrigger? seen = null;
        void Handler(PaywallTrigger t) => seen = t;
        EntitlementGate.OnPaywallRequested += Handler;
        try
        {
            if (!EntitlementGate.CanConnectChannel(3))
                EntitlementGate.RequestPaywall(PaywallTrigger.ChannelLimit);

            Assert.AreEqual(PaywallTrigger.ChannelLimit, seen);
        }
        finally
        {
            EntitlementGate.OnPaywallRequested -= Handler;
        }
    }

    // Fresh install: nothing purchased, trial never started → pre-auth grace (Trial),
    // not None — otherwise the first-bot wizard couldn't open on a brand-new install.
    [Test] public void Fresh_install_grace_yields_trial_tier()
        => Assert.AreEqual(PlanTier.Trial, EntitlementGate.CurrentTier);

    // Trial-grace default (seams from [SetUp]): MaxChannels=3. Pins the pre-flight
    // multi-slot check Manager.CreateBotFromForm uses ahead of any pairing.
    [TestCase(2, 1, true)]   // третий канал влезает
    [TestCase(2, 2, false)]  // «Оба» при двух занятых — отказ ДО пейринга
    [TestCase(0, 2, true)]
    [TestCase(3, 0, true)]   // нулевой спрос всегда ок
    public void Demand_math(int connected, int demand, bool ok)
        => Assert.AreEqual(ok, EntitlementGate.CanConnectChannels(connected, demand));

    [Test] public void ResetSeamsForTests_clears_paywall_subscribers()
    {
        bool called = false;
        EntitlementGate.OnPaywallRequested += _ => called = true;

        EntitlementGate.ResetSeamsForTests();
        EntitlementGate.RequestPaywall(PaywallTrigger.Browse);

        Assert.IsFalse(called, "a subscriber surviving ResetSeamsForTests would leak into other tests");
    }
}
