public static class EntitlementPolicy
{
    public static PlanTier EffectiveTier(PlanTier purchased, bool trialStarted, bool trialExpired)
    {
        if (purchased != PlanTier.None) return purchased;
        // Не стартовавший триал = Trial (pre-auth grace): часы запускает первая авторизация,
        // а мастер первого бота обязан открываться на свежей установке.
        return trialStarted && trialExpired ? PlanTier.None : PlanTier.Trial;
    }

    public static bool CanCreateBot(PlanTier tier, int existingBots)
        => existingBots < PlanCatalog.Get(tier).MaxBots;

    public static bool CanConnectChannel(PlanTier tier, int connectedChannels)
        => connectedChannels < PlanCatalog.Get(tier).MaxChannels;
}
