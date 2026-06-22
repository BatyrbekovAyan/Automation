/// <summary>
/// Converts a WhatsApp chat id into the <c>recipient</c> value the Wappi API expects.
/// 1:1 chats use the bare phone number; groups and everything else keep the full id.
/// </summary>
public static class WappiRecipient
{
    /// <summary>
    /// Returns the Wappi-compatible recipient string for the given chat id.
    /// </summary>
    /// <param name="chatId">The WhatsApp chat id (e.g. "79995579399@c.us" or "120363012345@g.us").</param>
    /// <returns>Bare phone number for @c.us chats; full id otherwise; null/empty passed through.</returns>
    public static string FromChatId(string chatId)
    {
        if (string.IsNullOrEmpty(chatId)) return chatId;
        return chatId.EndsWith("@c.us") ? chatId.Replace("@c.us", "") : chatId;
    }
}
