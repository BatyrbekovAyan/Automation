using System.Collections.Generic;

/// <summary>
/// Detects a <c>messages/get</c> response that belongs to a different chat than the one
/// requested — the symptom of Wappi's confirmed concurrent-request response crossing (the
/// same server bug that forces media downloads to run strictly serial). Every message in a
/// single-chat response carries the chat's id in <see cref="RawMessage.chatId"/>, so a
/// crossed payload is detectable: its messages name a foreign chat and never the requested one.
/// A WhatsApp chat's rows may carry its @lid code instead of its id («Личные и рабочие чаты»,
/// live check «@lid и номер — один id»): <see cref="ChatIdentity"/> says which ids name the chat.
/// </summary>
public static class CrossChatResponseGuard
{
    /// <summary>
    /// True only when the response clearly belongs to a DIFFERENT chat: no message names the
    /// requested chat — by its id or by the code that counts (<see cref="ChatIdentity.Names"/>) —
    /// AND at least one names a foreign chat. Deliberately conservative — messages with an
    /// empty/absent <c>chatId</c> are ignored (never discard on missing data), and a response is
    /// kept as soon as a single message confirms the requested chat, so one anomalous entry can't
    /// drop a legitimate page.
    /// </summary>
    public static bool IsForDifferentChat(IReadOnlyList<RawMessage> messages, ChatIdentity chat)
    {
        if (messages == null || messages.Count == 0 || string.IsNullOrEmpty(chat.ChatId))
            return false;

        bool anyMatch = false;
        bool anyForeign = false;

        for (int i = 0; i < messages.Count; i++)
        {
            RawMessage raw = messages[i];
            if (raw == null || string.IsNullOrEmpty(raw.chatId)) continue;

            if (chat.Names(raw.chatId)) anyMatch = true;
            else anyForeign = true;
        }

        return !anyMatch && anyForeign;
    }

    /// <summary>
    /// The chat known by its id alone — no code counts (a Telegram chat, a chat the list does not
    /// hold): exactly the rule this guard had before chats had codes.
    /// </summary>
    public static bool IsForDifferentChat(IReadOnlyList<RawMessage> messages, string requestedChatId) =>
        IsForDifferentChat(messages, ChatIdentity.ByIdAlone(requestedChatId));
}
