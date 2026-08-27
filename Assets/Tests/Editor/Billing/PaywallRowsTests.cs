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
        Assert.AreEqual("9\u00A0990\u00A0₸/мес", rows[0].PriceText);
        Assert.AreEqual("19\u00A0990\u00A0₸/мес", rows[1].PriceText);
        Assert.AreEqual("39\u00A0900\u00A0₸/мес", rows[2].PriceText);
    }

    [Test]
    public void Year_prices_match_the_tariff_matrix()
    {
        var rows = PaywallRows.Build(PaywallPeriod.Year);
        Assert.AreEqual("99\u00A0000\u00A0₸/год", rows[0].PriceText);
        Assert.AreEqual("198\u00A0990\u00A0₸/год", rows[1].PriceText);
        Assert.AreEqual("399\u00A0990\u00A0₸/год", rows[2].PriceText);
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
            PaywallRows.CtaText(false, PlanTier.None, false, PlanTier.Business, PaywallPeriod.Month));

    [Test]
    public void Cta_offers_the_free_trial_verbatim()
        => Assert.AreEqual("Попробовать 5 дней бесплатно",
            PaywallRows.CtaText(false, PlanTier.None, false, PlanTier.Business, PaywallPeriod.Month));

    [Test]
    public void Cta_switches_to_subscribe_once_the_trial_clock_started()
        => Assert.AreEqual("Оформить Бизнес — 19\u00A0990\u00A0₸/мес",
            PaywallRows.CtaText(true, PlanTier.None, false, PlanTier.Business, PaywallPeriod.Month));

    [Test]
    public void Cta_switches_to_subscribe_once_something_is_purchased()
        => Assert.AreEqual("Оформить Сеть — 399\u00A0990\u00A0₸/год",
            PaywallRows.CtaText(false, PlanTier.Start, false, PlanTier.Network, PaywallPeriod.Year));

    [Test]
    public void Cta_follows_the_selected_tier_and_period()
        => Assert.AreEqual("Оформить Старт — 99\u00A0000\u00A0₸/год",
            PaywallRows.CtaText(true, PlanTier.None, false, PlanTier.Start, PaywallPeriod.Year));

    // ── Secondary direct-purchase button (Task 18) ───────────────────────────

    [Test]
    public void Secondary_purchase_shows_while_the_cta_offers_the_trial()
    {
        var row = PaywallRows.SecondaryPurchase(false, PlanTier.None, false, PlanTier.Business, PaywallPeriod.Month);
        Assert.IsTrue(row.Visible);
        Assert.AreEqual("Оформить Бизнес — 19\u00A0990\u00A0₸/мес", row.Text);
    }

    [Test]
    public void Secondary_purchase_follows_the_selected_tier_and_period()
    {
        var row = PaywallRows.SecondaryPurchase(false, PlanTier.None, false, PlanTier.Network, PaywallPeriod.Year);
        Assert.IsTrue(row.Visible);
        Assert.AreEqual("Оформить Сеть — 399\u00A0990\u00A0₸/год", row.Text);
    }

    [Test]
    public void Secondary_purchase_hides_once_the_trial_clock_started()
        => Assert.IsFalse(PaywallRows.SecondaryPurchase(true, PlanTier.None, false, PlanTier.Business, PaywallPeriod.Month).Visible);

    [Test]
    public void Secondary_purchase_hides_once_something_is_purchased()
        => Assert.IsFalse(PaywallRows.SecondaryPurchase(false, PlanTier.Start, false, PlanTier.Business, PaywallPeriod.Month).Visible);

    [Test]
    public void Secondary_purchase_carries_no_text_while_hidden()
        => Assert.AreEqual("", PaywallRows.SecondaryPurchase(true, PlanTier.Start, false, PlanTier.Network, PaywallPeriod.Year).Text);

    [TestCase(PlanTier.None)]
    [TestCase(PlanTier.Trial)]
    public void Secondary_purchase_hides_for_a_selection_with_no_store_product(PlanTier selected)
        => Assert.IsFalse(PaywallRows.SecondaryPurchase(false, PlanTier.None, false, selected, PaywallPeriod.Month).Visible);

    /// <summary>
    /// The whole point of the button: it appears exactly where the CTA stops naming a tier,
    /// so the paywall can never show the subscribe form twice — nor zero times.
    /// </summary>
    [Test]
    public void Secondary_purchase_is_visible_exactly_when_the_cta_is_the_trial_offer()
    {
        foreach (bool started in new[] { false, true })
        foreach (PlanTier purchased in new[] { PlanTier.None, PlanTier.Start, PlanTier.Business, PlanTier.Network })
        foreach (bool serverExpired in new[] { false, true })
        foreach (PlanTier selected in PaywallRows.Order)
        foreach (PaywallPeriod period in new[] { PaywallPeriod.Month, PaywallPeriod.Year })
        {
            string cta = PaywallRows.CtaText(started, purchased, serverExpired, selected, period);
            var row = PaywallRows.SecondaryPurchase(started, purchased, serverExpired, selected, period);
            bool ctaIsTrial = cta == PaywallCopy.TrialCta();
            Assert.AreEqual(ctaIsTrial, row.Visible,
                $"started={started} purchased={purchased} serverExpired={serverExpired} selected={selected} {period}");
            // Whichever button carries it, the subscribe form reads the same.
            Assert.AreEqual(ctaIsTrial ? row.Text : cta, PaywallRows.SubscribeText(selected, period));
        }
    }

    [TestCase(false, PlanTier.None, true)]
    [TestCase(true, PlanTier.None, false)]
    [TestCase(false, PlanTier.Start, false)]
    [TestCase(true, PlanTier.Business, false)]
    public void Trial_offer_state(bool started, PlanTier purchased, bool expected)
        => Assert.AreEqual(expected, PaywallRows.IsTrialOffer(started, purchased, false));

    // ── Task 19: сервер уже сказал «expired» ─────────────────────────────────

    /// <summary>
    /// Тот же стёртый леджер, что и в гейте: локально триал «не начинался», но зеркало знает,
    /// что подписка кончилась. Предлагать бесплатный триал в этом состоянии — обещание, которое
    /// вебхук Create тут же нарушит.
    /// </summary>
    [Test]
    public void Server_expired_kills_the_trial_offer()
        => Assert.IsFalse(PaywallRows.IsTrialOffer(false, PlanTier.None, true));

    /// <summary>
    /// И CTA обязан стать рабочей кнопкой покупки — иначе у истёкшего аккаунта на пейволле
    /// вообще нет платного пути (это и был баг Task 18, только в другом состоянии).
    /// </summary>
    [Test]
    public void Server_expired_turns_the_cta_into_the_subscribe_form()
        => Assert.AreEqual("Оформить Бизнес — 19\u00A0990\u00A0₸/мес",
            PaywallRows.CtaText(false, PlanTier.None, true, PlanTier.Business, PaywallPeriod.Month));

    /// <summary>Вторая кнопка при этом прячется: дублировать форму подписки нечем и незачем.</summary>
    [Test]
    public void Server_expired_hides_the_secondary_button()
        => Assert.IsFalse(
            PaywallRows.SecondaryPurchase(false, PlanTier.None, true, PlanTier.Business, PaywallPeriod.Month).Visible);

    /// <summary>Неизвестный статус (снимка ещё нет) ничего не меняет — fail-open.</summary>
    [Test]
    public void Unknown_server_status_keeps_the_trial_offer()
        => Assert.IsTrue(PaywallRows.IsTrialOffer(false, PlanTier.None, false));

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

    // ── Fine print (store submission pack) ───────────────────────────────────

    [Test]
    public void FinePrint_trial_state_keeps_no_card_promise_on_both_stores()
    {
        Assert.AreEqual("Без карты · Отмена в любой момент", PaywallRows.FinePrintText(true, true));
        Assert.AreEqual("Без карты · Отмена в любой момент", PaywallRows.FinePrintText(true, false));
    }

    [Test]
    public void FinePrint_subscribe_state_discloses_auto_renew_per_store()
    {
        // 2.3.10: the iOS build must never say «Google Play», and naming the wrong store
        // in a renewal disclosure is its own kind of wrong — so the pair is pinned whole.
        Assert.AreEqual("Продлевается автоматически · отмена в настройках App Store",
            PaywallRows.FinePrintText(false, true));
        Assert.AreEqual("Продлевается автоматически · отмена в настройках Google Play",
            PaywallRows.FinePrintText(false, false));
    }
}
