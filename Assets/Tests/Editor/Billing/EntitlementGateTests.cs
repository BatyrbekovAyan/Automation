using NUnit.Framework;

public class EntitlementGateTests
{
    [SetUp]
    public void Seams()
    {
        EntitlementGate.ResetSeamsForTests();
        BillingService.ResetSeamsForTests();
        UsageStore.ResetSeamsForTests();   // CurrentTier reads it since Task 19
        TrialLedger.Load = _ => "";
        TrialLedger.Save = (_, __) => { };
        TrialLedger.UtcNow = () => System.DateTime.UtcNow;
    }

    [TearDown]
    public void Reset()
    {
        EntitlementGate.ResetSeamsForTests();
        BillingService.ResetSeamsForTests();
        UsageStore.ResetSeamsForTests();
        TrialLedger.ResetSeamsForTests();
    }

    /// <summary>
    /// EntitlementsKnown is false until Initialize() runs, and CurrentTier answers Trial GRACE
    /// for that whole window — so every server-status case below has to resolve entitlements
    /// first, exactly like a real launch does. A fake backend is "known" the moment it is
    /// wired (BillingService only defers for the RevenueCat one).
    /// </summary>
    private static void ResolveEntitlements(params string[] activeEntitlements)
    {
        var fake = new FakeBillingBackend();
        fake.SetActiveEntitlements(activeEntitlements);
        BillingService.BackendFactory = () => fake;
        BillingService.Initialize();
    }

    private static void ServerSays(string status) => UsageStore.Apply(new UsageSnapshot
    {
        success = true,
        plan = "business",
        status = status,
        quota = 1000,
    });

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

    // ── Task 19: сервер против стёртого леджера ──────────────────────────────

    /// <summary>
    /// Живой инцидент 2026-08-26 15:12, целиком через публичную поверхность гейта: анонимный id
    /// RC пережил переустановку, локальный леджер стёрся, зеркало отдаёт «expired» — и мастер
    /// первого бота обязан ОТКАЗАТЬ, а не довести владельца до авторизации WhatsApp.
    /// </summary>
    [Test] public void Server_expired_refuses_the_first_bot_wizard()
    {
        ResolveEntitlements();               // ничего не куплено, entitlements РАЗРЕШЕНЫ
        ServerSays("expired");

        Assert.AreEqual(PlanTier.None, EntitlementGate.CurrentTier);
        Assert.IsFalse(EntitlementGate.CanCreateBot(0), "нулевой бот на истёкшем аккаунте — отказ");
        Assert.IsFalse(EntitlementGate.CanConnectChannels(0, 1));
    }

    [TestCase("trialing")]
    [TestCase("active")]
    [TestCase("grace")]
    public void A_serviceable_server_status_leaves_onboarding_open(string status)
    {
        ResolveEntitlements();
        ServerSays(status);

        Assert.AreEqual(PlanTier.Trial, EntitlementGate.CurrentTier);
        Assert.IsTrue(EntitlementGate.CanCreateBot(0));
    }

    /// <summary>Fail-open: снимка нет (холодный старт, офлайн, упавший GetUsage) — как раньше.</summary>
    [Test] public void No_server_snapshot_keeps_the_pre_auth_grace()
    {
        ResolveEntitlements();

        Assert.AreEqual(PlanTier.Trial, EntitlementGate.CurrentTier);
        Assert.IsTrue(EntitlementGate.CanCreateBot(0));
    }

    /// <summary>
    /// Покупка бьёт всё: зеркало могло не успеть обновиться после оплаты (оно event-driven),
    /// и запирать заплатившего владельца на устаревшем «expired» — худший из возможных исходов.
    /// </summary>
    [Test] public void A_purchase_beats_a_stale_expired_snapshot()
    {
        ResolveEntitlements("tier_business");
        ServerSays("expired");

        Assert.AreEqual(PlanTier.Business, EntitlementGate.CurrentTier);
        Assert.IsTrue(EntitlementGate.CanCreateBot(0));
    }

    /// <summary>
    /// Триместное состояние EntitlementsKnown НЕ трогали: пока первый ответ CustomerInfo не
    /// пришёл, гейт по-прежнему отвечает Trial — даже с «expired» на руках. Иначе окно
    /// разрешения превратилось бы в пейволл для платящего.
    /// </summary>
    [Test] public void The_resolve_window_still_wins_over_the_server_status()
    {
        ServerSays("expired");   // без Initialize(): EntitlementsKnown == false

        Assert.IsFalse(BillingService.EntitlementsKnown);
        Assert.AreEqual(PlanTier.Trial, EntitlementGate.CurrentTier);
    }

    /// <summary>
    /// Куда ведёт отказ мастера. Потолок тарифа — лёгкий лист (BotLimit его перехватывает);
    /// «expired» — полноэкранный пейволл с чеком ценности, который лист НЕ перехватывает.
    /// </summary>
    [Test] public void Refusal_trigger_is_the_bot_limit_sheet_by_default()
    {
        Assert.AreEqual(PaywallTrigger.BotLimit, EntitlementGate.BotRefusalTrigger(false));
        Assert.IsTrue(BillingGateRows.ShouldInterceptWithSheet(EntitlementGate.BotRefusalTrigger(false)));
    }

    [Test] public void Refusal_trigger_is_the_full_paywall_when_the_server_says_expired()
    {
        Assert.AreEqual(PaywallTrigger.TrialExpired, EntitlementGate.BotRefusalTrigger(true));
        Assert.IsFalse(BillingGateRows.ShouldInterceptWithSheet(EntitlementGate.BotRefusalTrigger(true)),
            "истёкшая подписка — не потолок тарифа: одна строка в листе потратила бы этот момент впустую");
    }

    [Test] public void ResetSeamsForTests_clears_paywall_subscribers()
    {
        bool called = false;
        EntitlementGate.OnPaywallRequested += _ => called = true;

        EntitlementGate.ResetSeamsForTests();
        EntitlementGate.RequestPaywall(PaywallTrigger.Browse);

        Assert.IsFalse(called, "a subscriber surviving ResetSeamsForTests would leak into other tests");
    }
}
