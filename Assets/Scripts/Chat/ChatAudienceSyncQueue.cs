using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// «Личные и рабочие чаты»: the pure model of the queue that carries the owner's chat moves and the app's connection reports
/// to the ChatAudience webhook — the <see cref="ReplyModeSyncQueue"/> pattern.
///
/// A move is a DESIRE per (profile, chat): the kind the owner chose and a version the client mints
/// (<see cref="NextVersion"/>: max(now, last + 1), strictly increasing across every move and every restart, and raised above
/// any version the server reported — <see cref="RaiseFloor"/>). A newer move of the same chat replaces the queued one. The
/// server writes an owner decision only over an OLDER version and answers per chat; the answer settles the desire and the
/// desire leaves the queue (the decision mirror holds the result):
///   • applied — the owner's kind is the server's decision now;
///   • applied:false — the server holds a newer decision: its view is adopted;
///   • refused (a chat that is not a person) — adopted too, and reported as not accepted.
/// A refusal of the whole request (400/401/403) is the write failure: the desires are taken off and the chats go back. A
/// fault — no answer, a 5xx, n8n's 404 while the webhook is not deployed, a 2xx that is not the envelope, a chat the answer
/// did not name — never drops a move: it backs off (2 s, 5 s, 15 s, 60 s, then every 5 min), parks after
/// <see cref="MaxAttemptsPerLaunch"/> attempts and waits for a re-arm (a return to the app, at most once a minute, and the
/// next launch).
///
/// Connection reports are a separate list of the same queue — one per profile, sent before any move. An answered report
/// leaves the queue; a refused one waits for a re-arm; a fault backs off like a move.
///
/// Every entry names its bot: while Manager.DeleteBotOnServer runs the bot is HELD (V12) and none of its entries leaves —
/// one landing after Delete Bot Files would recreate the rows it removed.
///
/// No Unity types: EditMode-tested (ChatAudienceSyncQueueTests). The service owns PlayerPrefs (<c>CrAudience.Queue</c>).
/// </summary>
public static class ChatAudienceSyncQueue
{
    public const string StatePending = "pending";
    /// <summary>Every attempt of THIS launch failed — re-armed by a return to the app or the next launch.</summary>
    public const string StateFailed = "failed";
    /// <summary>The server refused a connection report (400/401/403) — never «check the internet»; re-armed like a failure.</summary>
    public const string StateRejected = "rejected";

    public const int MaxAttemptsPerLaunch = 8;

    /// <summary>A return to the app re-arms undelivered entries at most once in this long (AuthClient's resume rule).</summary>
    public const long FocusRearmGapMs = 60_000;

    /// <summary>The owner's move of one chat that the server has not answered for yet.</summary>
    [Serializable]
    public class Desire
    {
        public string botId;
        public string profileId;
        public string chatId;
        /// <summary>The wire word (<c>personal</c> / <c>business</c>) — readable in the store, stable across enum changes.</summary>
        public string kind;
        /// <summary>The version this move was minted with — the one sent, and the one an answer must name to settle it.</summary>
        public long version;
        public int attempts;
        public long nextAttemptAtMs;
        public string state = StatePending;

        [JsonIgnore] public bool IsPending => state == StatePending;
        [JsonIgnore] public AudienceKind Kind => ChatAudienceRequest.TryParseKind(kind, out AudienceKind k) ? k : AudienceKind.Personal;
    }

    /// <summary>A confirmed pairing of one profile's channel that the server has not been told about yet.</summary>
    [Serializable]
    public class Connection
    {
        public string botId;
        public string profileId;
        /// <summary>Bumped by every report of this profile: an answer settles only the report it was sent for.</summary>
        public int reports;
        public int attempts;
        public long nextAttemptAtMs;
        public string state = StatePending;

        [JsonIgnore] public bool IsPending => state == StatePending;
    }

    [Serializable]
    public class Model
    {
        public List<Connection> connections = new List<Connection>();
        public List<Desire> desires = new List<Desire>();
        /// <summary>The highest version minted or reported: every new move goes above it, whatever the clock says.</summary>
        public long lastVersion;
    }

    /// <summary>How the server answered one request: the success envelope, a definitive refusal, or anything else.</summary>
    public enum BatchStatus { Answered, Rejected, Transient }

