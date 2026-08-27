/// <summary>Which price column the paywall is showing.</summary>
public enum PaywallPeriod { Month, Year }

/// <summary>
/// The second, secondary-styled paywall button (Task 18): the direct-purchase escape hatch
/// shown only while the primary CTA is the free-trial offer.
/// </summary>
public struct PaywallSecondaryRow
{
    public bool Visible;
    public string Text;             // «Оформить Бизнес — 19 990 ₸/мес», empty when hidden
}

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

    /// <summary>
    /// Auto-renew disclosure for the subscribe state (Apple Guideline 3.1.2: renewal terms
    /// must be clear at the purchase point; price + period already sit on the tier cards).
    /// Split per store because Guideline 2.3.10 forbids mentioning Google Play inside the
    /// iOS binary. The trial state keeps <see cref="FinePrint"/> — that CTA buys nothing.
    /// </summary>
    public const string FinePrintAutoRenewIos =
        "Продлевается автоматически · отмена в настройках App Store";
    public const string FinePrintAutoRenewAndroid =
        "Продлевается автоматически · отмена в настройках Google Play";

    public static string FinePrintText(bool isTrialOffer, bool iosStore)
        => isTrialOffer ? FinePrint
         : iosStore ? FinePrintAutoRenewIos
         : FinePrintAutoRenewAndroid;

    public const string PeriodMonth = "Месяц";
    public const string PeriodYear = "Год";

    /// <summary>Shown instead of a number whenever the source metric is not reachable yet.</summary>
    public const string StatUnknown = "—";

    /// <summary>
    /// Value-receipt tile labels, in tile order (день-5 «чек ценности»). Tiles 3 and 4 have
    /// no source yet and render <see cref="StatUnknown"/> — see the task-14a report.
    /// Lives here rather than in the controller because it is user-facing RU copy, and this
    /// is the place tests can pin it (moved out of PaywallController by Task 14b).
    /// </summary>
    public static readonly string[] ReceiptLabels =
    {
        "Диалогов обработано",
        "Заказов собрано",
        "Ответов ночью",
        "Средний ответ",
    };

    /// <summary>
    /// Transient notices shown in the fine-print slot after a failed store round-trip.
    /// WORDING IS STILL PENDING THE OWNER'S SIGN-OFF — pinned here (rather than typed at the
    /// two call sites in PaywallController) so the eventual reword is a single edit.
    /// A user_cancelled purchase deliberately shows NOTHING: the user already knows.
    /// </summary>
    public const string PurchaseFailedNotice = "Не удалось оформить подписку. Попробуйте ещё раз.";
    public const string RestoreNothingFoundNotice = "Активных покупок не найдено";
    public const string RestoreFailedNotice = "Не удалось восстановить покупки";

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
    /// The one state in which the primary CTA offers the free trial instead of naming a tier:
    /// the trial clock has never started, nothing is purchased AND the server has not already
    /// told us this account is expired. Shared by <see cref="CtaText"/> and
    /// <see cref="SecondaryPurchase"/> so the two can never disagree about which button
    /// carries the subscribe form.
    ///
    /// <paramref name="serverSaysExpired"/> (Task 19) is the same fact
    /// <see cref="EntitlementGate.CurrentTier"/> now reads — an id-persisted reinstall wipes
    /// the local trial ledger while the server still knows the subscription ended, and
    /// offering that owner a free trial he cannot have is what walked him into the Create
    /// webhook's refusal on 2026-08-26. Unknown snapshot ⇒ <c>false</c> ⇒ unchanged.
    /// </summary>
    public static bool IsTrialOffer(bool trialStarted, PlanTier purchased, bool serverSaysExpired)
        => !trialStarted && purchased == PlanTier.None && !serverSaysExpired;

    /// <summary>
    /// «Попробовать 5 дней бесплатно» only in the <see cref="IsTrialOffer"/> state — every
    /// other one is a subscribe form naming the selected tier and its price in the selected
    /// period.
    /// </summary>
    public static string CtaText(bool trialStarted, PlanTier purchased, bool serverSaysExpired,
        PlanTier selected, PaywallPeriod period)
    {
        if (IsTrialOffer(trialStarted, purchased, serverSaysExpired))
            return PaywallCopy.TrialCta();
        return SubscribeText(selected, period);
    }

    /// <summary>
    /// The direct-purchase button UNDER the trial CTA (Task 18). It exists because the trial
    /// offer names no tier and buys nothing — without it, a fresh install has no way to pay,
    /// which is what the owner hit on device 2026-08-26.
    ///
    /// Visible ONLY in that state, deliberately: everywhere else the CTA already IS this exact
    /// string, and a second copy of it would be a duplicate. The text is therefore the SAME
    /// <see cref="PaywallCopy.SubscribeCta"/> form the CTA shows, so a tier/period change moves
    /// both in lockstep. A selection with no store product (never reachable through
    /// <see cref="Order"/>, but cheap to rule out) hides the button rather than offering a
    /// purchase that cannot be made.
    /// </summary>
    public static PaywallSecondaryRow SecondaryPurchase(bool trialStarted, PlanTier purchased,
        bool serverSaysExpired, PlanTier selected, PaywallPeriod period)
    {
        if (!IsTrialOffer(trialStarted, purchased, serverSaysExpired) || string.IsNullOrEmpty(Sku(selected, period)))
            return new PaywallSecondaryRow { Visible = false, Text = "" };

        return new PaywallSecondaryRow { Visible = true, Text = SubscribeText(selected, period) };
    }

    /// <summary>«Оформить &lt;тариф&gt; — &lt;цена&gt;» for the current selection.</summary>
    public static string SubscribeText(PlanTier tier, PaywallPeriod period)
        => PaywallCopy.SubscribeCta(tier, PriceText(PlanCatalog.Get(tier), period));

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
