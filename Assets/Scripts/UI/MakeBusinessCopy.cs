using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.5, §4.5): the «Сделать чат рабочим?» sheet's text — what the assistant will do once the
/// chat is working. Pure: the caller says whether «Авто» holds in the chat (the chat's own mode with the bot's schedule and
/// quota), whether the bot's settings left the chat's channel on, whether the last message is the client's, whether the
/// server would hand it to the assistant again (<see cref="IsReplayable"/>) and how old it is.
///   • «Авто», the client's message waiting, of a type the server hands on, and fresh (≤ <see cref="ChatAudienceRule.FreshHours"/>)
///     — the server hands that message to the assistant again: «…сразу ответит на последнее сообщение…», and the small note
///     says an older one would be skipped;
///   • «Авто», the client's message waiting but older — it is skipped: «…будет отвечать сам на новые сообщения…» + the note;
///   • «Авто», the client's last message one the server never hands on (a voice message, a reaction) — the text about new
///     messages, no note: its age is not why it goes unanswered;
///   • «Авто», the owner wrote last — nobody waits: the text about new messages, no note;
///   • «Авто», but the channel switched off in the bot's settings — nobody answers there by itself: says so, no note;
///   • «Авто» off — the assistant only suggests: no message is answered or skipped, no note.
/// Every string is <see cref="PersonalChatsCopy"/>'s.
/// </summary>
public static class MakeBusinessCopy
{
    private const double SecondsPerHour = 3600d;

    /// <summary>
    /// The message types the server hands to the assistant again when a chat turns working — the words of a messages/get
    /// row: text (WhatsApp «chat», Telegram «text») and the media the assistant answers. The server's own list is REPLAY_TYPES
    /// in Tools/n8n/build-chat-audience.py (MakeBusinessCopyTests compares the two): never a voice message («ptt» — the
    /// assistant transcribes the incoming webhook's file, which a messages/get row does not carry), a reaction, a poll, a
    /// call or a service row.
    /// </summary>
    public static readonly IReadOnlyList<string> ReplayTypes = new ReadOnlyCollection<string>(new[]
    {
        "chat", "text", "image", "video", "document", "audio", "sticker", "location", "vcard",
    });

    /// <summary>
    /// Whether the server would hand the chat's last message to the assistant again, by the chat list's word for its type
    /// (<see cref="ChatViewModel.LastMessageType"/>). The chat list names each type the server replays by the same word as a
    /// messages/get row; a word the server's list does not name promises nothing — a voice message («ptt», or «voice» as the
    /// row preview also reads it), a reaction, the preview's other names («photo», «contact»), an empty or unknown word.
    /// Matched exactly, as the server compares.
    /// </summary>
    public static bool IsReplayable(string lastMessageType)
    {
        if (string.IsNullOrEmpty(lastMessageType)) return false;
        for (int i = 0; i < ReplayTypes.Count; i++)
            if (string.Equals(ReplayTypes[i], lastMessageType, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Whether the server would hand the open chat's newest message to the assistant again, by the type its live list holds
    /// (<see cref="MessageTypeParser"/> reads the same messages/get words): text and the media the server replays. A voice
    /// message, a reaction and a type the app does not know (a location, a contact card among them) promise nothing.
    /// </summary>
    public static bool IsReplayable(MessageType type) =>
        type == MessageType.Chat || type == MessageType.Image || type == MessageType.Video || type == MessageType.Audio
        || type == MessageType.Sticker || type == MessageType.Document;

    /// <summary>The chat's last message as the sheet judges it: the client's or the owner's, one the server hands on, when.</summary>
    public readonly struct LastMessageFacts
    {
        public LastMessageFacts(bool isClient, bool replayable, long unixSec)
        {
            IsClient = isClient;
            Replayable = replayable;
            UnixSec = unixSec;
        }

        public bool IsClient { get; }
        public bool Replayable { get; }
        public long UnixSec { get; }
    }

    /// <summary>
    /// The chat's last message: the newer of the chat list ROW's last message and the open chat's newest message
    /// (<paramref name="openChatNewest"/>, null when the chat is not open). The row follows the chat list's syncs only, while
    /// the open chat's live list also holds the app's own sends (put in at once) and what its live poll found — the owner's
    /// messages from the phone, the client's replies — so after the owner wrote in the open chat the row would still name
    /// the client and promise an answer the server never gives. On the same second, the open chat's.
    /// </summary>
    public static LastMessageFacts LastMessage(ChatViewModel row, MessageViewModel openChatNewest)
    {
        var fromRow = row == null
            ? new LastMessageFacts(false, false, 0)
            : new LastMessageFacts(!row.IsLastMessageMine, IsReplayable(row.LastMessageType), row.LastMessageTime);
        if (openChatNewest == null || openChatNewest.timestamp < fromRow.UnixSec) return fromRow;
        return new LastMessageFacts(openChatNewest.isIncoming, IsReplayable(openChatNewest.type), openChatNewest.timestamp);
    }

    /// <summary>The sheet's body and its note (null — no note).</summary>
    public static (string Body, string Note) For(bool autoOn, bool channelOn, bool lastIsClient, bool lastReplayable, double lastAgeHours)
    {
        if (!autoOn) return (PersonalChatsCopy.MakeBusinessManual, null);
        if (!channelOn) return (PersonalChatsCopy.MakeBusinessChannelOff, null);
        // Nobody waits (the owner wrote last), or the waiting message is one the server never hands on — the assistant takes
        // the next one, and no age is the reason.
        if (!lastIsClient || !lastReplayable) return (PersonalChatsCopy.MakeBusinessAutoNoFresh, null);
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
