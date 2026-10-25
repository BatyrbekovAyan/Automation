/// <summary>
/// «Личные и рабочие чаты»: every user-facing string of the feature — the working list's «Личные» row, the folder and its
/// «Изменить» mode, the «Кто из них клиенты?» screen, the subline of a chat that is not a person, the swipe actions, the
/// toasts, the open chat's «Личный» pill and its two sheets, the settings section. RU only (house rule), glyphs the static
/// SF Pro atlases draw (registered in FontGlyphCoverageTests: no «−», «№», «✕», «→»); names per
/// docs/design/2026-10-21-naming-sotrudnik-vybrat.md — «ИИ-сотрудник», and the word «Выбрать» never appears on these screens.
/// Counted nouns agree through <see cref="RuPlural"/>. Two texts state a number the server keeps: «через 2 дня» is
/// <see cref="ChatAudienceRule.WipeDays"/> (passed in) and «старше суток» is <see cref="ChatAudienceRule.FreshHours"/>
/// (PersonalChatsCopyTests fails when the constant moves without the text). Static labels are authored from here by
/// PersonalChatsBuilder; everything else is written at runtime.
/// </summary>
public static class PersonalChatsCopy
{
    // ---- the working chat list ----

    public const string RowTitle = "Личные";

    // ---- the «Личные» folder and its «Изменить» mode ----

    public const string FolderTitle = "Личные";
    public const string Edit = "Изменить";
    public const string Cancel = "Отмена";
    public const string FolderHint =
        "Контакты из телефона, все, кто писал до подключения, группы и каналы. ИИ-сотрудник здесь не участвует, уведомлений нет.";
    public const string FolderFooter = "Свайп по чату — сделать его рабочим";
    public const string EditTitle = "Отметьте клиентов";
    public const string EditSearch = "Поиск по личным";
    public const string SelectAtLeastOne = "Отметьте хотя бы один чат";

    // ---- «Кто из них клиенты?» — the folder right after a channel is connected ----

    public const string OnboardingTitle = "Кто из них клиенты?";
    public const string OnboardingBody =
        "Все ваши контакты и все, с кем вы переписывались до подключения, — личные: ИИ-сотрудник их не трогает. Отметьте клиентов, с ними он будет работать.";
    public const string OnboardingSearch = "Поиск по чатам";
    public const string OnboardingSection = "НЕДАВНИЕ ЧАТЫ";
    public const string SublineContact = "в контактах";
    public const string SublineHistory = "не в контактах, писал раньше";
    public const string OnboardingLater = "Отмечу позже";

    // ---- the subline of a chat that is not a person ----

    public const string NonHumanGroup = "Группа · ИИ-сотрудник в группах не отвечает";
    public const string NonHumanChannel = "Канал · ИИ-сотрудник каналам не отвечает";
    public const string NonHumanBot = "Бот · ИИ-сотрудник ботам не отвечает";
    public const string NonHumanService = "Служебные уведомления · ИИ-сотрудник им не отвечает";
    public const string NonHumanSelf = "Избранное · сообщения самому себе";

    // ---- swipes and toasts ----

    public const string SwipeToPersonal = "В личные";
    public const string SwipeToBusiness = "В рабочие";
    public const string SwipeDelete = "Удалить";
    public const string MovedToPersonal = "Чат перенесён в «Личные»";
    public const string MovedToBusiness = "Чат стал рабочим";
    /// <summary>The server refused the move — the chat is back where it was. A network fault is retried, never told.</summary>
    public const string WriteFailed = "Не удалось перенести чат";
    public const string SaveFirst = "Сначала сохраните изменения";

    // ---- the open chat: the «Личный» pill and its two sheets ----

    public const string Pill = "Личный";
    public const string MakeBusinessTitle = "Сделать чат рабочим?";
    public const string MakeBusinessYes = "Сделать рабочим";
    public const string MakeBusinessNo = "Оставить личным";
    public const string MakeBusinessAutoFresh =
        "У вас включено «Авто»: ИИ-сотрудник сразу ответит на последнее сообщение и дальше будет отвечать сам. Выключить «Авто» в этом чате можно отдельно.";
    public const string MakeBusinessAutoNoFresh =
        "У вас включено «Авто»: ИИ-сотрудник будет отвечать сам на новые сообщения. Выключить «Авто» в этом чате можно отдельно.";
    /// <summary>«Старше суток» is <see cref="ChatAudienceRule.FreshHours"/> = 24.</summary>
    public const string MakeBusinessStaleNote = "На сообщения старше суток он не отвечает — начнёт с нового.";
    public const string MakeBusinessManual = "ИИ-сотрудник будет подсказывать ответы, а отправлять их будете вы.";
    public const string MakePersonalAction = "Сделать личным";
    public const string ContactMark = "в контактах";
    public const string NewMark = "новый";

    // ---- the bot's settings, «Основное» ----

    public const string SettingsHeader = "ЛИЧНЫЕ ЧАТЫ";
    public const string SettingsRow = "Личные чаты";
    public const string SettingsHint =
        "Контакты из телефона и все, кто писал до подключения. ИИ-сотрудник в них не участвует. Внутри можно отметить клиентов — с ними он будет работать. Ботам, каналам и рассылкам он не отвечает никогда.";

    // ---- composed ----

    /// <summary>The «Изменить» mode's button: «Сделать рабочими · 2».</summary>
    public static string MakeBusinessMany(int count) => $"Сделать рабочими · {count}";

    /// <summary>The onboarding's first line: «WhatsApp подключён» / «Telegram подключён».</summary>
    public static string OnboardingConnected(bool telegram) => $"{ChannelName(telegram)} подключён";

    /// <summary>The onboarding's button: «Готово · клиентов: 2».</summary>
    public static string OnboardingDone(int clients) => $"Готово · клиентов: {clients}";

    /// <summary>The onboarding while the channel's chats are still arriving.</summary>
    public static string OnboardingLoading(bool telegram) => $"Загружаем чаты из {ChannelName(telegram)}…";

    /// <summary>The onboarding's soft note when chats arrived but not one of them reads as a saved contact.</summary>
    public static string OnboardingNoContacts(bool telegram) =>
        $"{ChannelName(telegram)} на телефоне, похоже, не видит контакты. Клиентов можно отметить вручную.";

    /// <summary>The «Сделать личным» sheet's body; <paramref name="days"/> is <see cref="ChatAudienceRule.WipeDays"/>.</summary>
    public static string MakePersonalBody(int days) =>
        $"ИИ-сотрудник перестанет участвовать в этом чате. Через {days} {RuPlural.Pick(days, "день", "дня", "дней")} он забудет эту переписку.";

    /// <summary>
    /// What a chat that is not a person is, and that the assistant never answers it — its line in the folder, and what its
    /// «Личный» pill says when tapped (such a chat can never be made working). Null for a person.
    /// </summary>
    public static string NonHumanLine(NonHumanKind kind) => kind switch
    {
        NonHumanKind.Group => NonHumanGroup,
        NonHumanKind.Channel => NonHumanChannel,
        NonHumanKind.Bot => NonHumanBot,
        NonHumanKind.Service => NonHumanService,
        NonHumanKind.Self => NonHumanSelf,
        _ => null,
    };

    private static string ChannelName(bool telegram) => telegram ? "Telegram" : "WhatsApp";
}
