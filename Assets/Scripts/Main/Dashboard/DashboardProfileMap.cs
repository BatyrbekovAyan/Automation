using System.Collections.Generic;

/// <summary>
/// A live bot's channel profile ids, flattened for the pure dashboard seam. The
/// controller fills this from live <see cref="Bot"/> fields at runtime; kept a plain
/// struct so the selection/mapping logic below stays testable without a scene.
/// </summary>
public struct BotProfiles
{
    public string botName;
    public string whatsappProfileId;
    public string telegramProfileId;
}

/// <summary>
/// The bot + channel an outcome-row profileId resolves to. <see cref="channel"/> is
/// taken from WHICH profile id matched — never from the server payload (DASH-03 /
/// threat T-07-02-01).
/// </summary>
public struct DashboardProfileRef
{
    public string botName;
    public ChatChannel channel;
}

/// <summary>
/// Pure profile-collection / mapping / chip seam for the «Сводка» dashboard, spanning
/// BOTH channels. Mirrors the pure-seam precedents (SessionChatMap, DashboardTimeFormat):
/// no MonoBehaviour, no scene — the controller feeds it <see cref="BotProfiles"/> built
/// from live bots. A profile id counts only when authed: non-empty and not the "-1"
/// (<see cref="Bot.UnauthedProfileSentinel"/>) sentinel.
/// </summary>
public static class DashboardProfileMap
{
    /// <summary>Authed = a real, claimed profile: non-empty and not the "-1" sentinel.</summary>
    private static bool IsAuthed(string id) =>
        !string.IsNullOrEmpty(id) && id != Bot.UnauthedProfileSentinel;

    /// <summary>
    /// Every authed profile id across all bots, in bot order, WhatsApp then Telegram
    /// per bot. This is the POSTed profileIds list (DASH-01) — now Telegram-inclusive.
    /// </summary>
    public static List<string> AuthedProfiles(IEnumerable<BotProfiles> bots)
    {
        var list = new List<string>();
        if (bots == null) return list;
        foreach (var b in bots)
        {
            if (IsAuthed(b.whatsappProfileId)) list.Add(b.whatsappProfileId);
            if (IsAuthed(b.telegramProfileId)) list.Add(b.telegramProfileId);
        }
        return list;
    }

    /// <summary>
    /// profileId → (botName, channel) for every authed id. Profile ids are globally
    /// unique GUIDs, so a bot's two channels never collide. Drives the deep-link's
    /// channel resolution and the row bot-tag lookup.
    /// </summary>
    public static Dictionary<string, DashboardProfileRef> ProfileToBot(IEnumerable<BotProfiles> bots)
    {
        var map = new Dictionary<string, DashboardProfileRef>();
        if (bots == null) return map;
        foreach (var b in bots)
        {
            if (IsAuthed(b.whatsappProfileId))
                map[b.whatsappProfileId] =
                    new DashboardProfileRef { botName = b.botName, channel = ChatChannel.WhatsApp };
            if (IsAuthed(b.telegramProfileId))
                map[b.telegramProfileId] =
                    new DashboardProfileRef { botName = b.botName, channel = ChatChannel.Telegram };
        }
        return map;
    }

    /// <summary>
    /// One chip per bot that has ≥1 authed profile, in bot order; the chip's set is
    /// that bot's authed id(s) (1 or 2). A dual-channel bot ⇒ ONE chip covering both
    /// profiles (DASH-02) — never two same-named chips.
    /// </summary>
    public static List<(string botName, HashSet<string> profileIds)> BotChips(IEnumerable<BotProfiles> bots)
    {
        var chips = new List<(string botName, HashSet<string> profileIds)>();
        if (bots == null) return chips;
        foreach (var b in bots)
        {
            var ids = new HashSet<string>();
            if (IsAuthed(b.whatsappProfileId)) ids.Add(b.whatsappProfileId);
            if (IsAuthed(b.telegramProfileId)) ids.Add(b.telegramProfileId);
            if (ids.Count > 0) chips.Add((b.botName, ids));
        }
        return chips;
    }

    /// <summary>
    /// Resolve an outcome row's profileId to its owning bot + channel via the LOCAL
    /// map. Returns false (with WhatsApp/null defaults) on a null/unknown/forged id, so
    /// the caller early-returns — a server value can never force navigation off-device
    /// (threat T-07-02-01). The channel comes from WHICH local entry matched.
    /// </summary>
    public static bool TryResolve(IReadOnlyDictionary<string, DashboardProfileRef> map,
                                  string profileId, out string botName, out ChatChannel channel)
    {
        botName = null;
        channel = ChatChannel.WhatsApp;
        if (map == null || string.IsNullOrEmpty(profileId)) return false;
        if (!map.TryGetValue(profileId, out var pref)) return false;
        botName = pref.botName;
        channel = pref.channel;
        return true;
    }
}
