using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// «Личные и рабочие чаты»: the app's half of the ChatAudience webhook contract — pure composition of the three request
/// bodies. One POST path, the op in the body:
///   list       {op, profileIds[≤ 20]}                       → {success, profiles[{profileId, connectedAt|null, decisions[…]}]}
///   set        {op, profileId, chats[{chatId, kind, version} ≤ 50]} → {success, results[{chatId, applied, kind, version, refused}]}
///   connected  {op, profileId}                              → {success, connectedAt}
/// The server reads the connected number from get/status itself — the app never sends one. A chat id is the chat-list key
/// (<c>ChatViewModel.ChatId</c> = <c>ChatManager.CurrentChatId</c>), the key the per-chat reply mode uses; the reply mode's
/// wildcard <c>'*'</c> names every chat and is never one (<see cref="IsSendableChatId"/>). Answers are read by
/// <see cref="ChatAudienceResponse"/>.
/// </summary>
public static class ChatAudienceRequest
{
    public const string Path = "/webhook/ChatAudience";
    public const string OpList = "list";
    public const string OpSet = "set";
    public const string OpConnected = "connected";

    /// <summary>The server lists at most this many profiles per request.</summary>
    public const int MaxListProfiles = 20;

    /// <summary>The server takes at most this many chats per set.</summary>
    public const int MaxSetChats = 50;

    /// <summary>The server refuses a longer chat id.</summary>
    public const int MaxChatIdLength = 128;

    public const string KindPersonal = "personal";
    public const string KindBusiness = "business";

    // The server's reason words (chat_audience.reason); "unknown" is never written there — it is the app's «not known».
    private static readonly Dictionary<AudienceReason, string> ReasonWords = new Dictionary<AudienceReason, string>
    {
        { AudienceReason.NonHuman, "nonhuman" },
        { AudienceReason.Owner, "owner" },
        { AudienceReason.Contact, "contact" },
        { AudienceReason.History, "history" },
        { AudienceReason.New, "new" },
        { AudienceReason.Unknown, "unknown" },
    };

    /// <summary>One chat of a set: the owner's kind for it, with the version the queue minted.</summary>
    public readonly struct SetChat
    {
        public SetChat(string chatId, AudienceKind kind, long version)
        {
            ChatId = chatId;
            Kind = kind;
            Version = version;
        }

        public string ChatId { get; }
        public AudienceKind Kind { get; }
        public long Version { get; }
    }

    public static string KindWord(AudienceKind kind) => kind == AudienceKind.Business ? KindBusiness : KindPersonal;

    /// <summary>Exactly the server's two words — ordinal, case-sensitive; anything else is no kind.</summary>
    public static bool TryParseKind(string word, out AudienceKind kind)
    {
        kind = AudienceKind.Personal;
        if (string.Equals(word, KindBusiness, StringComparison.Ordinal))
        {
            kind = AudienceKind.Business;
            return true;
        }
        return string.Equals(word, KindPersonal, StringComparison.Ordinal);
    }

    public static string ReasonWord(AudienceReason reason) => ReasonWords.TryGetValue(reason, out string word) ? word : ReasonWords[AudienceReason.Unknown];

    /// <summary>The reason a word names; a word the server never writes reads as <see cref="AudienceReason.Unknown"/>.</summary>
    public static AudienceReason ParseReason(string word)
    {
        foreach (var pair in ReasonWords)
            if (string.Equals(pair.Value, word, StringComparison.Ordinal)) return pair.Key;
        return AudienceReason.Unknown;
    }

    /// <summary>A profile id the server can know: not empty, not the never-paired sentinel.</summary>
    public static bool IsRealProfileId(string profileId) =>
        !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    /// <summary>A chat id the server would take: not empty, at most <see cref="MaxChatIdLength"/>, no whitespace, not the wildcard.</summary>
    public static bool IsSendableChatId(string chatId)
    {
        if (string.IsNullOrEmpty(chatId) || chatId.Length > MaxChatIdLength) return false;
        if (chatId == ReplyModeSyncQueue.BotDefaultChat) return false;
        foreach (char c in chatId)
            if (char.IsWhiteSpace(c)) return false;
        return true;
    }

