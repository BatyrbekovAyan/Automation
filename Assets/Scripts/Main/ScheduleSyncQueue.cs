using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Part B «расписание бота» (spec 2026-10-14 §4 B2): the pure model of the schedule's delivery queue — the
/// <see cref="ReplyModeSyncQueue"/> pattern with ONE desire per bot (a newer Save replaces the queued one). The owner's
/// Save records a DESIRE with a version the client mints (<see cref="NextVersion"/>, ms based, strictly increasing per
/// bot); the server writes a profile's row only over an OLDER version and answers per profile
/// <c>{profileId, applied, version}</c>. applied:false means the server already holds a NEWER version — the desire is
/// SUPERSEDED: settled without a retry or an error, the local values kept (multi-device is unsupported in v1), and the
/// server's version becomes the floor of the next desire so a later Save is never refused by the same row again.
///
/// The profile ids are NOT part of the desire: the sender resolves the bot's channels at send time
/// (<see cref="Sendable"/>) — a channel re-paired since the Save is followed, a retired one (absent from the token) is
/// never sent, and a bot with no live channel settles until the next Save or settings Create re-arms it.
///
/// Fix round 3 (I-1): where a bot's delivery stands is readable (<see cref="StateOf"/> — the settings section shows a
/// pending or failed schedule), and an undelivered desire is re-armed (<see cref="Rearm"/>) on a return to the app
/// (<see cref="ShouldRearmOnFocus"/>, at most once a minute) and whenever the bot's settings open — not only on the next
/// launch, which on a phone that is never restarted can be weeks away.
///
/// No Unity types: EditMode-tested (ScheduleSyncServiceTests). The service owns PlayerPrefs (<c>CrSchedule.Queue</c>).
/// </summary>
public static class ScheduleSyncQueue
{
    public const string StatePending = "pending";
    public const string StateDone = "done";        // answered (applied or superseded) — or nothing was deliverable
    public const string StateFailed = "failed";    // refused / attempts exhausted THIS launch; re-armed on the next one

    public const int MaxAttemptsPerLaunch = 8;
    /// <summary>The server's Validate keeps at most ten ids (a bot has two).</summary>
    public const int MaxProfilesPerRequest = 10;
    /// <summary>A return to the app re-arms undelivered desires at most once in this long (AuthClient's resume rule).</summary>
    public const long FocusRearmGapMs = 60_000;

    [Serializable]
    public class Entry
    {
        public string botId;
        public bool enabled;
        public int daysMask;
        public int startMin;
        public int endMin;
        public string timeZone;
        /// <summary>The desire's version — the one sent, and the one an answer must name to settle it.</summary>
        public long version;
        /// <summary>The highest version the server reported for this bot: the floor of the next desire.</summary>
        public long serverVersion;
        public int attempts;
        public long nextAttemptAtMs;
        public string state = StatePending;

        [JsonIgnore] public bool IsPending => state == StatePending;
        [JsonIgnore] public BotSchedule Schedule => new BotSchedule(enabled, daysMask, startMin, endMin);
    }

    [Serializable]
    public class Model
    {
        public List<Entry> entries = new List<Entry>();
    }

    /// <summary>How the server answered one request: a success envelope, a definitive refusal, or anything else.</summary>
    public enum BatchStatus { Answered, Rejected, Transient }

    /// <summary>Strictly increasing per bot even when the wall clock stalls or steps back.</summary>
    public static long NextVersion(long nowMs, long lastVersion) => Math.Max(nowMs, lastVersion + 1);

    public static Entry Find(Model m, string botId)
    {
        foreach (var e in m.entries)
            if (e.botId == botId) return e;
        return null;
    }

    /// <summary>Record the bot's schedule as its one desire (replacing a queued one) and make it due now.</summary>
    public static long Desire(Model m, string botId, BotSchedule schedule, string timeZone, long nowMs)
    {
        var e = Find(m, botId);
        if (e == null)
        {
            e = new Entry { botId = botId };
            m.entries.Add(e);
        }
        e.version = NextVersion(nowMs, Math.Max(e.version, e.serverVersion));
        e.enabled = schedule.Enabled;
        e.daysMask = schedule.DaysMask;
        e.startMin = schedule.StartMin;
        e.endMin = schedule.EndMin;
        e.timeZone = timeZone;
        Arm(e, nowMs);
        return e.version;
    }

