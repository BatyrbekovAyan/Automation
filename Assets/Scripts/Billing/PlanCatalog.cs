public enum PlanTier { None, Trial, Start, Business, Network }

public struct PlanSpec
{
    public PlanTier Tier;
    public int MaxBots, MaxChannels, DialogQuota, PriceMonthKzt, PriceYearKzt;
    public string SkuMonth, SkuYear;
}

public static class PlanCatalog
{
    public const int TrialDays = 5;
    public const int TrialDialogCap = 150;
    public const int TopUpDialogs = 500;
    public const int TopUpPriceKzt = 3900;
    public const string SkuTopUp = "topup.dialogs.500";
    public const int WarnThresholdPercent = 80;

    public static PlanSpec Get(PlanTier tier)
    {
        switch (tier)
        {
            case PlanTier.Trial:    return new PlanSpec { Tier = tier, MaxBots = 3, MaxChannels = 3, DialogQuota = TrialDialogCap };
            case PlanTier.Start:    return new PlanSpec { Tier = tier, MaxBots = 1, MaxChannels = 1, DialogQuota = 300,  PriceMonthKzt = 9990,  PriceYearKzt = 99000,  SkuMonth = "sub.start.month",    SkuYear = "sub.start.year" };
            case PlanTier.Business: return new PlanSpec { Tier = tier, MaxBots = 3, MaxChannels = 3, DialogQuota = 1000, PriceMonthKzt = 19990, PriceYearKzt = 198990, SkuMonth = "sub.business.month", SkuYear = "sub.business.year" };
            case PlanTier.Network:  return new PlanSpec { Tier = tier, MaxBots = 5, MaxChannels = 5, DialogQuota = 3000, PriceMonthKzt = 39900, PriceYearKzt = 399990, SkuMonth = "sub.network.month",  SkuYear = "sub.network.year" };
            default:                return new PlanSpec { Tier = PlanTier.None };
        }
    }

    /// <summary>
    /// Every store product id the app sells (6 subscriptions + the consumable top-up) —
    /// the exact set the localized-price fetch asks the store for.
    /// </summary>
    public static string[] AllSkus()
    {
        PlanSpec start = Get(PlanTier.Start), business = Get(PlanTier.Business), network = Get(PlanTier.Network);
        return new[]
        {
            start.SkuMonth, start.SkuYear,
            business.SkuMonth, business.SkuYear,
            network.SkuMonth, network.SkuYear,
            SkuTopUp,
        };
    }

    /// <summary>
    /// The six subscription product ids — <see cref="AllSkus"/> minus the consumable top-up.
    /// Google Play Billing answers a product query per TYPE («subs» never returns a one-time
    /// product, «inapp» never returns a subscription), so the Android price fetch asks for
    /// these under «subs» and for <see cref="SkuTopUp"/> under «inapp». StoreKit ignores the
    /// type filter, which is why iOS still asks for <see cref="AllSkus"/> in one call.
    /// </summary>
    public static string[] SubscriptionSkus()
    {
        string[] all = AllSkus();
        var subs = new string[all.Length - 1];
        int n = 0;
        foreach (string sku in all)
            if (sku != SkuTopUp) subs[n++] = sku;
        return subs;
    }

    public static PlanTier FromEntitlementId(string id)
    {
        switch (id)
        {
            case "tier_start": return PlanTier.Start;
            case "tier_business": return PlanTier.Business;
            case "tier_network": return PlanTier.Network;
            default: return PlanTier.None;
        }
    }
}
