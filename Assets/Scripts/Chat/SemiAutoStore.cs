using System;

/// <summary>
/// Per-chat semi-auto state persistence (SEMI-02). Keyed <c>{botId}_semiAuto_{chatId}</c>.
/// Stored as a TRI-STATE so a chat can inherit the bot's default reply mode:
/// 0 = no per-chat override (follow the bot default), 1 = explicit OFF, 2 = explicit ON.
/// A chat with no override follows <see cref="ReplyModeToggleBinder"/>'s per-bot default
/// (Auto/Semi); toggling a chat records an explicit override for that conversation only.
///
/// Pure key/value utility — callers pass botId/chatId in (botId = ChatManager.CurrentBotId,
/// chatId = ChatManager.CurrentChatId); the store never reaches into ChatManager. The get/set
/// and bot-default seams default to PlayerPrefs / the reply-mode binder but are swappable so
/// EditMode tests don't pollute the editor registry (mirrors OutboxStore's injected Func seam).
/// Orphaned keys on bot delete are explicitly accepted this milestone (no enumeration/cleanup).
/// </summary>
public static class SemiAutoStore
{
    // Injectable seams: default to PlayerPrefs / the reply-mode binder; tests substitute in-memory.
    internal static Func<string, int> GetInt = key => UnityEngine.PlayerPrefs.GetInt(key, 0);
    internal static Action<string, int> SetIntAndSave = (key, v) =>
    {
        UnityEngine.PlayerPrefs.SetInt(key, v);
        UnityEngine.PlayerPrefs.Save();   // mobile apps get killed — flush (bot-persistence skill)
    };
    // Bot-wide default fallback: true when the bot's reply-mode default is Semi (Вместе).
    internal static Func<string, bool> BotDefaultSemi = botId =>
        ReplyModeToggleBinder.GetMode(botId) == ReplyModeToggleBinder.ReplyMode.Semi;

    public static string Key(string botId, string chatId) => $"{botId}_semiAuto_{chatId}";

    // 0 = unset (follow bot default), 1 = explicit OFF, 2 = explicit ON.
    public static bool IsOn(string botId, string chatId)
    {
        int raw = GetInt(Key(botId, chatId));
        if (raw == 2) return true;            // explicit per-chat ON
        if (raw == 1) return false;           // explicit per-chat OFF
        return BotDefaultSemi(botId);         // no override → inherit the bot's default
    }

    public static void Set(string botId, string chatId, bool on)
        => SetIntAndSave(Key(botId, chatId), on ? 2 : 1);   // record an explicit per-chat override
}
