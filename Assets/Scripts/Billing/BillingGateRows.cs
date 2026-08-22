/// <summary>
/// The limit gate sheet's ONE string/shape seam (spec §6 «Гейты в местах действия»).
/// <see cref="BillingGateSheet"/> renders exclusively from here, so every plural form
/// and every quoted allowance is pinned by EditMode tests instead of being typed into
/// a runtime builder where nothing can check it.
///
/// Sibling of <see cref="PaywallRows"/> / <see cref="SubscriptionPageRows"/> /
/// <see cref="BotsPageRows"/>, and deliberately the THINNEST of them: tier names,
/// numbers and RU plural agreement are DELEGATED to <see cref="PaywallCopy"/>, and the
/// allowances themselves to <see cref="PlanCatalog"/>. The sheet quoting a limit the
/// gate no longer enforces is the one failure this seam exists to prevent — which is
/// why nothing below is a literal count.
///
/// Nothing here touches Unity, PlayerPrefs or the store, which is what keeps it testable.
/// </summary>
public static class BillingGateRows
{
    // ── Interception ─────────────────────────────────────────────────────────

    /// <summary>
    /// Which triggers get the lightweight sheet BEFORE the full paywall.
    ///
    /// Only the two ceiling moments do. <see cref="PaywallTrigger.Browse"/> is the owner
    /// asking to see the plans (a sheet in front of it would be a door in front of a
    /// door) and <see cref="PaywallTrigger.TrialExpired"/> carries the день-5 «чек
    /// ценности», whose whole argument is the full-screen receipt — a one-line sheet
    /// would spend that moment on nothing.
    ///
    /// <see cref="PaywallController"/> is the single place this is consulted, so the
    /// paywall and the sheet can never both open on one request.
    /// </summary>
    public static bool ShouldInterceptWithSheet(PaywallTrigger trigger)
        => trigger == PaywallTrigger.BotLimit || trigger == PaywallTrigger.ChannelLimit;

    // ── Fixed copy ───────────────────────────────────────────────────────────

    public const string BotLimitTitle = "Лимит ботов вашего тарифа";
    public const string ChannelLimitTitle = "Лимит каналов вашего тарифа";

    /// <summary>«Оформите тариф, чтобы …» — the <see cref="PlanTier.None"/> lead-in.</summary>
    public const string NoSubscriptionLead = "Подписка не оформлена.";

    public const string PrimaryCtaText = "Посмотреть тарифы";
    public const string SecondaryCtaText = "Позже";

    // ── Rows ─────────────────────────────────────────────────────────────────

    public static string Title(PaywallTrigger trigger)
        => IsChannel(trigger) ? ChannelLimitTitle : BotLimitTitle;

    /// <summary>
    /// The one-line explanation under the title: what the current plan allows, and what
    /// upgrading buys. Three shapes, because the three states are genuinely different
    /// sentences rather than one sentence with a variable in it:
    ///
    ///  • a bought tier  — «В тарифе «Старт» — 1 бот. Повысьте тариф, чтобы добавить ещё.»
    ///  • the trial      — «В пробном периоде — 3 бота. …» (it is not a tariff the owner chose)
    ///  • <see cref="PlanTier.None"/> — «Подписка не оформлена. Оформите тариф, чтобы добавить бота.»
    ///    The catalog reports 0/0 there, so the tier shape would read «— 0 ботов», which
    ///    sounds like a broken plan rather than the absence of one.
    /// </summary>
    public static string Body(PaywallTrigger trigger, PlanTier tier)
    {
        bool channel = IsChannel(trigger);

        if (tier == PlanTier.None)
            return NoSubscriptionLead + " Оформите тариф, чтобы "
                 + (channel ? "подключить канал." : "добавить бота.");

        PlanSpec spec = PlanCatalog.Get(tier);
        string allowance = channel
            ? PaywallCopy.Channels(spec.MaxChannels)
            : PaywallCopy.Bots(spec.MaxBots);

        string lead = tier == PlanTier.Trial
            ? "В пробном периоде — "
            : "В тарифе «" + PaywallCopy.TierName(tier) + "» — ";

        return lead + allowance + ". Повысьте тариф, чтобы "
             + (channel ? "подключить ещё." : "добавить ещё.");
    }

    public static string PrimaryCta() => PrimaryCtaText;

    public static string SecondaryCta() => SecondaryCtaText;

    /// <summary>
    /// Only <see cref="PaywallTrigger.ChannelLimit"/> speaks about channels. Every other
    /// trigger falls to the bot shape — unreachable through the sheet (which is gated by
    /// <see cref="ShouldInterceptWithSheet"/>), but a defined answer beats an exception if
    /// a future caller renders these strings somewhere else.
    /// </summary>
    private static bool IsChannel(PaywallTrigger trigger) => trigger == PaywallTrigger.ChannelLimit;
}
