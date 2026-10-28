using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// «Личные и рабочие чаты»: whose chat it is. <see cref="Personal"/> — the owner's own: the assistant does not answer,
/// suggest or store anything in it. <see cref="Business"/> — a client: the assistant works as it always did. Whatever
/// cannot be shown to be a client is personal.
/// </summary>
public enum AudienceKind
{
    Personal,
    Business,
}

/// <summary>
/// Which step of the rule decided (<see cref="ChatAudienceRule.Decide"/>). <see cref="Owner"/> is the owner's own choice;
/// the rest are the rule's defaults: not a person at all, a saved contact, a conversation from before the connection, a
/// new person — and «not known», which reads as personal and is never written down.
/// </summary>
public enum AudienceReason
{
    NonHuman,
    Owner,
    Contact,
    History,
    New,
    Unknown,
}

/// <summary>
/// What a chat that is not a person is — the word under its name in the «Личные» folder. <see cref="None"/> is a person.
/// </summary>
public enum NonHumanKind
{
    None,
    Group,
    Channel,
    Bot,
    Service,
    Self,
}

/// <summary>
/// Everything the rule reads about one chat, taken from its chat-list row: the channel, the chat id, the Telegram chat
/// type, the Telegram user flags and whether the chat is a saved contact. A fact that is not known is null, and the rule
/// never reads «not known» as «no» — only a known «no» on both contact and history makes a new person.
///
/// The server's case table writes the Telegram flags as ONE object that is either given or null (the chat card was not
/// received). Here that is <see cref="Bot"/> = <see cref="Support"/> = <see cref="Self"/> = null; a card with any flag
/// given is a card, and the flags missing from it read as false. WhatsApp has no card to miss, so there null flags read
/// as false too. <c>default</c> is «nothing known».
/// </summary>
public readonly struct AudienceFacts : IEquatable<AudienceFacts>
{
    public AudienceFacts(bool telegram, string chatId, string chatType, bool? bot, bool? support, bool? self, bool? contact)
    {
        Telegram = telegram;
        ChatId = chatId;
        ChatType = chatType;
        Bot = bot;
        Support = support;
        Self = self;
        Contact = contact;
    }

    public bool Telegram { get; }

    /// <summary>The chat id as the list keys it: WhatsApp's jid verbatim, Telegram's bare id.</summary>
    public string ChatId { get; }

    /// <summary>The Telegram dialog type (<c>user</c>, <c>chat</c>, <c>channel</c>); WhatsApp does not send one and the rule does not read it.</summary>
    public string ChatType { get; }

    public bool? Bot { get; }
    public bool? Support { get; }
    public bool? Self { get; }

    /// <summary>A saved contact: WhatsApp — a name from the phone's address book; Telegram — the user card's Contact flag.</summary>
    public bool? Contact { get; }

    public bool Equals(AudienceFacts other) =>
        Telegram == other.Telegram && ChatId == other.ChatId && ChatType == other.ChatType
        && Bot == other.Bot && Support == other.Support && Self == other.Self && Contact == other.Contact;

    public override bool Equals(object obj) => obj is AudienceFacts other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Telegram, ChatId, ChatType, Bot, Support, Self, Contact);
}

/// <summary>
/// The C# twin of the server's PERSONAL / BUSINESS rule (Tools/n8n/audience.py, <c>RULE_JS</c>). Pure: no clock, no network,
/// no stored state. The app uses it only to SHOW a chat the server has not decided yet; the decision itself is the server's.
/// Both sides run every row of ONE case table, Tools/n8n/fixtures/audience-cases.json (ChatAudienceRuleTests reads it
/// through the project root, never a copy): a row the server passes and this fails is a bug here.
///
/// The steps — the first that applies wins:
///   1. not a person → personal / non-human. WhatsApp: the id ends with one of <see cref="WhatsappNonHumanSuffixes"/>
///      (a group, a channel, the status and broadcast lists), or the bot / self flag; Telegram: the id is one of
///      <see cref="TelegramServiceIds"/>, or the chat type is one of <see cref="TelegramNonHumanTypes"/>, or the bot /
///      support / self flag;
///   2. Telegram without the chat card (all three flags unknown) → personal / unknown: a bot cannot be ruled out;
///   2a. Telegram with a chat type other than <see cref="TelegramPersonType"/> → personal / unknown. ANY other value
///       counts, an empty one included — it cannot be classified, and what cannot be classified is never decided;
///   3. the owner's own decision, if there is one → that kind / owner;
///   4. a saved contact → personal / contact;   5. a conversation from before the connection → personal / history;
///   6. contact or history not known → personal / unknown;   7. otherwise → business / new.
/// Every literal is matched EXACTLY — ordinal, case-sensitive, untrimmed — like the server's string comparisons.
/// </summary>
public static class ChatAudienceRule
{
    /// <summary>After a channel is connected, a chat without a decision gets no reply, no suggestion and no decision for this long.</summary>
    public const int SettleMinutes = 30;

