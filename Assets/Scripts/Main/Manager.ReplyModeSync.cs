using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

// SUP-02 (client half) — writes the semi-auto suppression flag to the server.
//
// Second partial of the Manager god-object (Manager.cs gained the `partial` keyword, C2).
// Mirrors the fire-and-forget webhook precedent DeleteBotFilesOnServer/DeleteBotFilesRoutine:
// a POST to an unauthenticated /webhook/* endpoint that never blocks the UI, never retries,
// and only logs on failure. The pure builders (BuildReplyModePayload / AuthedProfileIds) are
// EditMode-tested (ReplyModeSyncPayloadTests) — no live n8n/DB needed.
//
// The three write sites (bot-default flip, per-chat toggle, re-assert-on-open heal) and the
// bot-default hook subscription land in the wiring pass — this file provides only the seams.
public partial class Manager
{
    /// <summary>Wire body for POST /webhook/SetReplyMode. Matches the Set_Reply_Mode
    /// Validate node contract: { profileIds:[...], chatId, suppressed }.</summary>
    [System.Serializable]
    private class ReplyModePayload
    {
        public string[] profileIds;
        public string chatId;
        public bool suppressed;
    }

    /// <summary>
    /// Serialises the SetReplyMode body. Pure + static so EditMode tests can JObject.Parse it.
    /// chatId is "*" for a bot-wide default row or the real chat id for a per-chat override.
    /// </summary>
    public static string BuildReplyModePayload(IReadOnlyList<string> profileIds, string chatId, bool suppressed)
    {
        var payload = new ReplyModePayload
        {
            profileIds = profileIds is string[] array ? array : new List<string>(profileIds).ToArray(),
            chatId = chatId,
            suppressed = suppressed
        };
        return JsonConvert.SerializeObject(payload);
    }

    /// <summary>
    /// The bot's authed profile ids for a bot-default ("*") write — one per connected channel,
    /// skipping the unauthed sentinel ("-1", Bot.UnauthedProfileSentinel) and blank ids (C1).
    /// </summary>
    public static string[] AuthedProfileIds(Bot bot)
    {
        var ids = new List<string>(2);
        if (IsRealProfileId(bot.whatsappProfileId)) ids.Add(bot.whatsappProfileId);
        if (IsRealProfileId(bot.telegramProfileId)) ids.Add(bot.telegramProfileId);
        return ids.ToArray();
    }

    private static bool IsRealProfileId(string id) =>
        !string.IsNullOrEmpty(id) && id != Bot.UnauthedProfileSentinel;

    /// <summary>
    /// Fire-and-forget write of the suppression flag for the given profiles + chat.
    /// No-ops on an empty/absent profile list so a never-authed bot never writes junk rows.
    /// </summary>
    public void SyncReplyMode(string[] profileIds, string chatId, bool suppressed)
    {
        if (profileIds == null || profileIds.Length == 0) return;
        StartCoroutine(SyncReplyModeRoutine(BuildReplyModePayload(profileIds, chatId, suppressed)));
    }

    // Copies DeleteBotFilesRoutine verbatim: POST the JSON to the unauthenticated
    // /webhook/SetReplyMode (NO auth header — every /webhook/* is open, R-02-01),
    // Content-Type: application/json (libcurl would otherwise stamp x-www-form-urlencoded
    // and n8n would 415 it), timeout 30, using-block, log-only on failure.
    private IEnumerator SyncReplyModeRoutine(string jsonBody)
    {
        string url = $"{n8nBaseUrl}/webhook/SetReplyMode";

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 30;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
            Debug.LogError($"[SetReplyMode] [{request.responseCode}] {url}: {request.error}\n{request.downloadHandler?.text}");
    }
}
