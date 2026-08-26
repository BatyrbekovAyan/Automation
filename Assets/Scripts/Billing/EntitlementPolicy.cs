using System;

public static class EntitlementPolicy
{
    /// <param name="serverSaysExpired">
    /// <see cref="ServerAccountStatus.Expired"/> — зеркало подписок ОПРЕДЕЛЁННО отдало
    /// «expired» для этого app_user_id (Task 19). Бьёт только по одному состоянию: локальный
    /// триал НЕ стартовал, а сервер уже знает, что аккаунт истёк — то есть переустановка стёрла
    /// леджер, но не идентичность (живой инцидент 2026-08-26). Неизвестный/устаревший снимок —
    /// <c>false</c>, и тогда всё поведение прежнее.
    /// </param>
    public static PlanTier EffectiveTier(PlanTier purchased, bool trialStarted, bool trialExpired,
        bool serverSaysExpired)
    {
        if (purchased != PlanTier.None) return purchased;
        // Стартовавший триал живёт по СВОИМ часам: сервер не может ни продлить его, ни отнять
        // (в этом состоянии слово сервера ничего не добавляет — статус аккаунта и так «trialing»).
        if (trialStarted) return trialExpired ? PlanTier.None : PlanTier.Trial;
        // Не стартовавший триал = Trial (pre-auth grace): часы запускает первая авторизация,
        // а мастер первого бота обязан открываться на свежей установке — ЕСЛИ сервер не говорит
        // обратного. Иначе гейт отпускает владельца в мастер, он проходит реальную авторизацию
        // канала, и отказывает уже вебхук Create — оставив оплаченный Wappi-профиль висеть.
        return serverSaysExpired ? PlanTier.None : PlanTier.Trial;
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
