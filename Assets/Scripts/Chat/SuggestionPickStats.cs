using UnityEngine;

/// <summary>
/// Preference counters `{botName}SuggestPick{move}` (PlayerPrefs, the closed 6-move taxonomy). Learning A
/// moved the write from the TAP (which also fired a paid drill) to the SUCCESSFUL SEND, and only for a
/// card sent as is or edited — a set the owner ignored is not a preference. Still per bot, per device:
/// the server counts the same thing from chat_history_events.origin_* (B moves the prompt's pickStats there).
/// </summary>
public static class SuggestionPickStats
{
    public static void Record(string botName, string move)
    {
        if (string.IsNullOrEmpty(botName) || !SuggestionMoves.IsMove(move)) return;
        string key = botName + "SuggestPick" + move;
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
        PlayerPrefs.Save();   // mobile apps get killed — flush (bot-persistence)
    }

    public static bool CountsAsPreference(SendOrigin o)
        => o != null && (o.kind == SendOriginKind.Card || o.kind == SendOriginKind.Edited) && SuggestionMoves.IsMove(o.pickedMove);

    public static void RecordFromOrigin(string botName, SendOrigin o)
    {
        if (CountsAsPreference(o)) Record(botName, o.pickedMove);
    }
}
