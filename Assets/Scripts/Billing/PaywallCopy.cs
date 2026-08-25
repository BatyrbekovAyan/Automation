using System.Text;

public static class PaywallCopy
{
    const char Nbsp = '\u00A0';

    /// <summary>
    /// NBSP-grouped digits, sign-safe (the minus is emitted BEFORE grouping so it is
    /// never counted as a digit). Single source for every user-facing number in the
    /// billing UI — <see cref="Kzt"/> and <see cref="Dialogs"/> both go through it, so
    /// «19 900 ₸» and «1 000 диалогов» can never disagree about thousands separators.
    /// </summary>
    public static string Number(int amount)
    {
        var negative = amount < 0;
        var digits = System.Math.Abs((long)amount).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        if (negative) sb.Append('-');
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(Nbsp);
            sb.Append(digits[i]);
        }
        return sb.ToString();
    }

    public static string Kzt(int amount) => Number(amount) + Nbsp + "₸";

    public static string PerMonth(int amount) => Kzt(amount) + "/мес";

    public static string PerYear(int amount) => Kzt(amount) + "/год";

    public static string YearLine(PlanSpec p) => Kzt(p.PriceYearKzt) + "/год — выгода до 17%";

    /// <summary>Short «до −17%» badge for the Год half of the period toggle.</summary>
    public const string YearSavingBadge = "до \u221217%";

    public static string Dialogs(int n)
        => Number(n) + " " + RuPlural.Pick(n, "диалог", "диалога", "диалогов");

    /// <summary>Counts-line form: «300 диалогов ИИ/мес».</summary>
    public static string DialogsPerMonth(int n) => Dialogs(n) + " ИИ/мес";

    public static string Bots(int n)
        => Number(n) + " " + RuPlural.Pick(n, "бот", "бота", "ботов");

    public static string Channels(int n)
        => Number(n) + " " + RuPlural.Pick(n, "канал", "канала", "каналов");

    /// <summary>
    /// The tier's user-facing RU name. Also a logic-adjacent string (it lands inside
    /// <see cref="SubscribeCta"/>), so it lives here rather than being hand-typed at
    /// each call site — see the RU-only-UI convention in CLAUDE.md.
    /// </summary>
    public static string TierName(PlanTier tier)
    {
        switch (tier)
        {
            case PlanTier.Trial:    return "Пробный";
            case PlanTier.Start:    return "Старт";
            case PlanTier.Business: return "Бизнес";
            case PlanTier.Network:  return "Сеть";
            default:                return "";
        }
    }

    /// <summary>Paywall CTA once the trial is spent/purchased: «Оформить Бизнес — 19 900 ₸/мес».</summary>
    public static string SubscribeCta(PlanTier tier, string priceText)
        => "Оформить " + TierName(tier) + " — " + priceText;

    public static string TrialCta()
        => "Попробовать " + PlanCatalog.TrialDays.ToString(System.Globalization.CultureInfo.InvariantCulture)
         + " " + RuPlural.Pick(PlanCatalog.TrialDays, "день", "дня", "дней") + " бесплатно";

    public static string TrialPill(int daysLeft)
        => "Пробный · " + daysLeft.ToString(System.Globalization.CultureInfo.InvariantCulture) + " дн.";

    /// <summary>Value-receipt header (PaywallTrigger.TrialExpired): «Ваш бот за 5 дней».</summary>
    public static string ReceiptTitle()
        => "Ваш бот за " + PlanCatalog.TrialDays.ToString(System.Globalization.CultureInfo.InvariantCulture)
         + " " + RuPlural.Pick(PlanCatalog.TrialDays, "день", "дня", "дней");
}