    /// <summary>The list read: each real profile once, in the order given (the caller keeps it under <see cref="MaxListProfiles"/>).</summary>
    public static string ListBody(IEnumerable<string> profileIds)
    {
        var ids = new List<string>();
        if (profileIds != null)
            foreach (string id in profileIds)
                if (IsRealProfileId(id) && !ids.Contains(id)) ids.Add(id);
        return JsonConvert.SerializeObject(new { op = OpList, profileIds = ids });
    }

    /// <summary>The owner's decisions for chats of one profile (the caller keeps them under <see cref="MaxSetChats"/>).</summary>
    public static string SetBody(string profileId, IReadOnlyList<SetChat> chats)
    {
        var rows = new List<object>();
        if (chats != null)
            foreach (var chat in chats)
                rows.Add(new { chatId = chat.ChatId, kind = KindWord(chat.Kind), version = chat.Version });
        return JsonConvert.SerializeObject(new { op = OpSet, profileId, chats = rows });
    }

    /// <summary>A confirmed pairing of the profile's channel — the server takes its moment and number from there.</summary>
    public static string ConnectedBody(string profileId) => JsonConvert.SerializeObject(new { op = OpConnected, profileId });
}

/// <summary>One decision the server holds for a chat (a list row): the kind, why, whether the owner made it, its version.</summary>
public sealed class AudienceDecision
{
    public string ChatId;
    public AudienceKind Kind;
    public AudienceReason Reason;
    /// <summary>The owner's decision (source 'owner'), against the rule's computed one (source 'default').</summary>
    public bool ByOwner;
    /// <summary>The owner's version; 0 for a computed decision, which carries none.</summary>
    public long Version;
}

/// <summary>What the server holds for one profile: the moment its channel counts as connected and the decisions that hold.</summary>
public sealed class AudienceProfileState
{
    public string ProfileId;
    /// <summary>Unix milliseconds; null when the server has no connection row for the profile yet.</summary>
    public long? ConnectedAtMs;
    public List<AudienceDecision> Decisions = new List<AudienceDecision>();
}

/// <summary>The server's answer about one chat of a set.</summary>
public sealed class AudienceSetRow
{
    public string ChatId;
    /// <summary>The owner's kind was written; false — the server holds a newer decision, or refused the chat.</summary>
    public bool Applied;
    /// <summary>The chat's kind on the server after the set (the owner's when applied).</summary>
    public AudienceKind Kind;
    public long Version;
    /// <summary>Why the server refused the chat (<c>"nonhuman"</c>), null when it did not.</summary>
    public string Refused;
    public bool IsRefused => Refused != null;
}

/// <summary>
/// Pure reader of the ChatAudience answers. 400 (bad_request), 401 (after AuthorizedRequest's refresh-and-retry) and 403
/// (a profile the token does not list) are the server's refusal. A 2xx is an answer only as the envelope with the BOOLEAN
/// <c>success:true</c> and the op's own positive field; everything else — no answer, n8n's 404 while the webhook is not
/// deployed, 409 / 5xx, a proxy page, <c>{}</c> — is a fault, retried by the queue.
/// </summary>
public static class ChatAudienceResponse
{
    public static ChatAudienceSyncQueue.BatchStatus ClassifyList(long responseCode, string body, out List<AudienceProfileState> profiles)
    {
        profiles = null;
        var status = Envelope(responseCode, body, "profiles", out JObject envelope);
        if (status != ChatAudienceSyncQueue.BatchStatus.Answered) return status;
        profiles = new List<AudienceProfileState>();
        foreach (JToken item in (JArray)envelope["profiles"])
        {
            var state = ReadProfile(item);
            if (state != null) profiles.Add(state);
        }
        return status;
    }

    public static ChatAudienceSyncQueue.BatchStatus ClassifySet(long responseCode, string body, out List<AudienceSetRow> rows)
    {
        rows = null;
        var status = Envelope(responseCode, body, "results", out JObject envelope);
        if (status != ChatAudienceSyncQueue.BatchStatus.Answered) return status;
        rows = new List<AudienceSetRow>();
        foreach (JToken item in (JArray)envelope["results"])
        {
            var row = ReadSetRow(item);
            if (row != null) rows.Add(row);
        }
        return status;
    }

    public static ChatAudienceSyncQueue.BatchStatus ClassifyConnected(long responseCode, string body, out long connectedAtMs)
    {
        connectedAtMs = 0;
        var status = Envelope(responseCode, body, null, out JObject envelope);
        if (status != ChatAudienceSyncQueue.BatchStatus.Answered) return status;
        long? moment = ReadLong(envelope["connectedAt"]);
        if (moment == null || moment.Value <= 0) return ChatAudienceSyncQueue.BatchStatus.Transient;   // names no moment
        connectedAtMs = moment.Value;
        return status;
    }