    /// <summary>
    /// The desires to send now. <paramref name="held"/>: bots whose server deletion is in flight (V12) — none of their
    /// desires leaves (a send landing after Delete Bot Files would recreate the bot_schedule rows it removed); they stay
    /// pending, untouched, until the service releases the bot (the server kept it) or forgets it (it went).
    /// </summary>
    public static List<Entry> Due(Model m, long nowMs, ICollection<string> held = null)
    {
        var due = new List<Entry>();
        foreach (var e in m.entries)
            if (e.IsPending && e.nextAttemptAtMs <= nowMs && !IsHeld(held, e.botId)) due.Add(e);
        return due;
    }

    /// <summary>Whether anything is left to deliver — a held bot's desire waits for its release, not for a backoff.</summary>
    public static bool AnyPending(Model m, ICollection<string> held = null)
    {
        foreach (var e in m.entries)
            if (e.IsPending && !IsHeld(held, e.botId)) return true;
        return false;
    }

    private static bool IsHeld(ICollection<string> held, string botId) => held != null && held.Contains(botId);

    /// <summary>2 s, 5 s, 15 s, 60 s, then every 5 min — the reply-mode schedule, bounded per launch.</summary>
    public static long BackoffMs(int attemptsSoFar) => ReplyModeSyncQueue.BackoffMs(attemptsSoFar);

    /// <summary>A transient failure (no answer, 5xx, an unreadable 2xx): retry later, or park until the next launch.</summary>
    public static void OnSendFailed(Entry e, long nowMs)
    {
        e.nextAttemptAtMs = nowMs + BackoffMs(e.attempts);
        e.attempts++;
        if (e.attempts >= MaxAttemptsPerLaunch) e.state = StateFailed;
    }

    /// <summary>No channel of the bot is live in the token: nothing to deliver until a Save or a settings Create.</summary>
    public static void OnNothingToSend(Entry e) => e.state = StateDone;

    /// <summary>
    /// Where the bot's schedule delivery stands, as the settings section shows it: no desire, or a settled one (applied,
    /// superseded by a newer server row, or nothing was deliverable) is confirmed; a desire still on its way — the boot
    /// gate, a backoff, a held deletion — is pending; one refused, or parked after its attempts, has failed.
    /// </summary>
    public static ScheduleDelivery StateOf(Model m, string botId)
    {
        var e = m != null ? Find(m, botId) : null;
        if (e == null || e.state == StateDone) return ScheduleDelivery.Confirmed;
        return e.state == StateFailed ? ScheduleDelivery.Failed : ScheduleDelivery.Pending;
    }

    /// <summary>
    /// Try an undelivered desire again NOW — what a new launch does (<see cref="Deserialize"/>): a pending one (waiting
    /// out a backoff) and a failed one (refused, or parked after its attempts) restart from attempt 0, due at once, with
    /// the version they were minted with; a settled one is untouched. <paramref name="botId"/> null = every bot.
    /// True when anything was re-armed.
    /// </summary>
    public static bool Rearm(Model m, string botId, long nowMs)
    {
        bool any = false;
        foreach (var e in m.entries)
        {
            if (botId != null && e.botId != botId) continue;
            if (e.state != StatePending && e.state != StateFailed) continue;
            Arm(e, nowMs);
            any = true;
        }
        return any;
    }

    /// <summary>A return to the app re-arms once a minute at most; a clock moved back never blocks it.</summary>
    public static bool ShouldRearmOnFocus(bool hasFocus, long nowMs, long lastRearmMs)
        => hasFocus && (nowMs - lastRearmMs >= FocusRearmGapMs || nowMs < lastRearmMs);

    /// <summary>
    /// Apply one answer to the desire it was sent for. Only a desire still carrying the SENT version is touched — a Save
    /// made while the request was in flight is newer, untried, and stays due. Answered: settled once every sent profile
    /// is reported (applied, or superseded by a newer server row); a profile the server did not report is retried.
    /// </summary>
    public static void ApplyResult(Entry e, long sentVersion, BatchStatus status, IReadOnlyList<string> sentIds,
                                   IReadOnlyList<ScheduleSyncResponse.Row> rows, long nowMs)
    {
        if (e == null || e.version != sentVersion) return;
        if (status == BatchStatus.Rejected)
        {
            e.state = StateFailed;
            return;
        }
        if (status == BatchStatus.Transient)
        {
            OnSendFailed(e, nowMs);
            return;
        }
        long seen = e.serverVersion;
        foreach (var id in sentIds)
        {
            var row = FindRow(rows, id);
            if (row == null)
            {
                OnSendFailed(e, nowMs);
                return;
            }
            seen = Math.Max(seen, row.Version);
        }
        e.serverVersion = seen;
        e.state = StateDone;
    }

