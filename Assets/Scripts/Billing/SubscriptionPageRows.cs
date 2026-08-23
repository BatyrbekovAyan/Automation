using System;
using System.Globalization;

/// <summary>Which of the three states the «Подписка» plan card is showing.</summary>
public enum SubscriptionState { Active, Trial, Expired }

/// <summary>The plan card's whole headline — title, status pill and subline.</summary>
public struct SubscriptionStatusLine
{
    public SubscriptionState State;
    public string Title;        // «Бизнес» / «Пробный» / «Без подписки»
    public string PillText;     // «Активна» / «Пробный» / «Истекла»
    public ThemeRole PillBg;
    public ThemeRole PillInk;
    public string Subline;      // «19 900 ₸/мес · продлится 26 августа»
}

/// <summary>One quota meter's label plus the state that colours its bar.</summary>
public struct SubscriptionUsageLine
{
    public string Text;         // «412 из 1 000»
    public QuotaState State;
}

/// <summary>
/// The «Подписка» page's ONE string/shape seam (Профиль → «Подписка», spec §6).
/// <c>ProfileSubPages.Subscription</c> renders exclusively from here — the sibling
/// of <see cref="PaywallRows"/> for the same reason: every price, plural, date and
/// state decision is pinned by EditMode tests instead of being hand-typed into the
/// scene where nothing can check it.
///
/// Numbers/words come from <see cref="PaywallCopy"/> (NBSP grouping + RU plural
/// agreement), limits from <see cref="PlanCatalog"/>, quota arithmetic from
/// <see cref="QuotaMath"/> and dates from <see cref="RuDateFormat"/> — never from
/// the ambient culture, which follows the DEVICE locale (RU-only-UI rule, CLAUDE.md).
/// Nothing here touches Unity, PlayerPrefs or the store, which is what keeps it testable.
/// </summary>
public static class SubscriptionPageRows
{
    // ── Fixed copy ───────────────────────────────────────────────────────────

    public const string PageTitle = "Подписка";

    public const string PlanCaption = "ВАШ ТАРИФ";
    public const string ActionsCaption = "УПРАВЛЕНИЕ";

    // The pill sits ON the plan card, which is Surface — so a Surface pill is the same RGB
    // as its own background in BOTH themes and simply disappears. Hairline is the one
    // neutral that steps off Surface in both (dark #242C38 on #171C24, light on white).
    public const string PillActive = "Активна";
    public const string PillTrial = "Пробный";
    public const string PillExpired = "Истекла";

    public const string NoPlanTitle = "Без подписки";
    public const string NoSubscriptionSubline = "Подписка не оформлена";

    public const string DialogsTitle = "Диалоги ИИ";
    public const string BotsTitle = "Боты";
    public const string ChannelsTitle = "Каналы";

    public const string ChangePlanRow = "Изменить тариф";
    public const string RestoreRow = "Восстановить покупки";
    public const string CancelRow = "Отменить подписку";
    public const string CancelCaption = "Управление подпиской — в настройках App Store / Google Play";

    /// <summary>
    /// Wording still pending the owner's sign-off (same status as the paywall's
    /// notices in <see cref="PaywallRows"/>) — pinned here so a reword is one edit.
    /// </summary>
    public const string TopUpDoneNotice = "Диалоги начислены";
    public const string TopUpFailedNotice = "Не удалось купить диалоги. Попробуйте ещё раз.";

    // ── Seams ────────────────────────────────────────────────────────────────

    /// <summary>
    /// UTC→device-local conversion for the renewal date. A seam (house pattern:
    /// TrialLedger.UtcNow / UsageStore) because the rendered DAY would otherwise
    /// depend on the test machine's timezone.
    /// </summary>
    internal static Func<DateTime, DateTime> LocalizeUtc = DefaultLocalize;

