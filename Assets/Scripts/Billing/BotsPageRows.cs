using System;

/// <summary>The «Боты» header trial pill: whether to show it, what it says, and its two colours.</summary>
public struct TrialPillRow
{
    public bool Visible;
    public string Text;     // «Пробный · 3 дн.»
    public ThemeRole Bg;
    public ThemeRole Ink;
}

/// <summary>
/// The «Боты» billing surface's ONE string/shape seam (spec §6): the header trial pill,
/// the account-level dialog meter strip above the list, and the «+ бот» card's
/// remaining-count subtext. <c>BotsPageBilling</c> renders exclusively from here.
///
/// Sibling of <see cref="PaywallRows"/> and <see cref="SubscriptionPageRows"/>, and
/// deliberately a THIN one: everything this screen shares with «Подписка» is DELEGATED
/// rather than re-derived — quota arithmetic to <see cref="QuotaMath"/>, the meter's
/// value line and bar colour to <see cref="SubscriptionPageRows"/>, numbers/plurals to
/// <see cref="PaywallCopy"/>/<see cref="RuPlural"/>, month names to
/// <see cref="RuDateFormat"/> (never the ambient culture, which follows the DEVICE
/// locale — RU-only-UI rule, CLAUDE.md). Two screens quoting one limit must never be
/// able to disagree about it.
///
/// Nothing here touches Unity, PlayerPrefs or the store, which is what keeps it testable.
/// </summary>
public static class BotsPageRows
{
    // ── Fixed copy ───────────────────────────────────────────────────────────

    /// <summary>Meter caption stem; the current month is appended by <see cref="MeterTitle"/>.</summary>
    public const string MeterTitleStem = "Диалоги ИИ";

    /// <summary>
    /// Shown once the ceiling (quota + top-up) is spent. It names the CONSEQUENCE rather
    /// than the number, because at that point the number is the same every time and what
    /// the owner needs to know is that the bot has not gone silent — it has fallen back
    /// to the «Вместе» suggestions panel, which costs no metered dialog.
    /// </summary>
    public const string OverHint = "Лимит исчерпан — бот отвечает в режиме «Вместе»";

    public const string AddBotTitle = "Добавить бота";
    public const string BotLimitSubtext = "Лимит ботов тарифа";

    /// <summary>At or below this many days left the pill switches to the urgent tint.</summary>
    public const int UrgentDaysLeft = 1;

    // ── Trial pill ───────────────────────────────────────────────────────────

    /// <summary>
    /// The header pill, shown ONLY on a live trial whose clock has actually started.
    /// Before the first channel auth the tier is still Trial (pre-auth grace, spec §3)
    /// but <see cref="TrialLedger.DaysLeft"/> would report the full <see cref="PlanCatalog.TrialDays"/>
    /// indefinitely — advertising a countdown that is not counting is worse than no pill.
    ///
    /// Colours: <see cref="ThemeRole"/> carries no «warning» token, and roles may only ever
    /// be APPENDED (ThemedColor serialises the ordinal). The amber
    /// <see cref="ThemeRole.StatusOwnerNeeded"/> that 14b uses for the near-full BAR is a fill,
    /// judged against a track; as INK on <see cref="ThemeRole.AccentSoft"/> it measures 2.99:1
    /// in the light theme — under the 3:1 floor. The urgent pill therefore uses the
    /// destructive pair, which clears 5:1 in BOTH themes (dark #F2555A on #2A1719 = 5.03:1,
    /// light #A01B12 on #FFCED5 = 5.66:1) and reads correctly for «the trial ends today».
    /// </summary>
    public static TrialPillRow TrialPill(PlanTier tier, bool trialStarted, int daysLeft)
    {
        if (tier != PlanTier.Trial || !trialStarted)
            return new TrialPillRow { Visible = false };

        int days = Math.Max(0, daysLeft);
        bool urgent = days <= UrgentDaysLeft;

        return new TrialPillRow
        {
            Visible = true,
            Text = PaywallCopy.TrialPill(days),
            Bg = urgent ? ThemeRole.DestructiveSoft : ThemeRole.AccentSoft,
            Ink = urgent ? ThemeRole.Destructive : ThemeRole.AccentText,
        };
    }

