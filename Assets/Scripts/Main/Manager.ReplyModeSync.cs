using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

// SUP-02 (client half) — writes the semi-auto suppression flag to the server.
//
// Second partial of the Manager god-object (Manager.cs gained the `partial` keyword, C2).
// Began as a fire-and-forget POST (the precedent of the old DeleteBotFiles call, itself replaced by the
// verdict-reading Manager.DeleteBotOnServer in V12); since A2 delivery is ReplyModeSyncService's queue
// and this partial only composes the body. The pure builders (BuildReplyModePayload / AuthedProfileIds) are
// EditMode-tested (ReplyModeSyncPayloadTests) — no live n8n/DB needed.
//
// The three write sites (bot-default flip, per-chat toggle, re-assert-on-open heal) and the
// bot-default hook subscription land in the wiring pass — this file provides only the seams.
public partial class Manager
{
    /// <summary>Wire body for POST /webhook/SetReplyMode. Matches the Set_Reply_Mode
    /// Validate node contract: { profileIds:[...], chatId, suppressed, version, decisionAgeMs? }.</summary>
    [System.Serializable]
    private class ReplyModePayload
    {
        public string[] profileIds;
        public string chatId;
        public bool suppressed;
        // A2: the desired-state version (ReplyModeSyncQueue.NextVersion) — the server applies the
        // row only when its stored version is older, so a reordered/retried write cannot regress.
        public long version;
        // R4-clock: how long ago the owner decided, measured as the request leaves. The server dates the decision
        // on its own clock (now − age) and closes only the handoff requests that existed by then. Omitted when there
        // is no decision to date — the server then keeps judging by the version.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long? decisionAgeMs;
    }

    /// <summary>
    /// Serialises the SetReplyMode body. Pure + static so EditMode tests can JObject.Parse it.
    /// chatId is "*" for a bot-wide default row or the real chat id for a per-chat override.
    /// </summary>
    public static string BuildReplyModePayload(IReadOnlyList<string> profileIds, string chatId, bool suppressed, long version,
                                               long? decisionAgeMs = null)
    {
        var payload = new ReplyModePayload
        {
            profileIds = profileIds is string[] array ? array : new List<string>(profileIds).ToArray(),
            chatId = chatId,
            suppressed = suppressed,
            version = version,
            decisionAgeMs = decisionAgeMs
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
    /// Record the owner's choice for the given profiles + chat as a DESIRE in the persisted
    /// last-state queue (A2): delivered with bounded retries, confirmed per profile by the server,
    /// survives a restart. No-ops on an empty/absent profile list so a never-authed bot never
    /// writes junk rows.
    /// </summary>
    public void SyncReplyMode(string botId, string[] profileIds, string chatId, bool suppressed)
    {
        if (profileIds == null || profileIds.Length == 0) return;
        ReplyModeSyncService.Instance?.Desire(botId, profileIds, chatId, suppressed);
    }

    /// <summary>
    /// The chat-open heal (R4): re-assert a chat's EXPLICIT override without minting a new decision. The server
    /// reads every applied chat-scoped «Авто» as the owner's resume (it closes the chat's handoff requests up to
    /// that version), so a chat open must repeat the original version, never «now».
    /// </summary>
    public void ReassertReplyMode(string botId, string profileId, string chatId, bool suppressed)
    {
        if (!IsRealProfileId(profileId)) return;
        ReplyModeSyncService.Instance?.Reassert(botId, profileId, chatId, suppressed);
    }

    /// <summary>
    /// R5 (2026-09-27): the boot session op, then the reply-mode send gate. ReplyModeSyncService resumes its
    /// persisted queue on every launch, but a send before BillingService.Initialize and the session would go out on
    /// the device-id fallback identity; the gate opens here, after both (the service opens it itself after
    /// <see cref="ReplyModeSendGate.SafetySeconds"/> if this never runs to the end).
    /// </summary>
    private IEnumerator EnsureSessionThenOpenReplyModeSendGate()
    {
        yield return AuthClient.Instance.EnsureSession();
        ReplyModeSyncService.OpenSendGate();
    }

    // --- Bot-default flip hook (ReplyModeToggleBinder.OnReplyModeChanged) -----

    // Manager declares its OWN OnEnable/OnDisable here (the primary file has none — C4) to
    // subscribe the always-alive singleton to the built-but-unconsumed reply-mode event. A
    // static event + singleton makes exactly-once subscribe/unsubscribe leak-free.
    private void OnEnable()  => ReplyModeToggleBinder.OnReplyModeChanged += OnBotReplyModeChanged;
    private void OnDisable() => ReplyModeToggleBinder.OnReplyModeChanged -= OnBotReplyModeChanged;

    /// <summary>
    /// A bot's default reply mode flipped: write the "*" bot-wide row for EVERY authed profile
    /// of that bot (Semi ⇒ suppressed=true, Auto ⇒ suppressed=false). Sentinel/blank ids are
    /// dropped by AuthedProfileIds; an all-unauthed bot no-ops inside SyncReplyMode.
    /// </summary>
    private void OnBotReplyModeChanged(string botId, ReplyModeToggleBinder.ReplyMode mode)
    {
        // A2: a value the SERVER just told us about is not a new desire — writing it back would only
        // mint a fresh version for a choice the server already holds (and could ping-pong).
        if (ReplyModeToggleBinder.ReconcilingFromServer) return;
        Bot bot = FindBotByName(botId);
        if (bot == null) return;
        SyncReplyMode(botId, AuthedProfileIds(bot), "*", mode == ReplyModeToggleBinder.ReplyMode.Semi);
    }

    /// <summary>
    /// Late-auth seed (WR-02): a channel authed AFTER the owner set the bot default to Вместе
    /// gets a real profile id but no "*" row — the gate would coalesce to false and the bot
    /// auto-replies on the new channel while the app shows Вместе (double-reply risk). Called
    /// fire-and-forget from the four workflow-creation success paths to re-assert the current
    /// default for the newly-authed profile. Semi-only by design: absence already reads as
    /// Авто, so the Auto case writes nothing (LOCKED absence→reply semantics).
    /// </summary>
    public void SeedReplyModeDefaultForProfile(string botId, string profileId)
    {
        if (!IsRealProfileId(profileId)) return;
        if (ReplyModeToggleBinder.GetMode(botId) != ReplyModeToggleBinder.ReplyMode.Semi) return;
        SyncReplyMode(botId, new[] { profileId }, "*", true);
    }
}