    private static DateTime DefaultLocalize(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

    internal static void ResetSeamsForTests() => LocalizeUtc = DefaultLocalize;

    // ── Plan card ────────────────────────────────────────────────────────────

    /// <summary>
    /// A purchase always wins; otherwise a trial with days on the clock is a trial
    /// (including the pre-auth grace, where the clock has never STARTED — see
    /// EntitlementPolicy.EffectiveTier), and everything else is spent.
    ///
    /// «Has the clock started» is deliberately NOT a parameter: TrialLedger.DaysLeft()
    /// already returns the full TrialDays before the first auth, so the day count is the
    /// only thing this decision needs — and taking the flag anyway would look like it
    /// mattered when no branch could ever read it.
    /// </summary>
    public static SubscriptionState State(PlanTier purchased, int trialDaysLeft)
    {
        if (purchased != PlanTier.None) return SubscriptionState.Active;
        return trialDaysLeft > 0 ? SubscriptionState.Trial : SubscriptionState.Expired;
    }

    /// <param name="interval">
    /// <see cref="UsageSnapshot.interval"/> — «month», «year», or null/anything else for
    /// «period unknown». Optional so the 20-odd existing call sites that have no snapshot to
    /// hand (and every test written before Task 15a) keep asserting the KNOWN DEFAULT, monthly.
    /// </param>
    public static SubscriptionStatusLine StatusLine(PlanTier purchased, int trialDaysLeft,
        string periodEndIso, string interval = null)
    {
        SubscriptionState state = State(purchased, trialDaysLeft);
        switch (state)
        {
            case SubscriptionState.Active:
                return new SubscriptionStatusLine
                {
                    State = state,
                    Title = PaywallCopy.TierName(purchased),
                    PillText = PillActive,
                    PillBg = ThemeRole.PositiveBg,
                    PillInk = ThemeRole.PositiveInk,
                    Subline = ActiveSubline(purchased, periodEndIso, interval),
                };

            case SubscriptionState.Trial:
                return new SubscriptionStatusLine
                {
                    State = state,
                    Title = PaywallCopy.TierName(PlanTier.Trial),
                    PillText = PillTrial,
                    PillBg = ThemeRole.Hairline,
                    PillInk = ThemeRole.InkTertiary,
                    Subline = TrialSubline(trialDaysLeft),
                };

            default:
                return new SubscriptionStatusLine
                {
                    State = SubscriptionState.Expired,
                    Title = NoPlanTitle,
                    PillText = PillExpired,
                    PillBg = ThemeRole.Hairline,
                    PillInk = ThemeRole.InkTertiary,
                    Subline = NoSubscriptionSubline,
                };
        }
    }

    /// <summary>Wire values of <see cref="UsageSnapshot.interval"/> (Get Usage, Task 15a).</summary>
    public const string IntervalMonth = "month";
    public const string IntervalYear = "year";

    /// <summary>
    /// «19 900 ₸/мес» (or «199 000 ₸/год» on an annual subscription) plus «· продлится
    /// 26 августа» when the server gave us a period end.
    ///
    /// <paramref name="interval"/> is the Get Usage snapshot's own field, derived there from
    /// the purchased SKU's <c>.month</c>/<c>.year</c> suffix. Anything that is not exactly
    /// <see cref="IntervalYear"/> — null, empty, an unrecognised value, or a payload from a
    /// deployment older than Task 15a — renders the MONTHLY line: that is the known default
    /// and the safe one, because the annual figure is ten times larger and quoting it at a
    /// monthly subscriber would be a straight lie about what they are paying.
    /// </summary>
    public static string ActiveSubline(PlanTier purchased, string periodEndIso, string interval = null)
    {
        PlanSpec spec = PlanCatalog.Get(purchased);
        string price = interval == IntervalYear
            ? PaywallCopy.PerYear(spec.PriceYearKzt)
            : PaywallCopy.PerMonth(spec.PriceMonthKzt);
        DateTime? end = ParsePeriodEnd(periodEndIso);
        return end.HasValue ? price + " · продлится " + RuDateFormat.DayMonth(end.Value) : price;
    }

    /// <summary>«Пробный · осталось 3 дн.» — «дн.» is an abbreviation, so no plural agreement.</summary>
    public static string TrialSubline(int daysLeft)
        => PillTrial + " · осталось "
         + Math.Max(0, daysLeft).ToString(CultureInfo.InvariantCulture) + " дн.";

    /// <summary>
    /// Parses the GetUsage payload's <c>periodEnd</c> (ISO-8601 or null). Returns null on
    /// anything unusable rather than throwing — a bad timestamp must cost the renewal clause,
    /// never the whole card.
    /// </summary>
    public static DateTime? ParsePeriodEnd(string iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        // Normalise every wire form to UTC before the seam sees it, so the seam alone decides
        // localisation. AdjustToUniversal converts an offset form ("+05:00") that would
        // otherwise land in the PARSING machine's local time; AssumeUniversal covers a naked
        // timestamp with no offset at all (a Postgres timestamptz is UTC, and "assume local"
        // would make the rendered day follow the device). Note RoundtripKind is ILLEGAL
        // alongside these two — DateTime.TryParse throws ArgumentException, it does not just
        // ignore it.
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
            return null;

        return LocalizeUtc(parsed);
    }

    // ── Meters ───────────────────────────────────────────────────────────────

    /// <summary>
    /// «412 из 1 000». A purchased top-up genuinely raises the ceiling, so it extends the
    /// denominator as well as the state — showing «1 000 из 1 000» to someone who just
    /// bought 500 more would read as a wall that isn't there.
    /// </summary>
    public static SubscriptionUsageLine UsageLine(int used, int quota, int topup)
        => new SubscriptionUsageLine
        {
            Text = CountLine(used, quota + topup),
            State = QuotaMath.State(used, quota, topup),
        };

    /// <summary>
    /// Before the first GetUsage read lands there is no honest numerator: «— из 1 000»,
    /// never «0 из 1 000» — a real zero is a very different statement (same rule as
    /// <see cref="PaywallRows.StatValue"/>). The bar reads Ok so nothing is coloured
    /// as an alarm on a number we do not have.
    /// </summary>
    public static SubscriptionUsageLine UnknownUsageLine(int quota)
        => new SubscriptionUsageLine
        {
            Text = PaywallRows.StatUnknown + " из " + PaywallCopy.Number(quota),
            State = QuotaState.Ok,
        };

    /// <summary>«2 из 3» — the shared "N of M" shape for the bots/channels rows too.</summary>
    public static string CountLine(int current, int max)
        => PaywallCopy.Number(current) + " из " + PaywallCopy.Number(max);

    /// <summary>
    /// 0..1 bar fill. A ceiling of zero reads EMPTY, never full: «no plan» is not «quota
    /// exhausted», and a full red bar there would invent an alarm out of a missing number
    /// (the meters are hidden outright in that state — see <see cref="MetersVisible"/>).
    /// </summary>
    public static float FillFraction(int used, int quota, int topup)
    {
        int ceiling = quota + topup;
        if (ceiling <= 0) return 0f;
        if (used <= 0) return 0f;
        return used >= ceiling ? 1f : used / (float)ceiling;
    }

    /// <summary>
    /// Bar colour by quota state. ThemeRole has no dedicated «warning» role, and roles may
    /// only ever be APPENDED (ThemedColor serialises the ordinal), so the amber
    /// StatusOwnerNeeded (#E46602 dark / spec's «#F8942F-class») carries the Warn step —
    /// semantically «нужно внимание владельца», which is precisely what a near-full quota is.
    /// </summary>
    public static ThemeRole FillRole(QuotaState state)
    {
        switch (state)
        {
            case QuotaState.Warn: return ThemeRole.StatusOwnerNeeded;
            case QuotaState.Over: return ThemeRole.Destructive;
            default: return ThemeRole.AccentFill;
        }
    }

    /// <summary>
    /// PlanTier.None has no allowances at all (PlanCatalog.Get(None) is 0 bots / 0 channels /
    /// 0 dialogs), so a meter block would read «2 из 0» — a limit that is not a limit but a
    /// missing plan. In that state the card's «Подписка не оформлена» and the actions below
    /// carry the page instead.
    /// </summary>
    public static bool MetersVisible(PlanTier effectiveTier) => effectiveTier != PlanTier.None;

    // ── Actions ──────────────────────────────────────────────────────────────

    /// <summary>«Купить 500 диалогов — 3 900 ₸».</summary>
    public static string TopUpRowText()
        => "Купить " + PaywallCopy.Dialogs(PlanCatalog.TopUpDialogs)
         + " — " + PaywallCopy.Kzt(PlanCatalog.TopUpPriceKzt);

    /// <summary>
    /// The store's cancel deep-link only makes sense once something was actually bought —
    /// a trial takes no card, so there is nothing there to cancel.
    /// </summary>
    public static bool CancelVisible(PlanTier purchased)
        => purchased == PlanTier.Start || purchased == PlanTier.Business || purchased == PlanTier.Network;
}
