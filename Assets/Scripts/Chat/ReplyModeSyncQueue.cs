using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Pure model of the reply-mode LAST-STATE queue (A2, audit stage 2). One entry per
/// (profileId, chatId): the DESIRED state (what the owner chose, with a version the client minted)
/// and the CONFIRMED state (what the server acknowledged, with the server's version). A tap only
/// changes the desire; the UI may paint «confirmed» only after <see cref="OnResponse"/> says so.
///
/// Versions: a per-entry monotonic number, ms-timestamp based (<see cref="NextVersion"/>), sent with
/// every write. The server applies a row only when its stored version is older, and answers with
/// its current value + version otherwise — so a delayed retry can never overwrite a newer desire,
/// and a newer server value (another device) is ADOPTED rather than fought.
///
/// No Unity types: EditMode-tested (ReplyModeSyncQueueTests). Persistence is JSON via
/// <see cref="Serialize"/>/<see cref="Deserialize"/>; the service layer owns PlayerPrefs.
/// </summary>
public static class ReplyModeSyncQueue
{
    public const string StatePending = "pending";
    public const string StateConfirmed = "confirmed";
    public const string StateFailed = "failed";        // attempts exhausted / refused THIS launch; retried on the next one (the service resumes the queue at boot)

    public const int MaxAttemptsPerLaunch = 8;
    public const string BotDefaultChat = "*";

    [Serializable]
    public class Entry
    {
        public string botId;
        public string profileId;
        public string chatId;
        public bool desiredSuppressed;
        public long desiredVersion;
        public bool hasConfirmed;
        public bool confirmedSuppressed;
        public long confirmedVersion;
        public int attempts;
        public long nextAttemptAtMs;
        public string state = StatePending;

        public bool IsPending => state == StatePending;
        public bool IsFailed => state == StateFailed;
    }

    [Serializable]
    public class Model
    {
        public List<Entry> entries = new List<Entry>();
    }

    public enum Outcome { Confirmed, AdoptedServer, Ignored }

    public static string Key(string profileId, string chatId) => profileId + "|" + (chatId ?? BotDefaultChat);

    /// <summary>Strictly increasing per entry even when the wall clock stalls or steps back.</summary>
    public static long NextVersion(long nowMs, long lastVersion) => Math.Max(nowMs, lastVersion + 1);

    public static Entry Find(Model m, string profileId, string chatId)
    {
        foreach (var e in m.entries)
            if (e.profileId == profileId && e.chatId == chatId) return e;
        return null;
    }

    /// <summary>
    /// Record a new desire for every profile (one version for the group so the sender can batch
    /// them into a single request). Supersedes any pending desire for the same entries.
    /// </summary>
    public static long Desire(Model m, string botId, IReadOnlyList<string> profileIds, string chatId, bool suppressed, long nowMs)
    {
        long last = 0;
        foreach (var pid in profileIds)
        {
            var e = Find(m, pid, chatId);
            if (e != null) last = Math.Max(last, Math.Max(e.desiredVersion, e.confirmedVersion));
        }
        long version = NextVersion(nowMs, last);
        foreach (var pid in profileIds)
        {
            var e = Find(m, pid, chatId);
            if (e == null)
            {
                e = new Entry { botId = botId, profileId = pid, chatId = chatId };
                m.entries.Add(e);
            }
            e.botId = botId;
            e.desiredSuppressed = suppressed;
            e.desiredVersion = version;
            e.attempts = 0;
            e.nextAttemptAtMs = nowMs;
            e.state = StatePending;
        }
        return version;
    }

    /// <summary>
    /// The version a heal uses for an «Авто» the queue has no record of: it applies only where the server holds no
    /// row for the chat (or a pre-A2 version-0 row), never beats a stored decision, and — being older than every
    /// handoff request — never resolves one on the server (R4).
    /// </summary>
    public const long FloorVersion = 1;

    /// <summary>
    /// R4 (2026-09-27): the chat-open heal REPEATS a decision, it never makes a new one. Every version the server
    /// applies to a chat-scoped «Авто» resolves that chat's handoff requests up to that version, so a heal that
    /// minted «now» turned a mere chat open into the owner's resume and closed a request the owner had not seen.
    /// Same value already queued → re-sent with its ORIGINAL version (a parked one is re-armed), or nothing when it
    /// is confirmed. «Вместе» resolves nothing server-side, so a differing one is an ordinary desire. An «Авто» with
    /// no record is probed with <see cref="FloorVersion"/>; an undelivered differing decision is left to its answer.
    /// </summary>
    public static void Reassert(Model m, string botId, string profileId, string chatId, bool suppressed, long nowMs)
    {
        var e = Find(m, profileId, chatId);
        if (e != null && e.desiredSuppressed == suppressed)
        {
            e.botId = botId;
            if (e.IsFailed) Arm(e, nowMs);
            return;
        }
        if (suppressed)
        {
            Desire(m, botId, new[] { profileId }, chatId, true, nowMs);
            return;
        }
        if (e != null && e.IsPending) return;
        if (e == null)
        {
            e = new Entry { profileId = profileId, chatId = chatId };
            m.entries.Add(e);
        }
        e.botId = botId;
        e.desiredSuppressed = false;
        e.desiredVersion = FloorVersion;
        Arm(e, nowMs);
    }

