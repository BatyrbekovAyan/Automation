using System.Collections.Generic;

/// <summary>
/// Detects a <c>messages/get</c> response that belongs to a different chat than the one
/// requested — the symptom of Wappi's confirmed concurrent-request response crossing (the
/// same server bug that forces media downloads to run strictly serial). Every message in a
/// single-chat response carries the chat's id in <see cref="RawMessage.chatId"/>, so a
/// crossed payload is detectable: its messages name a foreign chat and never the requested one.
/// </summary>
public static class CrossChatResponseGuard
{
    /// <summary>
    /// True only when the response clearly belongs to a DIFFERENT chat: no message names the
    /// requested chat AND at least one names a foreign chat. Deliberately conservative —
    /// messages with an empty/absent <c>chatId</c> are ignored (never discard on missing data),
    /// and a response is kept as soon as a single message confirms the requested chat, so one
    /// anomalous entry can't drop a legitimate page.
    /// </summary>
    public static bool IsForDifferentChat(IReadOnlyList<RawMessage> messages, string requestedChatId)
    {
        if (messages == null || messages.Count == 0 || string.IsNullOrEmpty(requestedChatId))
            return false;

        bool anyMatch = false;
        bool anyForeign = false;

        for (int i = 0; i < messages.Count; i++)
        {
            RawMessage raw = messages[i];
            if (raw == null || string.IsNullOrEmpty(raw.chatId)) continue;

            if (raw.chatId == requestedChatId) anyMatch = true;
            else anyForeign = true;
        }

        return !anyMatch && anyForeign;
    }
}
