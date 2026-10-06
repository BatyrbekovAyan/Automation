/// <summary>
/// RU copy for the legacy-profile claim sheet (A1). Pure seam — registered in
/// FontGlyphCoverageTests so every glyph exists in the static SF Pro atlases (no «№», «−», «✕», «→»).
/// </summary>
public static class ProfileClaimCopy
{
    public const string TitleCode = "Подтвердите бота";
    public const string TitleReauth = "Подключите канал заново";
    public const string TitleOwned = "Бот подтверждён";

    /// <summary>{0} = bot name, {1} = channel name.</summary>
    public const string BodyCodeFormat =
        "Мы отправили 6-значный код в чат «Избранное» ({1}) аккаунта бота «{0}». Введите его, чтобы " +
        "привязать бота к этой установке.";

    public const string BodyReauthFormat =
        "Бот «{0}» ещё не подтверждён на этом устройстве: откройте его настройки и подключите {1} заново. " +
        "После подключения введите код, который придёт в чат «Избранное».";

    public const string BodyTransportDown = "Сервер недоступен. Попробуйте позже.";

    public const string Placeholder = "Код из 6 цифр";
    public const string Submit = "Подтвердить";
    public const string Later = "Позже";
    public const string Ok = "Понятно";
    /// <summary>The code sheet raised right after a pairing (V2): leaving it cancels the connection.</summary>
    public const string Cancel = "Отменить";
    public const string Resend = "Отправить код ещё раз";

    public const string ErrorWrongCode = "Код не подходит. Проверьте его в чате «Избранное» и введите ещё раз.";
    public const string ErrorLocked = "Слишком много попыток. Запросите новый код через 10 минут.";
    public const string ErrorSendFailed = "Не удалось отправить код. Попробуйте ещё раз.";
    public const string ErrorGeneric = "Не удалось подтвердить. Попробуйте позже.";

    public const string ChannelWhatsApp = "WhatsApp";
    public const string ChannelTelegram = "Telegram";

    public static string ChannelName(ChatChannel channel) =>
        channel == ChatChannel.Telegram ? ChannelTelegram : ChannelWhatsApp;

    public static string BodyCode(string botName, ChatChannel channel) =>
        string.Format(BodyCodeFormat, botName, ChannelName(channel));

    public static string BodyReauth(string botName, ChatChannel channel) =>
        string.Format(BodyReauthFormat, botName, ChannelName(channel));
}
