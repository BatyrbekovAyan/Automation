using NUnit.Framework;

/// <summary>
/// Pins the limit gate sheet's copy and its interception rule (Task 14d, spec §6).
///
/// Two things are load-bearing here and would break silently without exact-match
/// assertions: the RU plural agreement on the count («1 бот» / «3 бота» / «5 ботов»),
/// which <see cref="RuPlural"/> owns but which only a literal can prove reached the
/// sentence; and the truth table of
/// <see cref="BillingGateRows.ShouldInterceptWithSheet"/> — a `true` leaking onto
/// Browse would put a «Лимит ботов» sheet in front of the browse paywall, and a
/// `false` on either limit trigger silently restores the pre-14d behaviour (the full
/// paywall on every refusal).
///
/// Same discipline as PaywallRowsTests / BotsPageRowsTests: the strings are compared
/// byte-for-byte. None of them carries an NBSP (every count here is ≤ 5, so
/// PaywallCopy.Number never inserts a group separator) — if a future tier pushes a
/// count past 999, write the expectation as a \u00A0 escape rather than the raw byte.
/// </summary>
public class BillingGateRowsTests
{
    // ── Interception truth table ─────────────────────────────────────────────

    [TestCase(PaywallTrigger.BotLimit, true)]
    [TestCase(PaywallTrigger.ChannelLimit, true)]
    [TestCase(PaywallTrigger.TrialExpired, false)]
    [TestCase(PaywallTrigger.Browse, false)]
    public void Only_the_two_limit_triggers_get_a_sheet(PaywallTrigger trigger, bool intercept)
        => Assert.AreEqual(intercept, BillingGateRows.ShouldInterceptWithSheet(trigger));

    // ── Titles ───────────────────────────────────────────────────────────────

    [Test]
    public void Bot_limit_title()
        => Assert.AreEqual("Лимит ботов вашего тарифа", BillingGateRows.Title(PaywallTrigger.BotLimit));

    [Test]
    public void Channel_limit_title()
        => Assert.AreEqual("Лимит каналов вашего тарифа", BillingGateRows.Title(PaywallTrigger.ChannelLimit));

    // ── Bodies · bot limit ───────────────────────────────────────────────────

    [Test]
    public void Bot_limit_on_start_names_the_tier_and_its_single_bot()
        => Assert.AreEqual("В тарифе «Старт» — 1 бот. Повысьте тариф, чтобы добавить ещё.",
            BillingGateRows.Body(PaywallTrigger.BotLimit, PlanTier.Start));

    [Test]
    public void Bot_limit_on_business_takes_the_few_form()
        => Assert.AreEqual("В тарифе «Бизнес» — 3 бота. Повысьте тариф, чтобы добавить ещё.",
            BillingGateRows.Body(PaywallTrigger.BotLimit, PlanTier.Business));

    [Test]
    public void Bot_limit_on_network_takes_the_many_form()
        => Assert.AreEqual("В тарифе «Сеть» — 5 ботов. Повысьте тариф, чтобы добавить ещё.",
            BillingGateRows.Body(PaywallTrigger.BotLimit, PlanTier.Network));

    [Test]
    public void Bot_limit_on_trial_says_period_not_tariff()
    {
        // «В тарифе «Пробный»» would be a lie — the trial is not a tariff the owner chose.
        Assert.AreEqual("В пробном периоде — 3 бота. Повысьте тариф, чтобы добавить ещё.",
            BillingGateRows.Body(PaywallTrigger.BotLimit, PlanTier.Trial));
    }

    // ── Bodies · channel limit ───────────────────────────────────────────────

    [Test]
    public void Channel_limit_on_start_uses_the_connect_verb()
        => Assert.AreEqual("В тарифе «Старт» — 1 канал. Повысьте тариф, чтобы подключить ещё.",
            BillingGateRows.Body(PaywallTrigger.ChannelLimit, PlanTier.Start));

    [Test]
    public void Channel_limit_on_business_takes_the_few_form()
        => Assert.AreEqual("В тарифе «Бизнес» — 3 канала. Повысьте тариф, чтобы подключить ещё.",
            BillingGateRows.Body(PaywallTrigger.ChannelLimit, PlanTier.Business));

    [Test]
    public void Channel_limit_on_network_takes_the_many_form()
        => Assert.AreEqual("В тарифе «Сеть» — 5 каналов. Повысьте тариф, чтобы подключить ещё.",
            BillingGateRows.Body(PaywallTrigger.ChannelLimit, PlanTier.Network));

    [Test]
    public void Channel_limit_on_trial_says_period_not_tariff()
        => Assert.AreEqual("В пробном периоде — 3 канала. Повысьте тариф, чтобы подключить ещё.",
            BillingGateRows.Body(PaywallTrigger.ChannelLimit, PlanTier.Trial));

    // ── Bodies · None (nothing bought, trial spent) ──────────────────────────

    [Test]
    public void No_subscription_never_quotes_a_zero_allowance_for_bots()
    {
        // PlanCatalog.Get(None) is 0/0, so the tier sentence would read «— 0 ботов»,
        // which sounds like a broken plan rather than «you don't have one».
        Assert.AreEqual("Подписка не оформлена. Оформите тариф, чтобы добавить бота.",
            BillingGateRows.Body(PaywallTrigger.BotLimit, PlanTier.None));
    }

    [Test]
    public void No_subscription_never_quotes_a_zero_allowance_for_channels()
        => Assert.AreEqual("Подписка не оформлена. Оформите тариф, чтобы подключить канал.",
            BillingGateRows.Body(PaywallTrigger.ChannelLimit, PlanTier.None));

    // ── Drift guards ─────────────────────────────────────────────────────────

    [TestCase(PlanTier.Start)]
    [TestCase(PlanTier.Business)]
    [TestCase(PlanTier.Network)]
    [TestCase(PlanTier.Trial)]
    public void The_bot_sentence_quotes_the_catalog_and_not_a_literal(PlanTier tier)
    {
        // The whole point of the seam: raising a tier's MaxBots must move this sentence
        // with it, so the sheet can never advertise a limit the gate no longer enforces.
        StringAssert.Contains(PaywallCopy.Bots(PlanCatalog.Get(tier).MaxBots),
            BillingGateRows.Body(PaywallTrigger.BotLimit, tier));
    }

    [TestCase(PlanTier.Start)]
    [TestCase(PlanTier.Business)]
    [TestCase(PlanTier.Network)]
    [TestCase(PlanTier.Trial)]
    public void The_channel_sentence_quotes_the_catalog_and_not_a_literal(PlanTier tier)
        => StringAssert.Contains(PaywallCopy.Channels(PlanCatalog.Get(tier).MaxChannels),
            BillingGateRows.Body(PaywallTrigger.ChannelLimit, tier));

    [TestCase(PlanTier.Start)]
    [TestCase(PlanTier.Business)]
    [TestCase(PlanTier.Network)]
    public void A_named_tier_is_quoted_by_its_display_name(PlanTier tier)
        => StringAssert.Contains("«" + PaywallCopy.TierName(tier) + "»",
            BillingGateRows.Body(PaywallTrigger.BotLimit, tier));

    // ── Buttons ──────────────────────────────────────────────────────────────

    [Test]
    public void Primary_cta_sends_the_owner_to_the_paywall()
        => Assert.AreEqual("Посмотреть тарифы", BillingGateRows.PrimaryCta());

    [Test]
    public void Secondary_cta_is_a_plain_dismissal()
        => Assert.AreEqual("Позже", BillingGateRows.SecondaryCta());
}