    /// <summary>A message that waited for the owner's decision is handed to the assistant again only while it is younger than this.</summary>
    public const int FreshHours = 24;

    /// <summary>The data of a chat the owner made personal is wiped after this many days.</summary>
    public const int WipeDays = 2;

    // The module's literals (Tools/n8n/audience.py), compared with its text by ChatAudienceRuleTests.
    private const string WhatsappGroupSuffix = "@g.us";
    private const string TelegramChannelType = "channel";
    private const string TelegramGroupType = "chat";
    private const string TelegramBotType = "bot";

    /// <summary>A WhatsApp id ends with one of these when it is not a person: a group; a channel; the status and broadcast lists.</summary>
    public static readonly IReadOnlyList<string> WhatsappNonHumanSuffixes =
        new ReadOnlyCollection<string>(new[] { WhatsappGroupSuffix, "@newsletter", "@broadcast" });

    /// <summary>The Telegram service account (login codes and the like).</summary>
    public static readonly IReadOnlyList<string> TelegramServiceIds =
        new ReadOnlyCollection<string>(new[] { "777000" });

    /// <summary>
    /// Telegram chat types that are not a person: a channel; a group; a bot (a bot's reply arrives with the chat type
    /// <c>bot</c>, while the chat list shows the same bot as a <c>user</c> with the Bot flag).
    /// </summary>
    public static readonly IReadOnlyList<string> TelegramNonHumanTypes =
        new ReadOnlyCollection<string>(new[] { TelegramChannelType, TelegramGroupType, TelegramBotType });

    /// <summary>The Telegram chat type of a private dialog — the only person.</summary>
    public const string TelegramPersonType = "user";

    /// <summary>
    /// What kind of non-person the chat is, <see cref="NonHumanKind.None"/> for a person (step 1 of the rule). The kind
    /// follows the chat's shape first (its id, its type) and its flags after.
    /// </summary>
    public static NonHumanKind NonHuman(in AudienceFacts f)
    {
        string id = f.ChatId ?? string.Empty;
        return f.Telegram
            ? TelegramNonHuman(id, f.ChatType ?? string.Empty, f)
            : WhatsappNonHuman(id, f);
    }

    /// <summary>
    /// The verdict for a chat: (kind, reason). <paramref name="history"/> — whether a conversation existed before the
    /// connection (see <see cref="HistoryBeforeConnection"/>), null when not known. <paramref name="owner"/> — the owner's
    /// own decision for this chat, null when there is none. The facts carry whether the chat is a saved contact.
    /// </summary>
    public static (AudienceKind Kind, AudienceReason Reason) Decide(in AudienceFacts f, bool? history, AudienceKind? owner)
    {
        // 1. Not a person: never a client, whatever the owner says.
        if (NonHuman(f) != NonHumanKind.None) return PersonalBecause(AudienceReason.NonHuman);

        if (f.Telegram)
        {
            // 2. No chat card: a bot cannot be ruled out.
            if (!f.Bot.HasValue && !f.Support.HasValue && !f.Self.HasValue) return PersonalBecause(AudienceReason.Unknown);
            // 2a. A chat type other than the person's ('' included, any other spelling or case) cannot be classified.
            if (!string.Equals(f.ChatType ?? string.Empty, TelegramPersonType, StringComparison.Ordinal))
                return PersonalBecause(AudienceReason.Unknown);
        }

        // 3. The owner's own decision.
        if (owner.HasValue) return (owner.Value, AudienceReason.Owner);
        // 4-5. A saved contact, or a conversation that existed before the connection.
        if (f.Contact == true) return PersonalBecause(AudienceReason.Contact);
        if (history == true) return PersonalBecause(AudienceReason.History);
        // 6. Contact or history not known.
        if (f.Contact != false || history != false) return PersonalBecause(AudienceReason.Unknown);
        // 7. A new person.
        return (AudienceKind.Business, AudienceReason.New);
    }

    /// <summary>
    /// Whether a conversation existed before the channel was connected, judged by the chat's LAST message: older than the
    /// connection moment → true, otherwise false. Only a display estimate for a chat the server has not decided — the
    /// server looks at the oldest message. Null (neither yes nor no) when the moment is not known, and also when there is
    /// no usable time on either side: a missing time is no evidence that the conversation was old.
    /// </summary>
    /// <param name="lastMessageUnixSec">Unix seconds of the chat's last message (0 when the list gave none).</param>
    /// <param name="connectedAtMs">Unix milliseconds of the connection moment, null when not known.</param>
    public static bool? HistoryBeforeConnection(long lastMessageUnixSec, long? connectedAtMs)
    {
        if (!connectedAtMs.HasValue || connectedAtMs.Value <= 0 || lastMessageUnixSec <= 0) return null;
        return lastMessageUnixSec * 1000L < connectedAtMs.Value;
    }

