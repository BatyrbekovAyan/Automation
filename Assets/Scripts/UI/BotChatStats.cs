using System.IO;
using UnityEngine;

/// <summary>
/// Reads a bot's cached chat statistics straight from its on-disk chat-list
/// caches — BOTH channels' files (BotCache/{botId}/chats.json + the telegram
/// sub-dir, per <see cref="ChannelCachePath"/>) — so Sheet_BotSwitcher can show
/// «N чатов · M новых» for every bot without touching ChatManager's live state,
/// which only ever holds the ACTIVE bot's ACTIVE channel.
///
/// Counting mirrors the visible list's one hide rule: chats with a sticky
/// <c>isDeleted</c> are skipped (ParseChatsJson hides them). Any read/parse
/// failure degrades to zeros — the subline then just omits the counts.
/// </summary>
public static class BotChatStats
{
    public readonly struct Stats
    {
        public readonly int ChatCount;
        public readonly int UnreadCount;

        public Stats(int chatCount, int unreadCount)
        {
            ChatCount = chatCount;
            UnreadCount = unreadCount;
        }
    }

    private static readonly string[] ChannelSubDirs = { "", ChannelCachePath.TelegramSubDir };

    public static Stats Read(string botId)
    {
        string safeId = ChatManager.SanitizeBotId(botId);
        int chats = 0;
        int unread = 0;

        foreach (string subDir in ChannelSubDirs)
        {
            string path = Path.Combine(Application.persistentDataPath, "BotCache", safeId, subDir, "chats.json");
            if (!File.Exists(path)) continue;

            try
            {
                var response = JsonUtility.FromJson<ChatsResponse>(File.ReadAllText(path));
                if (response?.dialogs == null) continue;

                foreach (ChatDialog dialog in response.dialogs)
                {
                    if (dialog == null || dialog.isDeleted) continue;
                    chats++;
                    unread += Mathf.Max(0, dialog.unread_count);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[BotChatStats] Failed to read {path}: {e.Message}");
            }
        }

        return new Stats(chats, unread);
    }
}
