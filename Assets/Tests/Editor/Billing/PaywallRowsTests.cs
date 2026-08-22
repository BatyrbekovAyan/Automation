using NUnit.Framework;

/// <summary>
/// Pins the paywall's ONE string seam. Every expected literal writes the non-breaking
/// space as a \u00A0 escape rather than the raw byte — a raw NBSP has silently
/// degraded to a plain space through an editing round-trip before (Task 4 lesson).
/// </summary>
public class PaywallRowsTests
{
    // ── Order + highlight ────────────────────────────────────────────────────

    [Test]
    public void Build_returns_three_tiers_in_catalog_order()
    {
        var rows = PaywallRows.Build(PaywallPeriod.Month);
        Assert.AreEqual(3, rows.Length);
        Assert.AreEqual(PlanTier.Start, rows[0].Tier);
        Assert.AreEqual(PlanTier.Business, rows[1].Tier);
        Assert.AreEqual(PlanTier.Network, rows[2].Tier);
    }

    [Test]
    public void Business_is_the_only_highlighted_row()
    {
        var rows = PaywallRows.Build(PaywallPeriod.Month);
        Assert.IsFalse(rows[0].IsHighlighted);
        Assert.IsTrue(rows[1].IsHighlighted);
        Assert.IsFalse(rows[2].IsHighlighted);
    }

    [Test]
    public void Cross_bot_summary_line_is_business_and_network_only()
    {
        var rows = PaywallRows.Build(PaywallPeriod.Year);
        Assert.IsFalse(rows[0].ShowCrossBotLine);
        Assert.IsTrue(rows[1].ShowCrossBotLine);
        Assert.IsTrue(rows[2].ShowCrossBotLine);
    }

    [Test]
    public void Titles_are_the_russian_tier_names()
    {
        var rows = PaywallRows.Build(PaywallPeriod.Month);
        Assert.AreEqual("Старт", rows[0].Title);
        Assert.AreEqual("Бизнес", rows[1].Title);
        Assert.AreEqual("Сеть", rows[2].Title);
    }

    // ── Prices (exact, incl. NBSP grouping) ──────────────────────────────────

    [Test]
    public void Month_prices_match_the_tariff_matrix()
    {
        var rows = PaywallRows.Build(PaywallPeriod.Month);
        Assert.AreEqual("9\u00A0900\u00A0₸/мес", rows[0].PriceText);
        Assert.AreEqual("19\u00A0900\u00A0₸/мес", rows[1].PriceText);
        Assert.AreEqual("39\u00A0900\u00A0₸/мес", rows[2].PriceText);
    }

    [Test]
    public void Year_prices_match_the_tariff_matrix()
    {
        var rows = PaywallRows.Build(PaywallPeriod.Year);
        Assert.AreEqual("99\u00A0000\u00A0₸/год", rows[0].PriceText);
        Assert.AreEqual("199\u00A0000\u00A0₸/год", rows[1].PriceText);
        Assert.AreEqual("399\u00A0000\u00A0₸/год", rows[2].PriceText);
    }

    // ── Counts line + RU plural agreement ────────────────────────────────────

    [Test]
    public void Counts_lines_carry_bots_channels_and_dialogs()
    {
        var rows = PaywallRows.Build(PaywallPeriod.Month);
        Assert.AreEqual("1 бот · 1 канал · 300 диалогов ИИ/мес", rows[0].CountsLine);
        Assert.AreEqual("3 бота · 3 канала · 1\u00A0000 диалогов ИИ/мес", rows[1].CountsLine);
        Assert.AreEqual("5 ботов · 5 каналов · 3\u00A0000 диалогов ИИ/мес", rows[2].CountsLine);
    }

    [Test]
    public void Counts_line_is_period_independent()
    {
        var month = PaywallRows.Build(PaywallPeriod.Month);
        var year = PaywallRows.Build(PaywallPeriod.Year);
        for (int i = 0; i < month.Length; i++)
            Assert.AreEqual(month[i].CountsLine, year[i].CountsLine);
    }

    [TestCase(1, "1 бот")]
    [TestCase(3, "3 бота")]
    [TestCase(5, "5 ботов")]
    [TestCase(11, "11 ботов")]
    public void Bot_plural(int n, string expected) => Assert.AreEqual(expected, PaywallCopy.Bots(n));

    [TestCase(1, "1 канал")]
    [TestCase(3, "3 канала")]
    [TestCase(5, "5 каналов")]
    [TestCase(14, "14 каналов")]
    public void Channel_plural(int n, string expected) => Assert.AreEqual(expected, PaywallCopy.Channels(n));

    [Test]
    public void Dialogs_group_thousands_with_nbsp()
        => Assert.AreEqual("1\u00A0000 диалогов", PaywallCopy.Dialogs(1000));

    // ── CTA ──────────────────────────────────────────────────────────────────

