using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>What one channel status check says about the device's «Канал отключён» mark.</summary>
public enum ChannelHealthVerdict
{
    /// <summary>The answer proves nothing (an error, no answer, a refusal, a body that is not JSON, no boolean flag, another
    /// profile's status) — the mark stays as it is.</summary>
    Keep,
    /// <summary>A 2xx whose body carries the boolean <c>authorized: false</c> — the channel is marked.</summary>
    Unauthorized,
    /// <summary>A 2xx whose body carries the boolean <c>authorized: true</c> — the mark is removed.</summary>
    Authorized,
}

/// <summary>
/// A4 «защита платящих» (2026-10-14; spec §2.2, §3 A4): a paying owner's WhatsApp/Telegram logs out (the phone dropped the
/// linked device — an ordinary event) and the bot falls silent while the card still says «connected». At launch and on every
/// return to the app (at most once a minute) <c>Manager.ChannelHealthRoutine</c> asks the proxy's <c>status</c> op for every
/// profile the session token lists; a positive <c>authorized:false</c> marks the channel — the card's subline says «Канал
/// отключён · переподключите в настройках» and its icon turns gray. NOTHING is deleted, logged out or switched off here.
/// The logged-out profile itself is not re-paired: the owner reconnects in the bot's settings, whose status probe on opening
/// (<c>BotSettings.CheckWhatsappUnauthorizationOutsideApp</c> / its Telegram twin) deletes a profile Wappi reports
/// <c>authorized:false</c>, and the owner pairs a FRESH profile — the bot's line (F1: Create's Clone Plan + Bind) carries the
/// bot's data over to it. The mark goes with a status that proves <c>authorized:true</c> (this routine, and since fix round 2
/// the settings' own status checks — <see cref="UnmarkIfAuthorized"/>), with a confirmed pairing (Manager's auth pollers →
/// <see cref="UnmarkProfile"/>) and with the bot (<c>Bot.DeleteBotLocally</c>). The server half (crChannelWatch001) tells the
/// owner through another channel; the orphan sweep no longer deletes such a profile.
///
/// Keys follow the per-bot convention (bot-persistence): <c>{bot}WhatsappUnauthorized</c> / <c>{bot}TelegramUnauthorized</c>
/// (int: 1 = marked, absent = not), prefixed with the bot GameObject's name. Static Func/Action seams (the IdentityHealLatch
/// pattern) let EditMode tests use an in-memory store. Pure decisions + the one store; EditMode-pinned (ChannelHealthTests).
/// </summary>
public static class ChannelHealth
{
    public const string WhatsappSuffix = "WhatsappUnauthorized";
    public const string TelegramSuffix = "TelegramUnauthorized";

    /// <summary>A launch and a return to the app check at most once a minute.</summary>
    public const long MinIntervalMs = 60L * 1000;

    /// <summary>A channel of a bot was marked or unmarked (argument: the bot key) — its card repaints.</summary>
    public static event Action<string> Changed;

    internal static Func<string, int, int> GetInt = PlayerPrefs.GetInt;
    internal static Action<string, int> SetInt = PlayerPrefs.SetInt;
    internal static Action<string> DeleteKey = PlayerPrefs.DeleteKey;
    internal static Action Save = PlayerPrefs.Save;

    internal static void ResetSeamsForTests()
    {
        GetInt = PlayerPrefs.GetInt;
        SetInt = PlayerPrefs.SetInt;
        DeleteKey = PlayerPrefs.DeleteKey;
        Save = PlayerPrefs.Save;
        Changed = null;
    }

    public static string Suffix(ChatChannel channel) => channel == ChatChannel.Telegram ? TelegramSuffix : WhatsappSuffix;

    // ---- the decision -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The proxy's <c>status</c> answer (Wappi's status code and body, passed through). <see cref="ChannelHealthVerdict.Unauthorized"/>
    /// ONLY on a 2xx whose JSON object carries the BOOLEAN <c>authorized: false</c> (the server watch's «strictly boolean»
    /// rule); <see cref="ChannelHealthVerdict.Authorized"/> on the boolean <c>true</c>; everything else — a non-2xx (403: a
    /// profile the server retired, 401, 5xx), no answer, an empty or non-JSON body, a string or missing flag — is
    /// <see cref="ChannelHealthVerdict.Keep"/>.
    /// </summary>
    public static ChannelHealthVerdict Judge(long status, string body) => Judge(status, body, null);

