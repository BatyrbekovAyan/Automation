using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.3, §5.4; sketch 009 screens 1, 3, 4): what the «Личные» folder shows.
///   • <see cref="Browse"/> — the folder itself: every personal chat, the ones that are not people included.
///   • <see cref="Edit"/> — «Изменить»: the people among them, with checkboxes, to make several chats working at once.
///   • <see cref="Onboarding"/> — «Кто из них клиенты?», the same selection right after a channel was connected, each row
///     saying why the chat is personal.
/// </summary>
public enum FolderMode { Browse, Edit, Onboarding }

/// <summary>One row of the folder: the chat, how it stands, the line under its name and whether it carries a checkbox.</summary>
public sealed class FolderRow
{
    public ChatViewModel Chat;
    public AudienceView View;
    /// <summary>The line under the name; null — the chat's own last message, as a chat row shows it.</summary>
    public string Subline;
    /// <summary>The row carries a checkbox: the two selection modes.</summary>
    public bool Selectable;
}

/// <summary>
/// The folder's rules, pure (no Unity, no clock): which chats it lists in each mode and in what order, the line under each
/// name, the search, the bottom button's word, the onboarding's soft note, what a confirmed selection moves and the toast
/// that says so, and when a bot or channel switch takes the folder away. <see cref="PersonalChatsFolderView"/> draws it.
/// A chat whose view is missing reads as personal and never moves — what is not known is personal.
/// </summary>
public static class PersonalChatsFolderModel
{
    private static readonly CompareInfo Ci = CultureInfo.InvariantCulture.CompareInfo;

    /// <summary>
    /// The folder's rows, newest first (<see cref="ChatListOrder"/>): Browse — every personal chat, the ones that are not people
    /// included; Edit and Onboarding — only the chats that can be made working (<see cref="AudienceView.CanMove"/>).
    /// <paramref name="query"/> keeps the chats whose name matches, or whose number does when the query is a number.
    /// </summary>
    public static IReadOnlyList<FolderRow> Rows(IReadOnlyList<(ChatViewModel chat, AudienceView view)> chats, FolderMode mode, string query)
    {
        var picked = new List<(ChatViewModel chat, AudienceView view)>();
        if (chats != null)
            foreach (var (chat, view) in chats)
                if (chat != null && Belongs(view, mode) && Matches(chat, query)) picked.Add((chat, view));

        List<(ChatViewModel chat, AudienceView view)> ordered = ChatListOrder.Apply(picked, entry => entry.chat.LastMessageTime);
        var rows = new List<FolderRow>(ordered.Count);
        foreach (var (chat, view) in ordered)
            rows.Add(new FolderRow { Chat = chat, View = view, Subline = Subline(view, mode), Selectable = mode != FolderMode.Browse });
        return rows;
    }

    /// <summary>
    /// The line under a chat's name: a chat that is not a person says what it is and that the assistant never answers it
    /// (<see cref="PersonalChatsCopy.NonHumanLine"/>); in «Кто из них клиенты?» a person says why it is personal — «в
    /// контактах» / «не в контактах, писал раньше»; otherwise null — the chat's own last message.
    /// </summary>
    public static string Subline(AudienceView view, FolderMode mode)
    {
        if (view == null) return null;
        if (view.NonHuman != NonHumanKind.None) return PersonalChatsCopy.NonHumanLine(view.NonHuman);
        if (mode != FolderMode.Onboarding) return null;
        return view.Reason switch
        {
            AudienceReason.Contact => PersonalChatsCopy.SublineContact,
            AudienceReason.History => PersonalChatsCopy.SublineHistory,
            _ => null,
        };
    }

    /// <summary>The bottom button's word: «Сделать рабочими · N» (Edit), «Готово · клиентов: N» (Onboarding); Browse has none.</summary>
    public static string PrimaryLabel(FolderMode mode, int selected) => mode switch
    {
        FolderMode.Edit => PersonalChatsCopy.MakeBusinessMany(Math.Max(0, selected)),
        FolderMode.Onboarding => PersonalChatsCopy.OnboardingDone(Math.Max(0, selected)),
        _ => null,
    };

