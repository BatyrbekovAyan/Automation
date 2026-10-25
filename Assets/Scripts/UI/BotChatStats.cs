using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Reads a bot's cached chat statistics straight from its on-disk chat-list
/// caches — BOTH channels' files (BotCache/{botId}/chats.json + the telegram
/// sub-dir, per <see cref="ChannelCachePath"/>) — so Sheet_BotSwitcher can show
/// «N чатов · M новых» for every bot without touching ChatManager's live state,
/// which only ever holds the ACTIVE bot's ACTIVE channel.
///
/// Counting mirrors the visible list's hide rules (<see cref="ShownChats"/>): a chat
/// with a sticky <c>isDeleted</c>, an id that cannot belong to the channel and a second
/// id-form of a chat already listed are skipped, as ParseChatsJson skips them.
/// «Личные и рабочие чаты»: «новых» counts the unread messages of WORKING chats only —
/// every chat is drawn the way the list draws it (<see cref="ChatAudienceStore.ViewOf"/>
/// under the channel's profile), and a personal chat is muted in the app. Any read/parse
/// failure degrades to zeros — the subline then just omits the counts.
/// The same reading feeds <see cref="PersonalChatsCount"/> (the bot settings' «Личные чаты»).
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

    /// <summary>Both channels, in the order their counts are added up.</summary>
    internal static readonly ChatChannel[] Channels = { ChatChannel.WhatsApp, ChatChannel.Telegram };

    private static string _cacheRootOverride;

    private static string CacheRoot => _cacheRootOverride ?? Path.Combine(Application.persistentDataPath, "BotCache");

    public static Stats Read(string botId)
    {
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(botId) : null;
        string ownWhatsappNumber = OwnWhatsappNumber(botId);
        int chats = 0;
        int unread = 0;

        foreach (ChatChannel channel in Channels)
        {
            if (!TryReadCache(botId, channel, out ChatsResponse response)) continue;
            Stats channelStats = Tally(response, channel, ProfileIdOf(bot, channel), ownWhatsappNumber);
            chats += channelStats.ChatCount;
            unread += channelStats.UnreadCount;
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
        int chats = 0;
        int unread = 0;
        foreach ((ChatDialog dialog, string chatKey) in ShownChats(response, channel))
        {
            chats++;
            if (dialog.unread_count > 0
                && !PersonalChatsListModel.IsPersonal(ViewOf(dialog, chatKey, channel, profileId, ownWhatsappNumber)))
                unread += dialog.unread_count;
        }
        return new Stats(chats, unread);
    }

    /// <summary>
    /// The chats one channel's cached list shows, with the key the list gives each row — ParseChatsJson's hide rules: a deleted
    /// chat is no chat, an id that cannot belong to the channel leaked from the other one, and a second id-form of a chat already
    /// listed is that chat again.
    /// </summary>
    internal static IEnumerable<(ChatDialog dialog, string chatKey)> ShownChats(ChatsResponse response, ChatChannel channel)
    {
        if (response?.dialogs == null) yield break;
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (ChatDialog dialog in response.dialogs)
        {
            if (dialog == null || dialog.isDeleted || string.IsNullOrEmpty(dialog.id)) continue;
            if (ChatIdFormat.IsForeignToChannel(dialog.id, channel)) continue;
            string chatKey = ChatIdFormat.CanonicalKey(dialog.id, channel);
            if (listed.Add(chatKey)) yield return (dialog, chatKey);
        }
    }

    /// <summary>The chat as the list draws it: the same key, facts and last-message time ParseChatsJson gives its row.</summary>
    internal static AudienceView ViewOf(ChatDialog dialog, string chatKey, ChatChannel channel, string profileId, string ownWhatsappNumber)
    {
        AudienceFacts facts = ChatAudienceFacts.From(dialog, channel, chatKey, ownWhatsappNumber);
        long lastMessageTime = ChatDialogTime.Resolve(dialog.last_timestamp, dialog.last_time);
        var chat = new ChatViewModel(chatKey, dialog.name, null, null, lastMessageTime, facts: facts);
        return ChatAudienceStore.ViewOf(profileId, chat);
    }

    /// <summary>The bot's real profile on the channel — null when it holds none (never connected, retired, the sentinel).</summary>
    internal static string ProfileIdOf(Bot bot, ChatChannel channel)
    {
        if (bot == null) return null;
        string profileId = channel == ChatChannel.Telegram ? bot.telegramProfileId : bot.whatsappProfileId;
        return string.IsNullOrEmpty(profileId) || profileId == Bot.UnauthedProfileSentinel ? null : profileId;
    }

    /// <summary>The bot's own WhatsApp number — what tells the chat with oneself on WhatsApp (the key BotSettings writes).</summary>
    internal static string OwnWhatsappNumber(string botId) => PlayerPrefs.GetString(botId + "WhatsappNumber", "");

    /// <summary>The channel's cached list of the bot; false when there is none or it does not read.</summary>
    internal static bool TryReadCache(string botId, ChatChannel channel, out ChatsResponse response)
    {
        response = null;
        string path = Path.Combine(CacheRoot, ChatManager.SanitizeBotId(botId), ChannelCachePath.SubDir(channel), "chats.json");
        if (!File.Exists(path)) return false;
        try
        {
            response = JsonUtility.FromJson<ChatsResponse>(File.ReadAllText(path));
            return response != null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BotChatStats] Failed to read {path}: {e.Message}");
            return false;
        }
    }

    // ---- test seams ----

    /// <summary>Reads the caches under <paramref name="root"/> instead of persistentDataPath/BotCache.</summary>
    internal static void UseCacheRootForTests(string root) => _cacheRootOverride = root;

    internal static void ResetForTests() => _cacheRootOverride = null;
}
