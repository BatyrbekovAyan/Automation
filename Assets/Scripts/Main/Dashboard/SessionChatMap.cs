using System.Collections.Generic;

/// <summary>Maps a WhatsApp profile id (from an outcome row) to the owning bot's
/// GameObject name. The controller builds the dictionary from live bots.
///
/// SUPERSEDED (Phase 7): the last production caller (<c>DashboardPage.OpenChat</c>) now
/// resolves bot + channel across BOTH channels via <see cref="DashboardProfileMap.TryResolve"/>;
/// the only remaining references are SessionChatMapTests. Retained deliberately to keep the
/// Phase-7 diff scoped — cleanup candidate for a later hygiene pass (delete together with
/// SessionChatMapTests).</summary>
public static class SessionChatMap
{
    public static string ResolveBotName(IReadOnlyDictionary<string, string> profileToBot, string profileId)
    {
        if (profileToBot == null || string.IsNullOrEmpty(profileId)) return null;
        return profileToBot.TryGetValue(profileId, out var name) ? name : null;
    }
}