    /// <summary>
    /// The onboarding's soft note — the messenger on the phone, it seems, does not see the address book: the channel's chats
    /// have arrived (<paramref name="loaded"/> — its chat list holds a chat) and not one of the rows is a saved contact.
    /// </summary>
    public static bool ShowNoContactsNote(IReadOnlyList<FolderRow> rows, bool loaded)
    {
        if (!loaded) return false;
        if (rows != null)
            foreach (FolderRow row in rows)
                if (IsContact(row)) return false;
        return true;
    }

    /// <summary>The search: the chat's name, or — for a query written as a number — the chat's phone number.</summary>
    public static bool Matches(ChatViewModel chat, string query)
    {
        string text = query?.Trim();
        if (string.IsNullOrEmpty(text)) return true;
        if (chat == null) return false;
        if (!string.IsNullOrEmpty(chat.Title) && Ci.IndexOf(chat.Title, text, CompareOptions.IgnoreCase) >= 0) return true;
        string digits = ChatAudienceConnectPolicy.NumberDigits(text);
        if (digits.Length == 0) return false;
        string number = WappiProxyRequest.DigitsOnly(ChatIdFormat.PhoneNumber(chat.ChatId) ?? "");
        return number.Length > 0 && number.Contains(digits);
    }

    /// <summary>Some row can be made working — the folder offers «Изменить» and its swipe hint.</summary>
    public static bool AnyMovable(IReadOnlyList<FolderRow> rows)
    {
        if (rows == null) return false;
        foreach (FolderRow row in rows)
            if (row?.View != null && row.View.CanMove) return true;
        return false;
    }

    /// <summary>
    /// What a confirmed selection makes working: the selected rows that can move and whose id the server takes
    /// (<see cref="ChatAudienceRequest.IsSendableChatId"/>), in the rows' order — never anything the folder does not list.
    /// </summary>
    public static List<string> ChatsToMove(IReadOnlyList<FolderRow> rows, ICollection<string> selected)
    {
        var chats = new List<string>();
        if (rows == null || selected == null || selected.Count == 0) return chats;
        foreach (FolderRow row in rows)
        {
            string chatId = row?.Chat?.ChatId;
            if (row?.View == null || !row.View.CanMove || !ChatAudienceRequest.IsSendableChatId(chatId)) continue;
            if (selected.Contains(chatId) && !chats.Contains(chatId)) chats.Add(chatId);
        }
        return chats;
    }

    /// <summary>The toast after chats were made working: «Чат стал рабочим» for one, «N чата стали рабочими» for several.</summary>
    public static string MovedToast(int moved) =>
        moved <= 0 ? null : moved == 1 ? PersonalChatsCopy.MovedToBusiness : PersonalChatsCopy.MovedToBusinessMany(moved);

    /// <summary>
    /// The folder stays open across a bot or channel switch only when the chats screen still shows the bot and the channel it
    /// was opened for: the folder belongs to one bot and one channel.
    /// </summary>
    public static bool StaysOpen(string openedBot, ChatChannel openedChannel, string bot, ChatChannel channel) =>
        string.Equals(openedBot, bot, StringComparison.Ordinal) && openedChannel == channel;

    // Browse holds every personal chat; the selection modes only the people — the chats that can be made working.
    private static bool Belongs(AudienceView view, FolderMode mode) =>
        PersonalChatsListModel.IsPersonal(view) && (mode == FolderMode.Browse || (view != null && view.CanMove));

    // A saved contact: the decision says so, or the chat's own facts do.
    private static bool IsContact(FolderRow row) =>
        row != null && ((row.View != null && row.View.Reason == AudienceReason.Contact) || (row.Chat != null && row.Chat.Facts.Contact == true));
}
