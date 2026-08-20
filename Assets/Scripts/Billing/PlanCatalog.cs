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
            case PlanTier.Start:    return new PlanSpec { Tier = tier, MaxBots = 1, MaxChannels = 1, DialogQuota = 300,  PriceMonthKzt = 9900,  PriceYearKzt = 99000,  SkuMonth = "sub.start.month",    SkuYear = "sub.start.year" };
            case PlanTier.Business: return new PlanSpec { Tier = tier, MaxBots = 3, MaxChannels = 3, DialogQuota = 1000, PriceMonthKzt = 19900, PriceYearKzt = 199000, SkuMonth = "sub.business.month", SkuYear = "sub.business.year" };
            case PlanTier.Network:  return new PlanSpec { Tier = tier, MaxBots = 5, MaxChannels = 5, DialogQuota = 3000, PriceMonthKzt = 39900, PriceYearKzt = 399000, SkuMonth = "sub.network.month",  SkuYear = "sub.network.year" };
            default:                return new PlanSpec { Tier = PlanTier.None };
        }
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