    private static (AudienceKind Kind, AudienceReason Reason) PersonalBecause(AudienceReason reason) =>
        (AudienceKind.Personal, reason);

    private static NonHumanKind WhatsappNonHuman(string id, in AudienceFacts f)
    {
        for (int i = 0; i < WhatsappNonHumanSuffixes.Count; i++)
        {
            string suffix = WhatsappNonHumanSuffixes[i];
            if (id.EndsWith(suffix, StringComparison.Ordinal))
                return suffix == WhatsappGroupSuffix ? NonHumanKind.Group : NonHumanKind.Channel;
        }
        if (f.Bot == true) return NonHumanKind.Bot;
        if (f.Self == true) return NonHumanKind.Self;
        return NonHumanKind.None;
    }

    private static NonHumanKind TelegramNonHuman(string id, string type, in AudienceFacts f)
    {
        if (Contains(TelegramServiceIds, id)) return NonHumanKind.Service;
        if (type == TelegramGroupType) return NonHumanKind.Group;
        if (type == TelegramBotType) return NonHumanKind.Bot;
        if (Contains(TelegramNonHumanTypes, type)) return NonHumanKind.Channel;
        if (f.Bot == true) return NonHumanKind.Bot;
        if (f.Support == true) return NonHumanKind.Service;
        if (f.Self == true) return NonHumanKind.Self;
        return NonHumanKind.None;
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i], value, StringComparison.Ordinal)) return true;
        return false;
    }
}

/// <summary>
/// Builds a chat's <see cref="AudienceFacts"/> from its chat-list row — the one place the row's contact object and user
/// card are read for the rule. Pure; <c>ChatManager.ParseChatsJson</c> calls it for every row, new or merged.
///
/// JsonUtility never leaves <c>contact</c> / <c>user</c> null: a row without them reads as an empty name / all flags false,
/// i.e. as KNOWN «not a contact, not a bot». Only a row built by hand can lack the object (null) — that reads as «not
/// known», which the rule treats as personal.
/// </summary>
public static class ChatAudienceFacts
{
    /// <param name="dialog">The chat-list row.</param>
    /// <param name="channel">The channel the list belongs to.</param>
    /// <param name="chatKey">The chat id as the list keys it (WhatsApp's jid verbatim, Telegram's bare id).</param>
    /// <param name="ownWhatsappNumber">The active bot's stored WhatsApp number, as typed; only WhatsApp reads it.</param>
    public static AudienceFacts From(ChatDialog dialog, ChatChannel channel, string chatKey, string ownWhatsappNumber)
    {
        string type = dialog?.type ?? string.Empty;
        return channel == ChatChannel.Telegram
            ? TelegramFacts(dialog, chatKey, type)
            : WhatsappFacts(dialog, chatKey, type, ownWhatsappNumber);
    }

    /// <summary>
    /// True when the chat is the chat with oneself («Избранное»): the digits of the id's number part equal the digits of the
    /// bot's own number. An empty number is never anyone's — an id with no digits has empty digits too.
    /// </summary>
    public static bool IsOwnWhatsappChat(string chatId, string ownNumber)
    {
        string own = WappiProxyRequest.DigitsOnly(ownNumber);
        if (own.Length == 0 || string.IsNullOrEmpty(chatId)) return false;

        int at = chatId.IndexOf('@');
        string numberPart = at < 0 ? chatId : chatId.Substring(0, at);
        return WappiProxyRequest.DigitsOnly(numberPart) == own;
    }

    // WhatsApp: a contact is a name from the phone's address book (contact.FullName). Wappi's Found flag means nothing and
    // a push name is what the person calls themselves. Bot / support are not WhatsApp facts: false.
    private static AudienceFacts WhatsappFacts(ChatDialog dialog, string chatKey, string type, string ownNumber)
    {
        ChatDialogContact contact = dialog?.contact;
        bool? saved = contact == null ? (bool?)null : !string.IsNullOrWhiteSpace(contact.FullName);
        return new AudienceFacts(telegram: false, chatKey, type, bot: false, support: false,
            self: IsOwnWhatsappChat(chatKey, ownNumber), contact: saved);
    }

    // Telegram: the user card's flags and the dialog type, as they are.
    private static AudienceFacts TelegramFacts(ChatDialog dialog, string chatKey, string type)
    {
        ChatDialogUser user = dialog?.user;
        return new AudienceFacts(telegram: true, chatKey, type, user?.Bot, user?.Support, user?.Self, user?.Contact);
    }
}