    private static void Arm(Entry e, long nowMs)
    {
        e.attempts = 0;
        e.nextAttemptAtMs = nowMs;
        e.state = StatePending;
    }

    public static List<Entry> Due(Model m, long nowMs)
    {
        var due = new List<Entry>();
        foreach (var e in m.entries)
            if (e.IsPending && e.nextAttemptAtMs <= nowMs) due.Add(e);
        return due;
    }

    public static bool AnyPending(Model m, string botId, string chatId = null)
    {
        foreach (var e in m.entries)
            if (e.IsPending && e.botId == botId && (chatId == null || e.chatId == chatId)) return true;
        return false;
    }

    public static bool AnyFailed(Model m, string botId)
    {
        foreach (var e in m.entries)
            if (e.IsFailed && e.botId == botId) return true;
        return false;
    }

    /// <summary>2 s, 5 s, 15 s, 60 s, then every 5 min — bounded per launch by <see cref="MaxAttemptsPerLaunch"/>.</summary>
    public static long BackoffMs(int attemptsSoFar) => attemptsSoFar switch
    {
        0 => 2_000, 1 => 5_000, 2 => 15_000, 3 => 60_000, _ => 300_000,
    };

    /// <summary>A transient failure (network, 5xx): schedule the next try or park the entry for the next launch.</summary>
    public static void OnSendFailed(Entry e, long nowMs)
    {
        e.nextAttemptAtMs = nowMs + BackoffMs(e.attempts);
        e.attempts++;
        if (e.attempts >= MaxAttemptsPerLaunch) e.state = StateFailed;
    }

    /// <summary>A definitive refusal (403 forbidden, 400): the desire cannot be delivered as is — stop retrying.</summary>
    public static void OnRejected(Entry e)
    {
        e.state = StateFailed;
    }

    /// <summary>
    /// The server answered the request that carried <paramref name="sentVersion"/>. A response for
    /// an older version than the current desire is IGNORED (a newer desire is already queued).
    /// applied ⇒ confirmed = desired. Not applied ⇒ the server holds a NEWER version: adopt it.
    /// </summary>
    public static Outcome OnResponse(Entry e, long sentVersion, bool applied, bool serverSuppressed, long serverVersion)
    {
        if (sentVersion != e.desiredVersion) return Outcome.Ignored;
        if (applied)
        {
            e.hasConfirmed = true;
            e.confirmedSuppressed = e.desiredSuppressed;
            e.confirmedVersion = Math.Max(serverVersion, sentVersion);
            e.state = StateConfirmed;
            return Outcome.Confirmed;
        }
        e.hasConfirmed = true;
        e.confirmedSuppressed = serverSuppressed;
        e.confirmedVersion = serverVersion;
        e.desiredSuppressed = serverSuppressed;
        e.desiredVersion = Math.Max(serverVersion, e.desiredVersion);
        e.state = StateConfirmed;
        return Outcome.AdoptedServer;
    }

    /// <summary>How the server answered one batch: a success envelope, a definitive refusal (400/403), or anything else.</summary>
    public enum BatchStatus { Answered, Rejected, Transient }

    /// <summary>
    /// Apply one batch's answer to its entries (R5, 2026-09-27). Only an entry still desiring the version that was
    /// SENT is touched: a desire the owner made while the request was in flight is newer, has not been tried, and must
    /// stay due — a stale 400/403 used to park it as failed, a stale transient failure delayed it by a backoff.
    /// Answered: per-profile confirmation / adoption (a profile the server did not report is retried like a transient
    /// failure). Returns the entries whose NEWER server value was adopted, for the caller to announce.
    /// </summary>
    public static List<Entry> ApplyBatchResult(List<Entry> batch, long sentVersion, BatchStatus status,
                                               IReadOnlyList<ReplyModeSyncResponse.Row> rows, long nowMs)
    {
        var adopted = new List<Entry>();
        foreach (var e in batch)
        {
            if (e.desiredVersion != sentVersion) continue;
            if (status == BatchStatus.Rejected) { OnRejected(e); continue; }
            var row = status == BatchStatus.Answered ? FindRow(rows, e.profileId) : null;
            if (row == null) { OnSendFailed(e, nowMs); continue; }
            if (OnResponse(e, sentVersion, row.Applied, row.Suppressed, row.Version) == Outcome.AdoptedServer) adopted.Add(e);
        }
        return adopted;
    }

