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

    // R15: which version of the bot answered — the ID line names it (the server's promptVersion)
    public const string VersionPublished = "версия";
    public const string VersionDraft = "канал выключен, проверены текущие настройки";
    public const string VersionDraftPending = "есть неопубликованные изменения";
    public const string VersionSeparator = " · ";
    public const int VersionIdChars = 8;

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

    /// <summary>R15: the tested version for the ID line — «версия 1a2b3c4d» for a live bot (plus the pending-draft note),
    /// the inactive-channel note for a draft; empty when the server did not say.</summary>
    public static string VersionNote(string source, string versionId, bool draftPending)
    {
        if (string.IsNullOrEmpty(versionId)) return "";
        string shortId = versionId.Length > VersionIdChars ? versionId.Substring(0, VersionIdChars) : versionId;
        if (source == TestDialogPromptVersion.SourceDraft) return $"{VersionDraft} {shortId}";
        return draftPending ? $"{VersionPublished} {shortId}, {VersionDraftPending}" : $"{VersionPublished} {shortId}";
    }

    /// <summary>The correlation line: «ID: 1546 · версия 1a2b3c4d»; either half may be absent.</summary>
    public static string CorrelationLine(string correlationId, string versionNote)
    {
        string id = string.IsNullOrEmpty(correlationId) ? "" : $"{Correlation}: {correlationId}";
        if (string.IsNullOrEmpty(versionNote)) return id;
        return id.Length == 0 ? versionNote : id + VersionSeparator + versionNote;
    }

    public static string Sources(bool catalog, bool contacts, int documents)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (catalog) parts.Add(SourceCatalog);
        if (contacts) parts.Add(SourceContacts);
        if (documents > 0) parts.Add(SourceDocuments + " (" + documents + ")");
        return SourcesTitle + ": " + (parts.Count == 0 ? SourceNone : string.Join(", ", parts));
    }
}

/// <summary>
/// R16: the identity of ONE test call, captured when it is sent. Its result belongs to this bot, this
/// profile/workflow and this screen — never to whatever the sheet shows by the time the answer lands.
/// </summary>
public readonly struct BotTestTicket
{
    public readonly int Seq;
    public readonly string BotName;
    public readonly string ProfileId;
    public readonly string WorkflowId;

    public BotTestTicket(int seq, string botName, string profileId, string workflowId)
    { Seq = seq; BotName = botName; ProfileId = profileId; WorkflowId = workflowId; }
}

/// <summary>What one returned test result may change (R16).</summary>
public readonly struct BotTestOutcome
{
    /// <summary>The sheet still shows the request's own screen AND its bot still exists: reply, history and busy may
    /// change. V11: the screen ends on Hide (not on the next Show), and a deleted bot's answer never lands on it. A live
    /// bot whose target changed under the open sheet still gets its answer shown (the owner's own question, the same
    /// bot) — the target check belongs to the verdict only; refusing the answer would leave the sheet «Бот думает…».</summary>
    public readonly bool ApplyToScreen;
    public readonly int? Verdict;         // 1 passed / 0 failed, written under the TICKET's bot; null = no verdict

    public BotTestOutcome(bool applyToScreen, int? verdict) { ApplyToScreen = applyToScreen; Verdict = verdict; }
}

/// <summary>R16, pure: who a late test answer may touch. The sheet is one shared static surface reused for every bot.</summary>
public static class BotTestResultPolicy
{
    /// <summary>Only the bot's own outcome is a verdict: a reply passes, a failed generation or an unreadable prompt
    /// fails; no connection, no session or anything else says nothing about the bot.</summary>
    public static int? VerdictOf(TestDialogResult result)
    {
        if (result == null) return null;
        if (result.IsOk) return 1;
        bool botSide = result.ErrorKind == TestDialogResult.KindGenerationFailed || result.ErrorKind == TestDialogResult.KindPromptUnavailable;
        return botSide ? 0 : (int?)null;
    }

    /// <param name="currentSeq">the sheet's sequence now — bumped by every Show, every Hide and every send</param>
    /// <param name="botAlive">the ticket's Bot still exists (Unity-null once deleted)</param>
    /// <param name="currentTarget">the ticket's bot's target NOW (null when no channel is connected)</param>
    /// <param name="lastRecordedSeq">the newest ticket whose verdict was already written for this bot (0 = none)</param>
    public static BotTestOutcome Decide(BotTestTicket ticket, int currentSeq, bool botAlive, BotTestTarget currentTarget, int lastRecordedSeq, TestDialogResult result)
    {
        int? verdict = VerdictOf(result);
        bool sameTarget = botAlive && currentTarget != null
            && currentTarget.ProfileId == ticket.ProfileId && currentTarget.WorkflowId == ticket.WorkflowId;
        bool newest = ticket.Seq > lastRecordedSeq;
        bool writable = verdict.HasValue && sameTarget && newest && !string.IsNullOrEmpty(ticket.BotName);
        return new BotTestOutcome(ticket.Seq == currentSeq && botAlive, writable ? verdict : null);
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
