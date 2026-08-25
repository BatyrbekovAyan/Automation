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

    [TestCase("tier_start", PlanTier.Start)]
    [TestCase("tier_business", PlanTier.Business)]
    [TestCase("tier_network", PlanTier.Network)]
    [TestCase("garbage", PlanTier.None)]
    public void Entitlement_ids_map(string id, PlanTier expected)
        => Assert.AreEqual(expected, PlanCatalog.FromEntitlementId(id));
}
