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
/// <c>isDeleted</c> are skipped (ParseChatsJson hides them). «Личные и рабочие
/// чаты»: «новых» counts the unread messages of WORKING chats only — every chat is
/// drawn the way the list draws it (<see cref="ChatAudienceStore.ViewOf"/> under the
/// channel's profile), and a personal chat is muted in the app. Any read/parse
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

    private static readonly ChatChannel[] Channels = { ChatChannel.WhatsApp, ChatChannel.Telegram };

    public static Stats Read(string botId)
    {
        string safeId = ChatManager.SanitizeBotId(botId);
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(botId) : null;
        string ownWhatsappNumber = PlayerPrefs.GetString(botId + "WhatsappNumber", "");
        int chats = 0;
        int unread = 0;

        foreach (ChatChannel channel in Channels)
        {
            string path = Path.Combine(Application.persistentDataPath, "BotCache", safeId,
                ChannelCachePath.SubDir(channel), "chats.json");
            if (!File.Exists(path)) continue;

            try
            {
                var response = JsonUtility.FromJson<ChatsResponse>(File.ReadAllText(path));
                Stats channelStats = Tally(response, channel, ProfileIdOf(bot, channel), ownWhatsappNumber);
                chats += channelStats.ChatCount;
                unread += channelStats.UnreadCount;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[BotChatStats] Failed to read {path}: {e.Message}");
            }
        }

        return new Stats(chats, unread);
    }

    /// <summary>
    /// One channel's cached list: every chat it shows (a deleted one is no chat), and the unread messages of its working
    /// chats. <paramref name="profileId"/> is the channel's profile — the key the audience decisions are kept under;
    /// <paramref name="ownWhatsappNumber"/> finds the chat with oneself on WhatsApp.
    /// </summary>
    public static Stats Tally(ChatsResponse response, ChatChannel channel, string profileId, string ownWhatsappNumber)
    {
        if (response?.dialogs == null) return default;

        int chats = 0;
        int unread = 0;
        foreach (ChatDialog dialog in response.dialogs)
        {
            if (dialog == null || dialog.isDeleted) continue;
            chats++;
            if (dialog.unread_count > 0 && !PersonalChatsListModel.IsPersonal(ViewOf(dialog, channel, profileId, ownWhatsappNumber)))
                unread += dialog.unread_count;
        }
        return new Stats(chats, unread);
    }

    // The chat as the list draws it: the same key, facts and last-message time ParseChatsJson gives its row.
    private static AudienceView ViewOf(ChatDialog dialog, ChatChannel channel, string profileId, string ownWhatsappNumber)
    {
        string chatKey = ChatIdFormat.CanonicalKey(dialog.id, channel);
        AudienceFacts facts = ChatAudienceFacts.From(dialog, channel, chatKey, ownWhatsappNumber);
        long lastMessageTime = ChatDialogTime.Resolve(dialog.last_timestamp, dialog.last_time);
        var chat = new ChatViewModel(chatKey, dialog.name, null, null, lastMessageTime, facts: facts);
        return ChatAudienceStore.ViewOf(profileId, chat);
    }

    private static string ProfileIdOf(Bot bot, ChatChannel channel)
    {
        if (bot == null) return null;
        string profileId = channel == ChatChannel.Telegram ? bot.telegramProfileId : bot.whatsappProfileId;
        return string.IsNullOrEmpty(profileId) || profileId == Bot.UnauthedProfileSentinel ? null : profileId;
    }
}
