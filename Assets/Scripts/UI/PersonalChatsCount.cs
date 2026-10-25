using System.Collections.Generic;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.7, sketch 009 screen 7): the number on the bot settings' «Личные чаты» row — the chats the
/// bot's «Личные» folder lists in Browse (every personal chat, the ones that are not people included), on BOTH channels.
///
/// The source is BotChatStats': each channel's cached list (BotCache/{bot}/…/chats.json) under the list's hide rules
/// (<see cref="BotChatStats.ShownChats"/>), every chat drawn the way the list draws it (<see cref="ChatAudienceStore.ViewOf"/>
/// under the channel's profile — the owner's move, else the server's decision, else the shared rule). The one exception is the
/// channel the chats screen shows for the active bot: its live list (ChatManager.Chats) is fresher than its cache and is exactly
/// what the folder lists there. A channel without a live profile counts nothing — the folder has nothing to open on it.
/// </summary>
public static class PersonalChatsCount
{
    /// <summary>The bot's personal chats on both channels; 0 for a bot that does not exist.</summary>
    public static int Read(string botId)
    {
        if (string.IsNullOrEmpty(botId)) return 0;
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(botId) : null;
        string ownWhatsappNumber = BotChatStats.OwnWhatsappNumber(botId);
        ChatManager onScreen = ChatManager.Instance;
        int count = 0;

        foreach (ChatChannel channel in BotChatStats.Channels)
        {
            string profileId = BotChatStats.ProfileIdOf(bot, channel);
            if (profileId == null) continue;
            if (onScreen != null && onScreen.CurrentBotId == botId && onScreen.ActiveChannel == channel)
                count += Live(onScreen.Chats, profileId);
            else if (BotChatStats.TryReadCache(botId, channel, out ChatsResponse cached))
                count += Tally(cached, channel, profileId, ownWhatsappNumber);
        }
        return count;
    }

    /// <summary>
    /// One channel's cached list: its personal chats, each drawn the way the list draws it. <paramref name="profileId"/> is the
    /// channel's profile — the key the audience decisions are kept under; <paramref name="ownWhatsappNumber"/> finds the chat
    /// with oneself on WhatsApp.
    /// </summary>
    public static int Tally(ChatsResponse response, ChatChannel channel, string profileId, string ownWhatsappNumber)
    {
        int count = 0;
        foreach ((ChatDialog dialog, string chatKey) in BotChatStats.ShownChats(response, channel))
            if (PersonalChatsListModel.IsPersonal(BotChatStats.ViewOf(dialog, chatKey, channel, profileId, ownWhatsappNumber)))
                count++;
        return count;
    }

    /// <summary>The personal chats of the list on screen — what the folder lists for that channel.</summary>
    public static int Live(IEnumerable<ChatViewModel> chats, string profileId)
    {
        int count = 0;
        if (chats == null) return count;
        foreach (ChatViewModel chat in chats)
            if (chat != null && PersonalChatsListModel.IsPersonal(ChatAudienceStore.ViewOf(profileId, chat))) count++;
        return count;
    }
}
