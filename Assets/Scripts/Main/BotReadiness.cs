using System;

/// <summary>
/// Stage 7.2: the owner's short readiness status — four facts, each from its own reliable source, kept
/// deliberately apart from the onboarding checklist (which counts a manual outgoing as «the bot replied»
/// on purpose). Pure; the sheet fills it from Bot/PlayerPrefs/ReplyModeSyncService.
/// </summary>
public readonly struct BotReadiness
{
    public readonly bool ChannelConnected;   // an authed Wappi profile on the active channel
    public readonly bool ModeConfirmed;      // the server confirmed the reply mode (no pending / failed sync)
    public readonly bool KnowledgeAvailable; // catalog items, uploaded documents or a business description
    public readonly bool? TestPassed;        // null = never tested

    public BotReadiness(bool channelConnected, bool modeConfirmed, bool knowledgeAvailable, bool? testPassed)
    { ChannelConnected = channelConnected; ModeConfirmed = modeConfirmed; KnowledgeAvailable = knowledgeAvailable; TestPassed = testPassed; }

    public bool Ready => ChannelConnected && ModeConfirmed && KnowledgeAvailable && TestPassed == true;

    public static BotReadiness Compute(string profileId, bool syncPending, bool syncFailed, int catalogItems, int documents, string businessText, bool? testPassed)
    {
        bool channel = !string.IsNullOrEmpty(profileId) && profileId != "-1";
        bool mode = channel && !syncPending && !syncFailed;
        bool knowledge = catalogItems > 0 || documents > 0 || !string.IsNullOrWhiteSpace(businessText);
        return new BotReadiness(channel, mode, knowledge, testPassed);
    }
}

/// <summary>RU lines of the readiness block (pinned by FontGlyphCoverageTests).</summary>
public static class BotReadinessCopy
{
    public const string Title = "Проверка бота";
    public const string RowTitle = "Проверить бота";
    public const string RowSubline = "Тест без отправки клиенту и без расхода лимита";
    public const string Channel = "Канал подключён";
    public const string ChannelNo = "Канал не подключён";
    public const string Mode = "Режим подтверждён";
    public const string ModeNo = "Режим ещё не подтверждён";
    public const string Knowledge = "Знания доступны";
    public const string KnowledgeNo = "Знаний нет: добавьте описание, товары или прайс-лист";
    public const string TestOk = "Тест пройден";
    public const string TestFailed = "Тест не пройден";
    public const string TestNone = "Тест ещё не запускали";
    public const string Placeholder = "Напишите как клиент…";
    public const string Send = "Отправить";
    public const string Close = "Закрыть";
    public const string Sending = "Бот думает…";
    public const string SafeNote = "Тест: клиенту ничего не отправляется, лимит диалогов не расходуется.";
    public const string SourcesTitle = "Источники";
    public const string SourceCatalog = "каталог";
    public const string SourceContacts = "контакты";
    public const string SourceDocuments = "прайс-листы";
    public const string SourceNone = "только описание бизнеса";
    public const string ActionHandoff = "Действие: заявка была бы передана владельцу";
    public const string ActionReply = "Действие: только ответ";
    public const string RecentTitle = "Последние ответы";
    public const string RecentNone = "Ответов ещё не было";
    public const string ErrorGeneric = "Не удалось проверить: попробуйте позже";
    public const string ErrorUnavailable = "Промпт бота недоступен: сохраните настройки бота и повторите";
    public const string Correlation = "ID";

    public const string ErrorNetwork = "Нет связи: проверьте интернет и повторите";
    public const string ErrorForbidden = "Этот бот не привязан к этой установке";
    public const string ErrorUnauthorized = "Сессия не подтверждена: перезапустите приложение";
    public const string Bullet = "• ";

    /// <summary>No check-mark glyph on purpose: the SF Pro atlases are static and ✓ is not in them — the state is carried by colour.</summary>
    public static string Line(bool ok, string yes, string no) => Bullet + (ok ? yes : no);

    public static string TestLine(bool? passed) => Bullet + (passed == null ? TestNone : passed == true ? TestOk : TestFailed);

    public static string ErrorFor(string kind) => kind switch
    {
        TestDialogResult.KindNetwork => ErrorNetwork,
        TestDialogResult.KindForbidden => ErrorForbidden,
        TestDialogResult.KindUnauthorized => ErrorUnauthorized,
        TestDialogResult.KindPromptUnavailable => ErrorUnavailable,
        _ => ErrorGeneric,
    };

    public static string Sources(bool catalog, bool contacts, int documents)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (catalog) parts.Add(SourceCatalog);
        if (contacts) parts.Add(SourceContacts);
        if (documents > 0) parts.Add(SourceDocuments + " (" + documents + ")");
        return SourcesTitle + ": " + (parts.Count == 0 ? SourceNone : string.Join(", ", parts));
    }
}

/// <summary>Which authed channel a test message goes through — WhatsApp first, Telegram otherwise, none when neither is connected.</summary>
public sealed class BotTestTarget
{
    public readonly string ProfileId;
    public readonly string WorkflowId;
    public readonly string Channel;

    private BotTestTarget(string profileId, string workflowId, string channel) { ProfileId = profileId; WorkflowId = workflowId; Channel = channel; }

    public static bool IsReal(string id) => !string.IsNullOrEmpty(id) && id != "-1";

    /// <summary>Pure. Returns null when no channel carries BOTH an authed profile and a bound workflow.</summary>
    public static BotTestTarget Resolve(string waProfileId, string waWorkflowId, string tgProfileId, string tgWorkflowId)
    {
        if (IsReal(waProfileId) && IsReal(waWorkflowId)) return new BotTestTarget(waProfileId, waWorkflowId, "whatsapp");
        if (IsReal(tgProfileId) && IsReal(tgWorkflowId)) return new BotTestTarget(tgProfileId, tgWorkflowId, "telegram");
        return null;
    }
}
