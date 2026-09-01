using NUnit.Framework;

public class PlanCatalogTests
{
    [TestCase(PlanTier.Start, 1, 1, 300, 9990, 99000)]
    [TestCase(PlanTier.Business, 3, 3, 1000, 19990, 198990)]
    [TestCase(PlanTier.Network, 5, 5, 3000, 39900, 399990)]
    public void Paid_tiers_match_spec(PlanTier t, int bots, int ch, int quota, int m, int y)
    {
        var p = PlanCatalog.Get(t);
        Assert.AreEqual(bots, p.MaxBots); Assert.AreEqual(ch, p.MaxChannels);
        Assert.AreEqual(quota, p.DialogQuota);
        Assert.AreEqual(m, p.PriceMonthKzt); Assert.AreEqual(y, p.PriceYearKzt);
    }

    [Test] public void Trial_is_business_shaped_with_150_cap()
    {
        var p = PlanCatalog.Get(PlanTier.Trial);
        Assert.AreEqual(3, p.MaxBots); Assert.AreEqual(3, p.MaxChannels);
        Assert.AreEqual(150, p.DialogQuota); Assert.AreEqual(0, p.PriceMonthKzt);
    }

    [Test] public void None_allows_nothing()
    {
        var p = PlanCatalog.Get(PlanTier.None);
        Assert.AreEqual(0, p.MaxBots); Assert.AreEqual(0, p.MaxChannels); Assert.AreEqual(0, p.DialogQuota);
    }

    // Дрейф SKU топ-апа ловим здесь (M-4): этот литерал ДУБЛИРУЕТСЯ в n8n RC_Events
    // (нода «Map Event» опознаёт по нему покупку резерва и начисляет 500 диалогов), а
    // подписочные SKU уже закреплены в PaywallRowsTests.Sku_for_selection. Переименование
    // на любой из двух сторон должно падать тестом, а не молча ломать начисление.
    [Test] public void Top_up_sku_and_pack_match_the_store_and_the_webhook()
    {
        Assert.AreEqual("topup.dialogs.500", PlanCatalog.SkuTopUp);
        Assert.AreEqual(500, PlanCatalog.TopUpDialogs);
        Assert.AreEqual(3900, PlanCatalog.TopUpPriceKzt);
    }

    [TestCase("tier_start", PlanTier.Start)]
    [TestCase("tier_business", PlanTier.Business)]
    [TestCase("tier_network", PlanTier.Network)]
    [TestCase("garbage", PlanTier.None)]
    public void Entitlement_ids_map(string id, PlanTier expected)
        => Assert.AreEqual(expected, PlanCatalog.FromEntitlementId(id));

    /// <summary>The localized-price fetch asks the store for EXACTLY the sellable set.</summary>
    [Test]
    public void All_skus_cover_six_subscriptions_and_the_topup()
        => CollectionAssert.AreEqual(new[]
        {
            "sub.start.month", "sub.start.year",
            "sub.business.month", "sub.business.year",
            "sub.network.month", "sub.network.year",
            "topup.dialogs.500",
        }, PlanCatalog.AllSkus());
}