    /// <summary>
    /// <see cref="Judge(long, string)"/> for the status of <paramref name="expectedProfileId"/>: a body that names ANOTHER
    /// profile (Wappi crosses concurrent answers) proves nothing about this channel — <see cref="ChannelHealthVerdict.Keep"/>.
    /// A body without a profile id is judged as is.
    /// </summary>
    public static ChannelHealthVerdict Judge(long status, string body, string expectedProfileId)
    {
        JObject jo = status >= 200 && status < 300 ? ParseObject(body) : null;
        JToken flag = jo?["authorized"];
        if (flag == null || flag.Type != JTokenType.Boolean || NamesAnotherProfile(jo, expectedProfileId))
            return ChannelHealthVerdict.Keep;
        return flag.Value<bool>() ? ChannelHealthVerdict.Authorized : ChannelHealthVerdict.Unauthorized;
    }

    private static JObject ParseObject(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JObject.Parse(body); }
        catch (Exception) { return null; }     // not JSON, or JSON that is not an object (an array, a bare value)
    }

    private static bool NamesAnotherProfile(JObject jo, string expectedProfileId)
    {
        if (string.IsNullOrEmpty(expectedProfileId)) return false;
        JToken echoed = jo["profile_id"];
        return echoed != null && echoed.Type != JTokenType.Null
            && !string.Equals(echoed.ToString(), expectedProfileId, StringComparison.Ordinal);
    }

    /// <summary>At launch and on a return to the app: never twice within <see cref="MinIntervalMs"/>; a run time from the
    /// future (the device clock moved back) never blocks.</summary>
    public static bool ShouldRun(long nowMs, long lastRunMs)
        => lastRunMs <= 0 || nowMs - lastRunMs >= MinIntervalMs || nowMs < lastRunMs;

    /// <summary>One bot's channels as the device holds them (the Bot component's current profile ids).</summary>
    public readonly struct BotChannels
    {
        public readonly string BotKey;
        public readonly string WhatsappProfileId;
        public readonly string TelegramProfileId;

        public BotChannels(string botKey, string whatsappProfileId, string telegramProfileId)
        {
            BotKey = botKey;
            WhatsappProfileId = whatsappProfileId;
            TelegramProfileId = telegramProfileId;
        }
    }

    /// <summary>One status check: this bot's channel, holding this profile.</summary>
    public readonly struct Target
    {
        public readonly string BotKey;
        public readonly ChatChannel Channel;
        public readonly string ProfileId;

        public Target(string botKey, ChatChannel channel, string profileId)
        {
            BotKey = botKey;
            Channel = channel;
            ProfileId = profileId;
        }
    }

    /// <summary>
    /// Which channels are checked: a connected channel (a real profile id) whose profile the session token lists — the proxy
    /// answers <c>status</c> only for those (a retired one is not listed: its ids were moved aside, or the server refuses it).
    /// Card order: WhatsApp, then Telegram, bot by bot.
    /// </summary>
    public static List<Target> Targets(IEnumerable<BotChannels> bots, IReadOnlyCollection<string> tokenProfiles)
    {
        var list = new List<Target>();
        if (bots == null || tokenProfiles == null || tokenProfiles.Count == 0) return list;
        var listed = new HashSet<string>(tokenProfiles, StringComparer.Ordinal);
        foreach (var b in bots)
        {
            if (string.IsNullOrEmpty(b.BotKey)) continue;
            if (BotCardModel.IsConnected(b.WhatsappProfileId) && listed.Contains(b.WhatsappProfileId))
                list.Add(new Target(b.BotKey, ChatChannel.WhatsApp, b.WhatsappProfileId));
            if (BotCardModel.IsConnected(b.TelegramProfileId) && listed.Contains(b.TelegramProfileId))
                list.Add(new Target(b.BotKey, ChatChannel.Telegram, b.TelegramProfileId));
        }
        return list;
    }

    /// <summary>
    /// The card says «Канал отключён · …» for a channel the device marked while it is still connected (a real profile id)
    /// and not retired — a retired channel's own subline outranks it.
    /// </summary>
    public static bool CardShowsUnauthorized(bool marked, string profileId, string retiredReason)
        => marked && BotCardModel.IsConnected(profileId) && retiredReason == null;

    // ---- the mark ---------------------------------------------------------------------------------------------------

    public static bool IsMarked(string botKey, ChatChannel channel)
        => !string.IsNullOrEmpty(botKey) && GetInt(KeyOf(botKey, channel), 0) == 1;

    private static string KeyOf(string botKey, ChatChannel channel) => $"{botKey}{Suffix(channel)}";

    /// <summary>
    /// Records a verdict: <see cref="ChannelHealthVerdict.Unauthorized"/> marks, <see cref="ChannelHealthVerdict.Authorized"/>
    /// unmarks, <see cref="ChannelHealthVerdict.Keep"/> changes nothing. Saves at once and raises <see cref="Changed"/> only
    /// when the mark actually changed.
    /// </summary>
    public static void Apply(string botKey, ChatChannel channel, ChannelHealthVerdict verdict)
    {
        if (string.IsNullOrEmpty(botKey) || verdict == ChannelHealthVerdict.Keep) return;
        bool mark = verdict == ChannelHealthVerdict.Unauthorized;
        if (IsMarked(botKey, channel) == mark) return;
        if (mark) SetInt(KeyOf(botKey, channel), 1);
        else DeleteKey(KeyOf(botKey, channel));
        Save();
        Changed?.Invoke(botKey);
    }

    /// <summary>The channel was (re)authorised — by a pairing, a re-auth from the settings, or a status that says so.</summary>
    public static void Unmark(string botKey, ChatChannel channel) => Apply(botKey, channel, ChannelHealthVerdict.Authorized);

    /// <summary>The bot whose <paramref name="channel"/> now holds <paramref name="profileId"/> under <paramref name="botsRoot"/>
    /// was (re)authorised: its mark goes (a pairing confirmed by Manager's auth poller — the wizard and the settings flows).</summary>
    public static void UnmarkProfile(Transform botsRoot, ChatChannel channel, string profileId)
    {
        if (botsRoot == null || !BotCardModel.IsConnected(profileId)) return;
        for (int i = 0; i < botsRoot.childCount; i++)
        {
            var bot = botsRoot.GetChild(i).GetComponent<Bot>();
            if (bot == null) continue;
            string held = channel == ChatChannel.Telegram ? bot.telegramProfileId : bot.whatsappProfileId;
            if (string.Equals(held, profileId, StringComparison.Ordinal)) Unmark(bot.name, channel);
        }
    }

    /// <summary>
    /// A status answer the bot's settings already read for their own reasons — the check on switching a channel on and the
    /// probe on opening them (fix round 2, 2026-10-15): the card heals at once instead of at the next launch / return to the
    /// app. The routine's own verdict decides (<see cref="Judge(long, string, string)"/>: a 2xx with the boolean
    /// <c>authorized:true</c> naming THIS profile), and it only ever unmarks — those checks act on <c>authorized:false</c>
    /// themselves (the probe deletes the profile).
    /// </summary>
    public static void UnmarkIfAuthorized(Transform botsRoot, ChatChannel channel, string profileId, long status, string body)
    {
        if (Judge(status, body, profileId) == ChannelHealthVerdict.Authorized) UnmarkProfile(botsRoot, channel, profileId);
    }

    /// <summary>Deletes both channels' marks of a bot that is being deleted (no event: the card goes with it).</summary>
    public static void Clear(string botKey)
    {
        if (string.IsNullOrEmpty(botKey)) return;
        DeleteKey(KeyOf(botKey, ChatChannel.WhatsApp));
        DeleteKey(KeyOf(botKey, ChatChannel.Telegram));
    }
}