    /// <summary>
    /// The profile ids one request may carry: the bot's real channels ("-1" / blank dropped — a channel never paired)
    /// that the session token lists (a retired one is not — the gate would refuse the whole request with 403),
    /// de-duplicated, at most <see cref="MaxProfilesPerRequest"/>.
    /// </summary>
    public static List<string> Sendable(IEnumerable<string> currentIds, Func<string, bool> tokenOwns)
    {
        var ids = new List<string>();
        if (currentIds == null || tokenOwns == null) return ids;
        foreach (var id in currentIds)
        {
            if (ids.Count == MaxProfilesPerRequest) break;
            if (string.IsNullOrEmpty(id) || id == Bot.UnauthedProfileSentinel || ids.Contains(id) || !tokenOwns(id)) continue;
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>A deleted bot's desire must never be sent again (it would recreate the rows its deletion removed).</summary>
    public static void ForgetBot(Model m, string botId) => m.entries.RemoveAll(e => e.botId == botId);

    /// <summary>The full local wipe: nothing may survive for a later bot that reuses a slot name.</summary>
    public static void ForgetAll(Model m) => m.entries.Clear();

    // ---- persistence (JSON) ----

    public static string Serialize(Model m) => JsonConvert.SerializeObject(m);

    /// <summary>Reload for a new launch: pending and parked desires are retried from attempt 0, settled ones stay.</summary>
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
        m.entries ??= new List<Entry>();
        m.entries.RemoveAll(e => e == null || string.IsNullOrEmpty(e.botId));
        Rearm(m, null, nowMs);
        return m;
    }

    private static void Arm(Entry e, long nowMs)
    {
        e.attempts = 0;
        e.nextAttemptAtMs = nowMs;
        e.state = StatePending;
    }

    private static ScheduleSyncResponse.Row FindRow(IReadOnlyList<ScheduleSyncResponse.Row> rows, string profileId)
    {
        if (rows == null) return null;
        foreach (var r in rows)
            if (r.ProfileId == profileId) return r;
        return null;
    }
}

/// <summary>Where a bot's schedule delivery stands (<see cref="ScheduleSyncQueue.StateOf"/>; ScheduleCopy.SyncStatus says it).</summary>
public enum ScheduleDelivery
{
    /// <summary>Nothing owed: the server holds the saved schedule (or a newer one), or the bot has nothing to receive it.</summary>
    Confirmed,
    /// <summary>A desire is still on its way: the boot gate, a backoff, a held deletion.</summary>
    Pending,
    /// <summary>Refused, or parked after its attempts — re-armed by a return to the app, the bot's settings, a new launch.</summary>
    Failed,
}

/// <summary>Pure reader of the Set Schedule answer (build-set-schedule.py Shape Response).</summary>
public static class ScheduleSyncResponse
{
    public class Row
    {
        public string ProfileId;
        public bool Applied;
        public long Version;
    }

    /// <summary>
    /// 400 (bad_request / bad_timezone), 401 (after AuthorizedRequest's refresh-and-retry) and 403 (a profile that is not
    /// the token's) are the server's word: Rejected, no retry. A 2xx is an answer only as the envelope with the BOOLEAN
    /// success:true; everything else — no answer, 5xx, n8n's own 404, a proxy page — is Transient.
    /// </summary>
    public static ScheduleSyncQueue.BatchStatus Classify(long responseCode, string body, out List<Row> rows)
    {
        rows = null;
        if (responseCode == 400 || responseCode == 401 || responseCode == 403) return ScheduleSyncQueue.BatchStatus.Rejected;
        if (responseCode < 200 || responseCode >= 300) return ScheduleSyncQueue.BatchStatus.Transient;
        var parsed = ParseSuccess(body);
        if (parsed == null) return ScheduleSyncQueue.BatchStatus.Transient;
        rows = parsed;
        return ScheduleSyncQueue.BatchStatus.Answered;
    }

    private static List<Row> ParseSuccess(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        try
        {
            if (!(JToken.Parse(body) is JObject jo)) return null;
            var success = jo["success"];
            if (success == null || success.Type != JTokenType.Boolean || !(bool)success) return null;
            var rows = new List<Row>();
            if (jo["results"] is JArray results)
                foreach (var r in results)
                {
                    if (!(r is JObject row) || row["profileId"]?.Type != JTokenType.String) continue;
                    rows.Add(new Row
                    {
                        ProfileId = (string)row["profileId"],
                        Applied = row["applied"]?.Type == JTokenType.Boolean && (bool)row["applied"],
                        Version = row["version"]?.Type == JTokenType.Integer ? (long)row["version"] : 0,
                    });
                }
            return rows;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