    // ── Meter strip ──────────────────────────────────────────────────────────

    /// <summary>
    /// «Диалоги ИИ · август». Nominative, because the month is a label here rather than
    /// a date being pointed at (<see cref="RuDateFormat.MonthGenitive"/> is the form that
    /// follows a day number, as on the «Подписка» renewal line).
    /// </summary>
    public static string MeterTitle(DateTime localNow)
        => MeterTitleStem + " · " + RuDateFormat.MonthNominative(localNow.Month);

    /// <summary>
    /// The second caption line, or <c>null</c> while there is nothing worth saying.
    /// Ok → null · Warn → «Осталось 86 — докупить 500 за 3 900 ₸» · Over → <see cref="OverHint"/>.
    ///
    /// The verb agrees with the bare number («Остался 1» / «Осталось 2» / «Осталось 86»),
    /// which is why it goes through <see cref="RuPlural"/> instead of a hand-rolled ternary —
    /// 11..14 take the "many" form despite ending in 1..4.
    /// </summary>
    public static string MeterHint(int used, int quota, int topup)
    {
        QuotaState state = QuotaMath.State(used, quota, topup);
        if (state == QuotaState.Ok) return null;
        if (state == QuotaState.Over) return OverHint;

        int left = QuotaMath.Remaining(used, quota, topup);
        return RuPlural.Pick(left, "Остался", "Осталось", "Осталось")
             + " " + PaywallCopy.Number(left)
             + " — докупить " + PaywallCopy.Number(PlanCatalog.TopUpDialogs)
             + " за " + PaywallCopy.Kzt(PlanCatalog.TopUpPriceKzt);
    }

    /// <summary>
    /// Ink for the hint line. Warn stays <see cref="ThemeRole.InkSecondary"/> — the amber
    /// <see cref="ThemeRole.StatusOwnerNeeded"/> already carries that state on the BAR (where
    /// it is a fill judged against a track), and as small INK on Surface it measures 3.39:1
    /// in the light theme, under the floor for a 30-unit caption. Over gets
    /// <see cref="ThemeRole.Destructive"/>, which clears it in both themes (dark 5.06:1,
    /// light 7.90:1) and is the one state where the line reports a wall rather than an offer.
    /// </summary>
    public static ThemeRole HintRole(QuotaState state)
        => state == QuotaState.Over ? ThemeRole.Destructive : ThemeRole.InkSecondary;

    /// <summary>
    /// The strip is hidden without a plan (delegated to
    /// <see cref="SubscriptionPageRows.MetersVisible"/> — <see cref="PlanTier.None"/> has no
    /// allowance to measure against) and before the first bot exists, where the EmptyState
    /// owns the screen and an account dialog meter is noise over it.
    /// </summary>
    public static bool MeterVisible(PlanTier effectiveTier, int liveBots)
        => SubscriptionPageRows.MetersVisible(effectiveTier) && liveBots > 0;

    // ── «+ бот» card ─────────────────────────────────────────────────────────

    /// <summary>
    /// «Ещё 2 бота в тарифе», or <see cref="BotLimitSubtext"/> once the plan is full.
    ///
    /// The count comes from <see cref="EntitlementPolicy.RemainingBots"/> — the same
    /// expression <see cref="EntitlementPolicy.CanCreateBot"/> is built on — rather than
    /// being re-derived here, so the card's limit STATE and the gate's refusal are one fact.
    /// (That seam also owns the zero clamp: a downgrade leaves the bots in place while the
    /// allowance shrinks, and the remainder must never count backwards.)
    /// </summary>
    public static string AddBotSubtext(PlanTier effectiveTier, int liveBots)
    {
        int remaining = EntitlementPolicy.RemainingBots(effectiveTier, liveBots);
        return remaining == 0
            ? BotLimitSubtext
            : "Ещё " + PaywallCopy.Bots(remaining) + " в тарифе";
    }
}