    private static ReplyModeSyncResponse.Row FindRow(IReadOnlyList<ReplyModeSyncResponse.Row> rows, string profileId)
    {
        if (rows == null) return null;
        foreach (var r in rows)
            if (r.ProfileId == profileId) return r;
        return null;
    }

    /// <summary>A deleted bot's desires must never be sent again (a resumed send would recreate wiped server rows).</summary>
    public static void ForgetBot(Model m, string botId) => m.entries.RemoveAll(e => e.botId == botId);

    /// <summary>The full local wipe: nothing may survive for a later bot that reuses a slot name.</summary>
    public static void ForgetAll(Model m) => m.entries.Clear();

    /// <summary>Each (bot, chat) with an unconfirmed choice, once — what a freshly loaded queue announces to the UI.</summary>
    public static List<(string botId, string chatId)> PendingScopes(Model m)
    {
        var scopes = new List<(string botId, string chatId)>();
        foreach (var e in m.entries)
            if (e.IsPending && !scopes.Contains((e.botId, e.chatId))) scopes.Add((e.botId, e.chatId));
        return scopes;
    }

    /// <summary>Groups due entries that can travel in one request (same chat, value and version).</summary>
    public static List<List<Entry>> Batches(List<Entry> due)
    {
        var groups = new Dictionary<string, List<Entry>>();
        var order = new List<string>();
        foreach (var e in due)
        {
            string k = e.chatId + "|" + e.desiredSuppressed + "|" + e.desiredVersion;
            if (!groups.TryGetValue(k, out var list)) { list = new List<Entry>(); groups[k] = list; order.Add(k); }
            list.Add(e);
        }
        var result = new List<List<Entry>>();
        foreach (var k in order) result.Add(groups[k]);
        return result;
    }

    // ---- persistence (JSON) ----

    public static string Serialize(Model m) => JsonConvert.SerializeObject(m);

    /// <summary>Reload for a new launch: parked/pending desires are retried from attempt 0.</summary>
    public static Model Deserialize(string json, long nowMs)
    {
        Model m = null;
        try { if (!string.IsNullOrEmpty(json)) m = JsonConvert.DeserializeObject<Model>(json); }
        catch { m = null; }
        m ??= new Model();
        m.entries ??= new List<Entry>();
        foreach (var e in m.entries)
        {
            if (e.state == StateFailed || e.state == StatePending)
            {
                e.state = StatePending;
                e.attempts = 0;
                e.nextAttemptAtMs = nowMs;
            }
        }
        return m;
    }
}

/// <summary>Pure reader of the Set Reply Mode v2 confirmation envelope.</summary>
public static class ReplyModeSyncResponse
{
    public class Row
    {
        public string ProfileId;
        public bool Applied;
        public bool Suppressed;
        public long Version;
    }

    public class Result
    {
        public bool Success;
        public bool AllApplied;
        public string Error;
        public List<Row> Applied = new List<Row>();
    }

    public static bool TryParse(string json, out Result result)
    {
        result = null;
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            var jo = JObject.Parse(json);
            var r = new Result
            {
                Success = jo.Value<bool?>("success") == true,
                AllApplied = jo.Value<bool?>("allApplied") == true,
                Error = jo.Value<string>("error"),
            };
            if (jo["applied"] is JArray arr)
                foreach (var t in arr)
                    r.Applied.Add(new Row
                    {
                        ProfileId = t.Value<string>("profileId"),
                        Applied = t.Value<bool?>("applied") == true,
                        Suppressed = t.Value<bool?>("suppressed") == true,
                        Version = t.Value<long?>("version") ?? 0,
                    });
            result = r;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>RU copy for the sync state a bot card / pill shows while a mode change is unconfirmed.</summary>
public static class ReplyModeSyncCopy
{
    public const string Pending = "Сохраняем режим…";
    public const string Failed = "Режим не сохранён";
}

/// <summary>
/// R5 (2026-09-27): the queue resumes on every cold start, but its first send waits until the boot sequence has
/// initialised billing and finished the auth session op (Manager opens the gate) — otherwise the request would go
/// out on the device-id fallback identity. A boot that never opens it (an exception, a hung session op) cannot strand
/// the queue: the gate opens by itself after <see cref="SafetySeconds"/>. Once open it stays open for the launch.
/// </summary>
public static class ReplyModeSendGate
{
    public const float SafetySeconds = 20f;

    public static bool CanSend(bool opened, float secondsSinceBoot) => opened || secondsSinceBoot >= SafetySeconds;
}