    /// <summary>A chat the server answered for: the kind it holds now and whether that is the owner's move.</summary>
    public sealed class SettledChat
    {
        public string ChatId;
        public AudienceKind Kind;
        public bool Applied;
        public bool Refused;
    }

    /// <summary>What one set answer did to its moves.</summary>
    public sealed class SetOutcome
    {
        /// <summary>Moves the server answered for — the mirror takes the server's kind.</summary>
        public readonly List<SettledChat> Settled = new List<SettledChat>();
        /// <summary>Moves of a refused request — taken off, the chats go back to where they were.</summary>
        public readonly List<string> Rejected = new List<string>();

        /// <summary>The chats whose move the server did not take: a refused request, or a chat it refused.</summary>
        public List<string> NotAccepted()
        {
            var chats = new List<string>(Rejected);
            foreach (var chat in Settled)
                if (chat.Refused) chats.Add(chat.ChatId);
            return chats;
        }
    }

    /// <summary>Strictly increasing even when the wall clock stalls or steps back.</summary>
    public static long NextVersion(long nowMs, long lastVersion) => Math.Max(nowMs, lastVersion + 1);

    public static Desire FindDesire(Model m, string profileId, string chatId)
    {
        foreach (var d in m.desires)
            if (d.profileId == profileId && d.chatId == chatId) return d;
        return null;
    }

    public static Connection FindConnection(Model m, string profileId)
    {
        foreach (var c in m.connections)
            if (c.profileId == profileId) return c;
        return null;
    }

    /// <summary>
    /// The owner moved these chats of one profile: one desire per chat (a queued move of the same chat is replaced), one
    /// version for the call, due at once. Chats the server would refuse by their id (empty, the wildcard, whitespace, too
    /// long) are left out, each chat counts once. Returns the version (0 when nothing was accepted).
    /// </summary>
    public static long AddDesires(Model m, string botId, string profileId, IReadOnlyList<string> chatIds, AudienceKind kind, long nowMs,
                                  out List<string> accepted)
    {
        accepted = new List<string>();
        if (!ChatAudienceRequest.IsRealProfileId(profileId) || chatIds == null) return 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string chatId in chatIds)
            if (ChatAudienceRequest.IsSendableChatId(chatId) && seen.Add(chatId)) accepted.Add(chatId);
        if (accepted.Count == 0) return 0;

