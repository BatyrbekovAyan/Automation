using System;

/// <summary>
/// Per-chat semi-auto state persistence (SEMI-02). Keyed <c>{botId}_semiAuto_{chatId}</c>,
/// defaults OFF, and is isolated per bot and per chat (a different bot's state is independent).
///
/// Pure key/value utility — callers pass botId/chatId in (botId = ChatManager.CurrentBotId,
/// chatId = ChatManager.CurrentChatId); the store never reaches into ChatManager. The get/set
/// seam defaults to PlayerPrefs but is swappable so EditMode tests don't pollute the editor
/// registry (mirrors OutboxStore's injected Func seam). Orphaned keys on bot delete are
/// explicitly accepted this milestone (no enumeration/cleanup).
/// </summary>
public static class SemiAutoStore
{
    // Injectable seam: defaults to PlayerPrefs; tests substitute an in-memory dictionary.
    internal static Func<string, int> GetInt = key => UnityEngine.PlayerPrefs.GetInt(key, 0);
    internal static Action<string, int> SetIntAndSave = (key, v) =>
    {
        UnityEngine.PlayerPrefs.SetInt(key, v);
        UnityEngine.PlayerPrefs.Save();   // mobile apps get killed — flush (bot-persistence skill)
    };

    public static string Key(string botId, string chatId) => $"{botId}_semiAuto_{chatId}";

    public static bool IsOn(string botId, string chatId)
        => GetInt(Key(botId, chatId)) == 1;            // default OFF (GetInt default arg is 0)

    public static void Set(string botId, string chatId, bool on)
        => SetIntAndSave(Key(botId, chatId), on ? 1 : 0);
}
