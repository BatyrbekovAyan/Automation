using NUnit.Framework;

public class EntitlementPolicyTests
{
    [Test] public void Purchase_beats_trial()
        => Assert.AreEqual(PlanTier.Start, EntitlementPolicy.EffectiveTier(PlanTier.Start, true, true, false));

    [Test] public void Active_trial_when_nothing_purchased()
        => Assert.AreEqual(PlanTier.Trial, EntitlementPolicy.EffectiveTier(PlanTier.None, true, false, false));

    [Test] public void Expired_trial_without_purchase_is_none()
        => Assert.AreEqual(PlanTier.None, EntitlementPolicy.EffectiveTier(PlanTier.None, true, true, false));

    [Test] public void Not_started_trial_is_trial_grace()   // мастер первого бота должен открываться до первой авторизации
        => Assert.AreEqual(PlanTier.Trial, EntitlementPolicy.EffectiveTier(PlanTier.None, false, false, false));

    // ── Task 19: слово сервера против стёртого леджера ────────────────────────

    /// <summary>
    /// САМ инцидент 2026-08-26: переустановка стёрла триал-леджер, id RC выжил, сервер знает
    /// «expired». Раньше это давало Trial — мастер открывался, владелец авторизовал WhatsApp,
    /// и отказывал уже вебхук Create, оставив оплаченный профиль висеть на Wappi.
    /// </summary>
    [Test] public void Server_expired_beats_a_wiped_trial_ledger()
        => Assert.AreEqual(PlanTier.None, EntitlementPolicy.EffectiveTier(PlanTier.None, false, false, true));

    /// <summary>Покупка — по-прежнему первый и безусловный ответ: сервер её не отменяет.</summary>
    [TestCase(PlanTier.Start)]
    [TestCase(PlanTier.Business)]
    [TestCase(PlanTier.Network)]
    public void Server_expired_never_overrides_a_purchase(PlanTier purchased)
        => Assert.AreEqual(purchased, EntitlementPolicy.EffectiveTier(purchased, false, false, true));

    /// <summary>
    /// Уже стартовавший локальный триал живёт по своим часам — сервер не может отнять
    /// оставшиеся дни (в этом состоянии зеркало и так отдаёт «trialing»).
    /// </summary>
    [Test] public void Server_expired_does_not_cut_a_running_local_trial()
        => Assert.AreEqual(PlanTier.Trial, EntitlementPolicy.EffectiveTier(PlanTier.None, true, false, true));

    /// <summary>
    /// Fail-open: неизвестный/устаревший/неудачный снимок = false, и КАЖДОЕ плечо матрицы
    /// обязано совпасть с доTask19-поведением. Это тот самый гард, который не даёт свежей
    /// офлайн-установке потерять мастер первого бота.
    /// </summary>
    [Test] public void Unknown_server_status_changes_nothing()
    {
        foreach (PlanTier purchased in new[] { PlanTier.None, PlanTier.Trial, PlanTier.Start,
                                               PlanTier.Business, PlanTier.Network })
        foreach (bool started in new[] { false, true })
        foreach (bool expired in new[] { false, true })
        {
            PlanTier legacy = purchased != PlanTier.None
                ? purchased
                : (started && expired ? PlanTier.None : PlanTier.Trial);
            Assert.AreEqual(legacy, EntitlementPolicy.EffectiveTier(purchased, started, expired, false),
                $"purchased={purchased} started={started} expired={expired}");
        }
    }

    [TestCase(PlanTier.Start, 0, true)]
    [TestCase(PlanTier.Start, 1, false)]
    [TestCase(PlanTier.Business, 2, true)]
    [TestCase(PlanTier.Business, 3, false)]
    [TestCase(PlanTier.None, 0, false)]
    public void Bot_gate(PlanTier t, int existing, bool ok)
        => Assert.AreEqual(ok, EntitlementPolicy.CanCreateBot(t, existing));

    // RemainingBots is what the «+ бот» card renders and what CanCreateBot is built on, so
    // these cases also pin that the card and the gate can never disagree (Task 14d).
    [TestCase(PlanTier.Start, 0, 1)]
    [TestCase(PlanTier.Start, 1, 0)]
    [TestCase(PlanTier.Business, 0, 3)]
    [TestCase(PlanTier.Business, 2, 1)]
    [TestCase(PlanTier.Network, 5, 0)]
    [TestCase(PlanTier.Trial, 1, 2)]
    [TestCase(PlanTier.None, 0, 0)]
    public void Remaining_bots(PlanTier tier, int existing, int remaining)
        => Assert.AreEqual(remaining, EntitlementPolicy.RemainingBots(tier, existing));

    [TestCase(PlanTier.Start, 4)]
    [TestCase(PlanTier.Business, 9)]
    public void A_downgrade_never_counts_backwards(PlanTier tier, int existing)
    {
        // Over the allowance: the bots stay, the allowance shrank. «Ещё -3 бота» would be
        // both wrong and unpluralisable.
        Assert.AreEqual(0, EntitlementPolicy.RemainingBots(tier, existing));
        Assert.IsFalse(EntitlementPolicy.CanCreateBot(tier, existing));
    }

    [TestCase(PlanTier.None, 0)]
    [TestCase(PlanTier.Trial, 2)]
    [TestCase(PlanTier.Trial, 3)]
    [TestCase(PlanTier.Network, 4)]
    [TestCase(PlanTier.Network, 5)]
    public void The_bot_gate_is_exactly_remaining_greater_than_zero(PlanTier tier, int existing)
        => Assert.AreEqual(EntitlementPolicy.RemainingBots(tier, existing) > 0,
                           EntitlementPolicy.CanCreateBot(tier, existing));

    [TestCase(PlanTier.Network, 4, true)]
    [TestCase(PlanTier.Network, 5, false)]
    [TestCase(PlanTier.Trial, 2, true)]
    [TestCase(PlanTier.Trial, 3, false)]
    public void Channel_gate(PlanTier t, int connected, bool ok)
        => Assert.AreEqual(ok, EntitlementPolicy.CanConnectChannel(t, connected));
}
