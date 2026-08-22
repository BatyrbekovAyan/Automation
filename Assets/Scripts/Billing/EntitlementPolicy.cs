using System;

public static class EntitlementPolicy
{
    public static PlanTier EffectiveTier(PlanTier purchased, bool trialStarted, bool trialExpired)
    {
        if (purchased != PlanTier.None) return purchased;
        // Не стартовавший триал = Trial (pre-auth grace): часы запускает первая авторизация,
        // а мастер первого бота обязан открываться на свежей установке.
        return trialStarted && trialExpired ? PlanTier.None : PlanTier.Trial;
    }

    /// <summary>
    /// Bot slots the plan still has. Clamped at zero because a DOWNGRADE leaves the existing
    /// bots in place while the allowance shrinks — the remainder must never count backwards.
    ///
    /// <see cref="CanCreateBot"/> is expressed in terms of this so the «+ бот» card's
    /// «Ещё N ботов в тарифе» / «Лимит ботов тарифа» state IS the gate's own predicate: the
    /// card can never advertise a slot the gate would refuse, or vice versa.
    /// </summary>
    public static int RemainingBots(PlanTier tier, int existingBots)
        => Math.Max(0, PlanCatalog.Get(tier).MaxBots - existingBots);

    public static bool CanCreateBot(PlanTier tier, int existingBots)
        => RemainingBots(tier, existingBots) > 0;

    public static bool CanConnectChannel(PlanTier tier, int connectedChannels)
        => connectedChannels < PlanCatalog.Get(tier).MaxChannels;
}
