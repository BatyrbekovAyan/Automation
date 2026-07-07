using System.Collections.Generic;

/// <summary>Maps a WhatsApp profile id (from an outcome row) to the owning bot's
/// GameObject name. The controller builds the dictionary from live bots.</summary>
public static class SessionChatMap
{
    public static string ResolveBotName(IReadOnlyDictionary<string, string> profileToBot, string profileId)
    {
        if (profileToBot == null || string.IsNullOrEmpty(profileId)) return null;
        return profileToBot.TryGetValue(profileId, out var name) ? name : null;
    }
}
