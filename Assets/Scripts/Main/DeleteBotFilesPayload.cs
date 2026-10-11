using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Pure composer for the /webhook/DeleteBotFiles body sent when a bot is deleted.
/// Besides the RAG sweep (botWaId/botTgId — the workflow ids the chunks are tagged with),
/// the payload carries the bot's Wappi profile ids so the server can retire the matching
/// bot_profiles rows in the same execution and free the channel slots immediately —
/// the scheduled sweeps deliberately never reconcile status='active' owners, so this
/// synchronous send is the ONLY thing that releases a paying owner's slot on delete.
/// appUserId rides along for the execution log's audit trail only; the server matches
/// by profile_id alone (the identity can drift between create and delete — RevenueCat
/// anonymous-id rotation, or the pre-init deviceUniqueIdentifier fallback).
/// </summary>
public static class DeleteBotFilesPayload
{
    /// <summary>
    /// Returns the JSON body, or null when the bot has zero server-side trace (all four
    /// ids are the unauthed sentinel). Sentinel-normalizes each id so the server-side
    /// guards see the same "-1" convention regardless of which empty form the client held.
    /// A real profile id with a sentinel workflow id still sends: the CreateWorkflow
    /// response can be lost after the server already registered the bot_profiles row.
    /// </summary>
    public static string Compose(string whatsappWorkflowId, string telegramWorkflowId,
                                 string whatsappProfileId, string telegramProfileId,
                                 string appUserId)
    {
        string botWaId = Normalize(whatsappWorkflowId);
        string botTgId = Normalize(telegramWorkflowId);
        string waProfileId = Normalize(whatsappProfileId);
        string tgProfileId = Normalize(telegramProfileId);

        if (IsSentinel(botWaId) && IsSentinel(botTgId)
            && IsSentinel(waProfileId) && IsSentinel(tgProfileId))
        {
            return null;
        }

        return JsonConvert.SerializeObject(new
        {
            botWaId,
            botTgId,
            waProfileId,
            tgProfileId,
            appUserId = appUserId ?? ""
        });
    }

    /// <summary>
    /// F2 (T8): every Delete Bot Files body a bot's deletion sends, retired sets included. A channel normally holds either
    /// its current ids or its retired ones (moved aside when the server retired its profile), so ONE call carries, per
    /// channel, whichever it has. Only a channel holding BOTH (a reconnect whose Create did not adopt the retired profile)
    /// needs a second call — with the retired ids of such channels and the sentinel elsewhere. Empty for a bot with no
    /// server trace at all.
    /// </summary>
    public static IReadOnlyList<string> ComposeAll(BotServerDeletion.BotServerIds ids, string appUserId)
    {
        var bodies = new List<string>();
        var wa = ids.Whatsapp;
        var tg = ids.Telegram;
        string first = Compose(
            wa.HasCurrent ? wa.WorkflowId : wa.SeparateRetiredWorkflowId, tg.HasCurrent ? tg.WorkflowId : tg.SeparateRetiredWorkflowId,
            wa.HasCurrent ? wa.ProfileId : wa.SeparateRetiredProfileId, tg.HasCurrent ? tg.ProfileId : tg.SeparateRetiredProfileId,
            appUserId);
        if (first != null) bodies.Add(first);
        bool waBoth = wa.HasCurrent && wa.HasSeparateRetired;
        bool tgBoth = tg.HasCurrent && tg.HasSeparateRetired;
        string second = waBoth || tgBoth
            ? Compose(waBoth ? wa.SeparateRetiredWorkflowId : null, tgBoth ? tg.SeparateRetiredWorkflowId : null,
                      waBoth ? wa.SeparateRetiredProfileId : null, tgBoth ? tg.SeparateRetiredProfileId : null, appUserId)
            : null;
        if (second != null) bodies.Add(second);
        return bodies;
    }

    private static string Normalize(string id) =>
        string.IsNullOrEmpty(id) ? Bot.UnauthedProfileSentinel : id;

    private static bool IsSentinel(string id) => id == Bot.UnauthedProfileSentinel;
}
