/// <summary>
/// «Личные и рабочие чаты» (spec §5.5, §4.5): the «Сделать чат рабочим?» sheet's text — what the assistant will do once the
/// chat is working. Pure: the caller says whether «Авто» holds in the chat (the chat's own mode with the bot's schedule and
/// quota), whether the last message is the client's and how old it is.
///   • «Авто», the client's message waiting and fresh (≤ <see cref="ChatAudienceRule.FreshHours"/>) — the server hands that
///     message to the assistant again: «…сразу ответит на последнее сообщение…», and the small note says an older one would
///     be skipped;
///   • «Авто», the client's message waiting but older — it is skipped: «…будет отвечать сам на новые сообщения…» + the note;
///   • «Авто», the owner wrote last — nobody waits: the text about new messages, no note;
///   • «Авто» off — the assistant only suggests: no message is answered or skipped, no note.
/// Every string is <see cref="PersonalChatsCopy"/>'s.
/// </summary>
public static class MakeBusinessCopy
{
    private const double SecondsPerHour = 3600d;

    /// <summary>The sheet's body and its note (null — no note).</summary>
    public static (string Body, string Note) For(bool autoOn, bool lastIsClient, double lastAgeHours)
    {
        if (!autoOn) return (PersonalChatsCopy.MakeBusinessManual, null);
        if (!lastIsClient) return (PersonalChatsCopy.MakeBusinessAutoNoFresh, null);
        // NaN compares false: an age nobody could compute is never fresh.
        string body = lastAgeHours <= ChatAudienceRule.FreshHours
            ? PersonalChatsCopy.MakeBusinessAutoFresh
            : PersonalChatsCopy.MakeBusinessAutoNoFresh;
        return (body, PersonalChatsCopy.MakeBusinessStaleNote);
    }

    /// <summary>
    /// How old the chat's last message is, in hours (unix seconds, as <see cref="ChatViewModel.LastMessageTime"/> holds it).
    /// A missing time (≤ 0) is infinitely old: no promise of an answer is made on a time the list did not give.
    /// </summary>
    public static double AgeHours(long lastMessageUnixSec, long nowUnixSec) =>
        lastMessageUnixSec <= 0 ? double.PositiveInfinity : (nowUnixSec - lastMessageUnixSec) / SecondsPerHour;
}