    // The status line, then the envelope: a JSON object, success the boolean true, and (when named) the op's array.
    private static ChatAudienceSyncQueue.BatchStatus Envelope(long responseCode, string body, string arrayField, out JObject envelope)
    {
        envelope = null;
        if (responseCode == 400 || responseCode == 401 || responseCode == 403) return ChatAudienceSyncQueue.BatchStatus.Rejected;
        if (responseCode < 200 || responseCode >= 300 || string.IsNullOrEmpty(body)) return ChatAudienceSyncQueue.BatchStatus.Transient;
        try
        {
            if (!(JToken.Parse(body) is JObject jo)) return ChatAudienceSyncQueue.BatchStatus.Transient;
            JToken success = jo["success"];
            if (success == null || success.Type != JTokenType.Boolean || !(bool)success) return ChatAudienceSyncQueue.BatchStatus.Transient;
            if (arrayField != null && !(jo[arrayField] is JArray)) return ChatAudienceSyncQueue.BatchStatus.Transient;
            envelope = jo;
            return ChatAudienceSyncQueue.BatchStatus.Answered;
        }
        catch (JsonException)
        {
            return ChatAudienceSyncQueue.BatchStatus.Transient;
        }
    }

    private static AudienceProfileState ReadProfile(JToken item)
    {
        if (!(item is JObject jo) || jo["profileId"]?.Type != JTokenType.String) return null;
        string profileId = (string)jo["profileId"];
        if (!ChatAudienceRequest.IsRealProfileId(profileId)) return null;
        long? moment = ReadLong(jo["connectedAt"]);
        var state = new AudienceProfileState { ProfileId = profileId, ConnectedAtMs = moment > 0 ? moment : null };
        if (jo["decisions"] is JArray decisions)
            foreach (JToken row in decisions)
            {
                var decision = ReadDecision(row);
                if (decision != null) state.Decisions.Add(decision);
            }
        return state;
    }

    private static AudienceDecision ReadDecision(JToken item)
    {
        if (!(item is JObject jo) || jo["chatId"]?.Type != JTokenType.String) return null;
        string chatId = (string)jo["chatId"];
        if (string.IsNullOrEmpty(chatId) || !ChatAudienceRequest.TryParseKind(Text(jo["kind"]), out AudienceKind kind)) return null;
        return new AudienceDecision
        {
            ChatId = chatId,
            Kind = kind,
            Reason = ChatAudienceRequest.ParseReason(Text(jo["reason"])),
            ByOwner = string.Equals(Text(jo["source"]), "owner", StringComparison.Ordinal),
            Version = Math.Max(0, ReadLong(jo["version"]) ?? 0),
        };
    }

    private static AudienceSetRow ReadSetRow(JToken item)
    {
        if (!(item is JObject jo) || jo["chatId"]?.Type != JTokenType.String) return null;
        string chatId = (string)jo["chatId"];
        if (string.IsNullOrEmpty(chatId) || !ChatAudienceRequest.TryParseKind(Text(jo["kind"]), out AudienceKind kind)) return null;
        JToken applied = jo["applied"];
        return new AudienceSetRow
        {
            ChatId = chatId,
            Applied = applied != null && applied.Type == JTokenType.Boolean && (bool)applied,
            Kind = kind,
            Version = Math.Max(0, ReadLong(jo["version"]) ?? 0),
            Refused = jo["refused"]?.Type == JTokenType.String ? (string)jo["refused"] : null,
        };
    }

    private static string Text(JToken token) => token != null && token.Type == JTokenType.String ? (string)token : null;

    // An integer — or the digits of a bigint n8n hands over as text. Anything else is not a number.
    private static long? ReadLong(JToken token)
    {
        if (token == null) return null;
        if (token.Type == JTokenType.Integer)
        {
            try { return (long)token; }
            catch (OverflowException) { return null; }        // a number past long: not a moment or a version of ours
            catch (InvalidCastException) { return null; }
        }
        if (token.Type != JTokenType.String) return null;
        string text = (string)token;
        if (string.IsNullOrEmpty(text) || text.Length > 18) return null;
        foreach (char c in text)
            if (c < '0' || c > '9') return null;
        return long.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }
}