    [Test]
    public void Cta_offers_the_free_trial_while_nothing_started_and_nothing_bought()
        => Assert.AreEqual(PaywallCopy.TrialCta(),
            PaywallRows.CtaText(false, PlanTier.None, PlanTier.Business, PaywallPeriod.Month));

    [Test]
    public void Cta_offers_the_free_trial_verbatim()
        => Assert.AreEqual("Попробовать 5 дней бесплатно",
            PaywallRows.CtaText(false, PlanTier.None, PlanTier.Business, PaywallPeriod.Month));

    [Test]
    public void Cta_switches_to_subscribe_once_the_trial_clock_started()
        => Assert.AreEqual("Оформить Бизнес — 19\u00A0900\u00A0₸/мес",
            PaywallRows.CtaText(true, PlanTier.None, PlanTier.Business, PaywallPeriod.Month));

    [Test]
    public void Cta_switches_to_subscribe_once_something_is_purchased()
        => Assert.AreEqual("Оформить Сеть — 399\u00A0000\u00A0₸/год",
            PaywallRows.CtaText(false, PlanTier.Start, PlanTier.Network, PaywallPeriod.Year));

    [Test]
    public void Cta_follows_the_selected_tier_and_period()
        => Assert.AreEqual("Оформить Старт — 99\u00A0000\u00A0₸/год",
            PaywallRows.CtaText(true, PlanTier.None, PlanTier.Start, PaywallPeriod.Year));

    // ── Sku routing (what the CTA actually buys) ─────────────────────────────

    [TestCase(PlanTier.Start, PaywallPeriod.Month, "sub.start.month")]
    [TestCase(PlanTier.Start, PaywallPeriod.Year, "sub.start.year")]
    [TestCase(PlanTier.Business, PaywallPeriod.Month, "sub.business.month")]
    [TestCase(PlanTier.Business, PaywallPeriod.Year, "sub.business.year")]
    [TestCase(PlanTier.Network, PaywallPeriod.Month, "sub.network.month")]
    [TestCase(PlanTier.Network, PaywallPeriod.Year, "sub.network.year")]
    public void Sku_for_selection(PlanTier tier, PaywallPeriod period, string sku)
        => Assert.AreEqual(sku, PaywallRows.Sku(tier, period));

    // ── Feature checklist («Во всех тарифах») ────────────────────────────────

    [Test]
    public void All_plans_block_lists_eight_features()
        => Assert.AreEqual(8, PaywallRows.AllPlansFeatures.Length);

    [Test]
    public void All_plans_block_has_no_blank_lines()
    {
        foreach (var f in PaywallRows.AllPlansFeatures)
            Assert.IsFalse(string.IsNullOrWhiteSpace(f));
    }

    // ── Value receipt ────────────────────────────────────────────────────────

    [Test]
    public void Stat_value_shows_a_dash_when_the_number_is_not_reachable()
        => Assert.AreEqual("—", PaywallRows.StatValue(null));

    [Test]
    public void Stat_value_groups_thousands()
        => Assert.AreEqual("1\u00A0234", PaywallRows.StatValue(1234));

    [Test]
    public void Stat_value_keeps_a_real_zero_as_zero()
        => Assert.AreEqual("0", PaywallRows.StatValue(0));

    [Test]
    public void Receipt_title_names_the_trial_length()
        => Assert.AreEqual("Ваш бот за 5 дней", PaywallCopy.ReceiptTitle());

    // ── Copy moved out of PaywallController by Task 14b ───────────────────────

    [Test]
    public void Receipt_labels_are_four_tiles_in_order()
    {
        Assert.AreEqual(4, PaywallRows.ReceiptLabels.Length);
        Assert.AreEqual("Диалогов обработано", PaywallRows.ReceiptLabels[0]);
        Assert.AreEqual("Заказов собрано", PaywallRows.ReceiptLabels[1]);
        Assert.AreEqual("Ответов ночью", PaywallRows.ReceiptLabels[2]);
        Assert.AreEqual("Средний ответ", PaywallRows.ReceiptLabels[3]);
    }

    [Test]
    public void Store_failure_notices_are_pinned()
    {
        // Wording is still with the owner; pinning it here is what makes the reword one edit.
        Assert.AreEqual("Не удалось оформить подписку. Попробуйте ещё раз.",
            PaywallRows.PurchaseFailedNotice);
        Assert.AreEqual("Активных покупок не найдено", PaywallRows.RestoreNothingFoundNotice);
        Assert.AreEqual("Не удалось восстановить покупки", PaywallRows.RestoreFailedNotice);
    }

    [Test]
    public void No_notice_is_blank()
    {
        foreach (string notice in new[] { PaywallRows.PurchaseFailedNotice, PaywallRows.RestoreNothingFoundNotice, PaywallRows.RestoreFailedNotice })
            Assert.IsFalse(string.IsNullOrWhiteSpace(notice));
    }
}
