/// <summary>Which price column the paywall is showing.</summary>
public enum PaywallPeriod { Month, Year }

/// <summary>One rendered tier card's worth of state — everything the view needs, nothing it doesn't.</summary>
public struct PaywallTierRow
{
    public PlanTier Tier;
    public string Title;            // «Бизнес»
    public string PriceText;        // «19 900 ₸/мес» / «199 000 ₸/год»
    public string CountsLine;       // «3 бота · 3 канала · 1 000 диалогов ИИ/мес»
    public bool ShowCrossBotLine;   // «Сводка по всем ботам» — Бизнес/Сеть only
    public bool IsHighlighted;      // ★Популярный + accent ring
}

/// <summary>
/// The paywall's ONE string/shape seam. <see cref="PaywallController"/> renders exclusively
/// from here, so every price, plural and CTA form is pinned by EditMode tests instead of
/// being hand-typed into the scene where nothing can check it.
///
/// Numbers/words come from <see cref="PaywallCopy"/> (which owns NBSP grouping and RU plural
/// agreement) and limits come from <see cref="PlanCatalog"/> (which owns the tariff matrix) —
/// this type only decides WHICH of them a row carries. Nothing here touches Unity, PlayerPrefs
/// or the store, which is what keeps it testable.
/// </summary>
public static class PaywallRows
{
    /// <summary>Card order, left/top to right/bottom. Trial is never a purchasable card.</summary>
    public static readonly PlanTier[] Order = { PlanTier.Start, PlanTier.Business, PlanTier.Network };

    /// <summary>Pre-selected + ringed + badged tier (spec §2 «Бизнес ★Популярный»).</summary>
    public const PlanTier Recommended = PlanTier.Business;

    public const string HeaderTitle = "Все возможности — в каждом тарифе";
    public const string HeaderSubline = "Платите только за масштаб";
    public const string ReceiptSubline = "Итоги пробного периода";

    public const string PopularBadge = "Популярный";
    public const string CrossBotLine = "Сводка по всем ботам";
    public const string AllPlansOverline = "ВО ВСЕХ ТАРИФАХ";
    public const string FinePrint = "Без карты · Отмена в любой момент";
    public const string RestoreLabel = "Восстановить покупки";

    public const string PeriodMonth = "Месяц";
    public const string PeriodYear = "Год";

    /// <summary>Shown instead of a number whenever the source metric is not reachable yet.</summary>
    public const string StatUnknown = "—";

    /// <summary>
    /// Spec §2 «Во всех тарифах», verbatim and in order. The launch rule («пейволл не
    /// публикуется, пока каждая строка не работает») is checked against THIS list, so it
    /// must stay the single place the promise is written down.
    /// </summary>
    public static readonly string[] AllPlansFeatures =
    {
        "Умный ИИ последнего поколения",
        "Понимает голосовые сообщения",
        "Прайс-листы без лимита — файлы и фото",
        "Режимы «Авто» и «Вместе»",
        "Сводка за всё время + экспорт",
        "Алерты: «клиент готов купить», «канал отключился»",
        "Недельный отчёт в Telegram",
        "Расписание работы бота · докупка диалогов",
    };

    // ── Rows ─────────────────────────────────────────────────────────────────

    public static PaywallTierRow[] Build(PaywallPeriod period)
    {
        var rows = new PaywallTierRow[Order.Length];
        for (int i = 0; i < Order.Length; i++)
            rows[i] = Build(Order[i], period);
        return rows;
    }

    public static PaywallTierRow Build(PlanTier tier, PaywallPeriod period)
    {
        PlanSpec spec = PlanCatalog.Get(tier);
        return new PaywallTierRow
        {
            Tier = tier,
            Title = PaywallCopy.TierName(tier),
            PriceText = PriceText(spec, period),
            CountsLine = CountsLine(spec),
            ShowCrossBotLine = HasCrossBotSummary(tier),
            IsHighlighted = tier == Recommended,
        };
    }

    public static string PriceText(PlanSpec spec, PaywallPeriod period)
        => period == PaywallPeriod.Year
            ? PaywallCopy.PerYear(spec.PriceYearKzt)
            : PaywallCopy.PerMonth(spec.PriceMonthKzt);

    public static string CountsLine(PlanSpec spec)
        => PaywallCopy.Bots(spec.MaxBots)
         + " · " + PaywallCopy.Channels(spec.MaxChannels)
         + " · " + PaywallCopy.DialogsPerMonth(spec.DialogQuota);

    /// <summary>Cross-bot «Сводка» is a Бизнес/Сеть differentiator (spec §2 matrix).</summary>
    public static bool HasCrossBotSummary(PlanTier tier)
        => tier == PlanTier.Business || tier == PlanTier.Network;

    // ── CTA ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// «Попробовать 5 дней бесплатно» only while the trial clock has never started AND
    /// nothing is purchased — every other state is a subscribe form naming the selected
    /// tier and its price in the selected period.
    /// </summary>
    public static string CtaText(bool trialStarted, PlanTier purchased, PlanTier selected, PaywallPeriod period)
    {
        if (!trialStarted && purchased == PlanTier.None)
            return PaywallCopy.TrialCta();
        return PaywallCopy.SubscribeCta(selected, PriceText(PlanCatalog.Get(selected), period));
    }

    /// <summary>Store product id the CTA buys for a (tier, period) selection; empty for non-purchasable tiers.</summary>
    public static string Sku(PlanTier tier, PaywallPeriod period)
    {
        PlanSpec spec = PlanCatalog.Get(tier);
        string sku = period == PaywallPeriod.Year ? spec.SkuYear : spec.SkuMonth;
        return sku ?? "";
    }

    // ── Value receipt (PaywallTrigger.TrialExpired) ──────────────────────────

    /// <summary>
    /// A stat tile's number. null means «we cannot reach this metric», which renders as
    /// an em dash — never as 0, because a real 0 is a very different statement.
    /// </summary>
    public static string StatValue(int? value)
        => value.HasValue ? PaywallCopy.Number(value.Value) : StatUnknown;
}
