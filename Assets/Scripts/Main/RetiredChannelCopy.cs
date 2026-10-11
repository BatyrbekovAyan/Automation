/// <summary>
/// F2 (plan 2026-10-11 §5.1, task T9): every RU string the reconnect screens show — the settings banner and row hint, the
/// card's «Подключить», the chats empty state, the server wait before a pairing and the post-purchase sheet. Pure seam,
/// registered in FontGlyphCoverageTests: every glyph exists in the static SF Pro atlases («·», «—», «…», «« »» yes;
/// «→», «№», «−», «✕» never). The card subline keeps <see cref="ProfileRetirementCopy"/> (M4).
/// The owner sees these texts before the release (decision 2026-10-09).
/// </summary>
public static class RetiredChannelCopy
{
    // ── Settings → «Основное»: the banner while a channel is off and no plan is active ──
    public const string BannerTitle = "Бот отключён";
    public const string BannerBodyTrial =
        "Пробный период закончился, и канал отключили. Настройки, прайс-листы и переписка сохранены — после оплаты подключите канал снова.";
    public const string BannerBodySubscription =
        "Подписка закончилась, и канал отключили. Настройки, прайс-листы и переписка сохранены — после оплаты подключите канал снова.";
    public const string BannerBodyOther =
        "Канал отключили. Настройки, прайс-листы и переписка сохранены — после оплаты подключите канал снова.";
    public const string BannerCta = "Выбрать тариф";

    // ── Settings → under the channel row ──
    public const string RowHint = "Отключён · нажмите, чтобы подключить снова";
    public const string PreviousNumberPrefix = "Ранее: ";

    // ── The bot card: the capsule when no channel of the bot is live ──
    public const string ConnectPill = "Подключить";

    // ── Chats: the empty state of a retired channel ──
    public const string EmptyTitle = "Бот отключён";
    public const string EmptyBody = "Подключите канал снова в настройках бота";
    public const string EmptyCta = "Открыть настройки";

    // ── The wait for the server before a pairing (the store callback lands before RevenueCat's webhook) ──
    public const string Settling = "Подтверждаем оплату…";
    public const string SettleTimedOut = "Оплата ещё не дошла до сервера. Попробуйте через минуту.";

    // ── The sheet after a purchase ──
    public const string SheetTitle = "Подключите ботов снова";
    public const string SheetBody =
        "Тариф активен. Настройки, прайс-листы и переписка на месте — осталось заново подключить WhatsApp или Telegram.";
    public const string SheetRowCta = "Подключить";
    public const string SheetLater = "Позже";

    /// <summary>The banner's body for the server's reason (WhatsApp's first): trial, subscription, or a channel switched off otherwise.</summary>
    public static string BannerBody(string reason) => reason switch
    {
        ProfileRetirement.ReasonTrialExpiry => BannerBodyTrial,
        ProfileRetirement.ReasonChurnGrace => BannerBodySubscription,
        _ => BannerBodyOther,
    };

    /// <summary>
    /// «Ранее: +7 707 …» — the number the channel was paired with, or null when none is stored. A bare digit string (how
    /// Wappi reports a phone) gets its «+»; anything else is shown as stored.
    /// </summary>
    public static string PreviousNumber(string number)
    {
        string trimmed = number?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return PreviousNumberPrefix + (char.IsDigit(trimmed[0]) ? "+" + trimmed : trimmed);
    }

    /// <summary>A sheet row: «{бот} · WhatsApp» / «{бот} · Telegram» (brand names are never translated).</summary>
    public static string SheetRow(string botName, ChatChannel channel) =>
        $"{botName} · {(channel == ChatChannel.Telegram ? "Telegram" : "WhatsApp")}";
}