        long version = NextVersion(nowMs, m.lastVersion);
        m.lastVersion = version;
        foreach (string chatId in accepted)
        {
            var desire = FindDesire(m, profileId, chatId);
            if (desire == null)
            {
                desire = new Desire { profileId = profileId, chatId = chatId };
                m.desires.Add(desire);
            }
            desire.botId = botId ?? "";
            desire.kind = ChatAudienceRequest.KindWord(kind);
            desire.version = version;
            Arm(desire, nowMs);
        }
        return version;
    }

    /// <summary>A pairing of the profile's channel was confirmed: report it (again), due at once. A bot already known stays.</summary>
    public static void AddConnection(Model m, string botId, string profileId, long nowMs)
    {
        if (!ChatAudienceRequest.IsRealProfileId(profileId)) return;
        var connection = FindConnection(m, profileId);
        if (connection == null)
        {
            connection = new Connection { profileId = profileId, botId = "" };
            m.connections.Add(connection);
        }
        if (!string.IsNullOrEmpty(botId)) connection.botId = botId;
        connection.reports++;
        Arm(connection, nowMs);
    }

    /// <summary>The next report to send: due now, not of a held bot (V12).</summary>
    public static Connection NextDueConnection(Model m, long nowMs, ICollection<string> held = null)
    {
        foreach (var c in m.connections)
            if (c.IsPending && c.nextAttemptAtMs <= nowMs && !IsHeld(held, c.botId)) return c;
        return null;
    }

    /// <summary>
    /// The next set request: the due moves of ONE profile (the first due move's), in the order they were made, at most
    /// <see cref="ChatAudienceRequest.MaxSetChats"/>. Empty when nothing is due.
    /// </summary>
    public static List<Desire> NextDueBatch(Model m, long nowMs, ICollection<string> held = null)
    {
        var batch = new List<Desire>();
        foreach (var d in m.desires)
        {
            if (!d.IsPending || d.nextAttemptAtMs > nowMs || IsHeld(held, d.botId)) continue;
            if (batch.Count > 0 && (d.profileId != batch[0].profileId || d.botId != batch[0].botId)) continue;
            batch.Add(d);
            if (batch.Count == ChatAudienceRequest.MaxSetChats) break;
        }
        return batch;
    }

    /// <summary>Whether anything still waits for a backoff — a parked, refused or held entry waits for a re-arm or a release.</summary>
    public static bool AnyPending(Model m, ICollection<string> held = null)
    {
        foreach (var c in m.connections)
            if (c.IsPending && !IsHeld(held, c.botId)) return true;
        foreach (var d in m.desires)
            if (d.IsPending && !IsHeld(held, d.botId)) return true;
        return false;
    }

    /// <summary>2 s, 5 s, 15 s, 60 s, then every 5 min — the reply-mode schedule, bounded per launch.</summary>
    public static long BackoffMs(int attemptsSoFar) => ReplyModeSyncQueue.BackoffMs(attemptsSoFar);

    /// <summary>What a set request carries: each move's chat, kind and version, captured together before the send.</summary>
    public static List<ChatAudienceRequest.SetChat> Snapshot(IReadOnlyList<Desire> batch)
    {
        var sent = new List<ChatAudienceRequest.SetChat>();
        if (batch != null)
            foreach (var d in batch)
                sent.Add(new ChatAudienceRequest.SetChat(d.chatId, d.Kind, d.version));
        return sent;
    }

    /// <summary>
    /// Apply one set answer to the moves it was sent for. Only a move still carrying the SENT version is touched — a move
    /// the owner made while the request was on the wire is newer, untried, and stays due. A chat the answer did not name is
    /// retried like a fault; every reported version raises the floor of the next move.
    /// </summary>
    public static SetOutcome ApplySetResult(Model m, string profileId, IReadOnlyList<ChatAudienceRequest.SetChat> sent, BatchStatus status,
                                            IReadOnlyList<AudienceSetRow> rows, long nowMs)
    {
        var outcome = new SetOutcome();
        if (sent == null) return outcome;
        foreach (var chat in sent)
        {
            var desire = FindDesire(m, profileId, chat.ChatId);
            if (desire == null || desire.version != chat.Version) continue;     // forgotten, or moved again meanwhile
            if (status == BatchStatus.Rejected)
            {
                m.desires.Remove(desire);
                outcome.Rejected.Add(chat.ChatId);
                continue;
            }
            var row = status == BatchStatus.Answered ? FindRow(rows, chat.ChatId) : null;
            if (row == null)
            {
                OnSendFailed(desire, nowMs);
                continue;
            }
            m.desires.Remove(desire);
            m.lastVersion = Math.Max(m.lastVersion, row.Version);
            outcome.Settled.Add(new SettledChat { ChatId = chat.ChatId, Kind = row.Kind, Applied = row.Applied, Refused = row.IsRefused });
        }
        return outcome;
    }

    /// <summary>
    /// Apply one connected answer. True when the report is settled (answered for the report it was sent for); a report made
    /// again while it was on the wire stays due — the server must read the newest pairing.
    /// </summary>
    public static bool ApplyConnectedResult(Model m, Connection entry, int sentReport, BatchStatus status, long nowMs)
    {
        if (entry == null || !m.connections.Contains(entry) || entry.reports != sentReport) return false;
        switch (status)
        {
            case BatchStatus.Answered:
                m.connections.Remove(entry);
                return true;
            case BatchStatus.Rejected:
                entry.state = StateRejected;
                return false;
            default:
                OnSendFailed(entry, nowMs);
                return false;
        }
    }

    /// <summary>The session does not list the profile: no install of ours holds it now — nothing to report.</summary>
    public static void DropConnection(Model m, Connection entry) => m.connections.Remove(entry);

    /// <summary>A version the server reported (a list row, a set answer) becomes the floor of the next move. True when it rose.</summary>
    public static bool RaiseFloor(Model m, long serverVersion)
    {
        if (serverVersion <= m.lastVersion) return false;
        m.lastVersion = serverVersion;
        return true;
    }

    /// <summary>
    /// Try every undelivered entry again NOW — a move waiting out a backoff or parked after its attempts, a report refused or
    /// failed: attempt 0, due at once, versions kept. What a launch does (<see cref="Deserialize"/>) and a return to the app.
    /// True when anything was re-armed.
    /// </summary>
    public static bool Rearm(Model m, long nowMs)
    {
        foreach (var c in m.connections) Arm(c, nowMs);
        foreach (var d in m.desires) Arm(d, nowMs);
        return m.connections.Count > 0 || m.desires.Count > 0;
    }

    /// <summary>A return to the app re-arms once a minute at most; a clock moved back never blocks it.</summary>
    public static bool ShouldRearmOnFocus(bool hasFocus, long nowMs, long lastRearmMs)
        => hasFocus && (nowMs - lastRearmMs >= FocusRearmGapMs || nowMs < lastRearmMs);

    /// <summary>
    /// A deleted bot's entries must never be sent again: every entry of the bot, and every entry of one of its profiles (a
    /// report made before its bot was known). Returns the profiles whose entries went.
    /// </summary>
    public static List<string> ForgetBot(Model m, string botId, ICollection<string> profileIds)
    {
        var touched = new List<string>();
        bool Belongs(string entryBot, string entryProfile) =>
            (!string.IsNullOrEmpty(botId) && entryBot == botId) || (profileIds != null && profileIds.Contains(entryProfile));
        foreach (var d in m.desires)
            if (Belongs(d.botId, d.profileId) && !touched.Contains(d.profileId)) touched.Add(d.profileId);
        foreach (var c in m.connections)
            if (Belongs(c.botId, c.profileId) && !touched.Contains(c.profileId)) touched.Add(c.profileId);
        m.desires.RemoveAll(d => Belongs(d.botId, d.profileId));
        m.connections.RemoveAll(c => Belongs(c.botId, c.profileId));
        return touched;
    }

    /// <summary>The full local wipe: no entry survives for a later bot. The version floor stays — versions never go back.</summary>
    public static void ForgetAll(Model m)
    {
        m.desires.Clear();
        m.connections.Clear();
    }

    // ---- persistence (JSON) ----

    public static string Serialize(Model m) => JsonConvert.SerializeObject(m);

    /// <summary>
    /// Reload for a new launch: every undelivered entry is retried from attempt 0. A corrupt store is an empty queue; an
    /// entry that could never travel (no profile, an unsendable chat, a kind the server does not know) is dropped.
    /// </summary>
    public static Model Deserialize(string json, long nowMs)
    {
        Model m = null;
        try
        {
            if (!string.IsNullOrEmpty(json)) m = JsonConvert.DeserializeObject<Model>(json);
        }
        catch (Exception)
        {
            m = null;
        }
        m ??= new Model();
        m.connections ??= new List<Connection>();
        m.desires ??= new List<Desire>();
        m.connections.RemoveAll(c => c == null || !ChatAudienceRequest.IsRealProfileId(c.profileId));
        m.desires.RemoveAll(d => d == null || !ChatAudienceRequest.IsRealProfileId(d.profileId)
                                 || !ChatAudienceRequest.IsSendableChatId(d.chatId) || !ChatAudienceRequest.TryParseKind(d.kind, out _));
        foreach (var c in m.connections) c.botId ??= "";
        foreach (var d in m.desires) d.botId ??= "";
        Rearm(m, nowMs);
        return m;
    }

    private static bool IsHeld(ICollection<string> held, string botId) => held != null && !string.IsNullOrEmpty(botId) && held.Contains(botId);

    private static void Arm(Desire d, long nowMs)
    {
        d.attempts = 0;
        d.nextAttemptAtMs = nowMs;
        d.state = StatePending;
    }

    private static void Arm(Connection c, long nowMs)
    {
        c.attempts = 0;
        c.nextAttemptAtMs = nowMs;
        c.state = StatePending;
    }

    // A fault: the next try after the backoff, or parked until a re-arm once the launch's attempts are spent.
    private static void OnSendFailed(Desire d, long nowMs)
    {
        d.nextAttemptAtMs = nowMs + BackoffMs(d.attempts);
        d.attempts++;
        if (d.attempts >= MaxAttemptsPerLaunch) d.state = StateFailed;
    }

    private static void OnSendFailed(Connection c, long nowMs)
    {
        c.nextAttemptAtMs = nowMs + BackoffMs(c.attempts);
        c.attempts++;
        if (c.attempts >= MaxAttemptsPerLaunch) c.state = StateFailed;
    }

    private static AudienceSetRow FindRow(IReadOnlyList<AudienceSetRow> rows, string chatId)
    {
        if (rows == null) return null;
        foreach (var row in rows)
            if (row != null && row.ChatId == chatId) return row;
        return null;
    }
}
