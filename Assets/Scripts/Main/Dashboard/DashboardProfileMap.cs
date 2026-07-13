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
    // TDD scaffold — implemented in the GREEN commit.
    public static List<string> AuthedProfiles(IEnumerable<BotProfiles> bots) => new List<string>();

    public static Dictionary<string, DashboardProfileRef> ProfileToBot(IEnumerable<BotProfiles> bots)
        => new Dictionary<string, DashboardProfileRef>();

    public static List<(string botName, HashSet<string> profileIds)> BotChips(IEnumerable<BotProfiles> bots)
        => new List<(string botName, HashSet<string> profileIds)>();

    public static bool TryResolve(IReadOnlyDictionary<string, DashboardProfileRef> map,
                                  string profileId, out string botName, out ChatChannel channel)
    {
        botName = null;
        channel = ChatChannel.WhatsApp;
        return false;
    }
}
