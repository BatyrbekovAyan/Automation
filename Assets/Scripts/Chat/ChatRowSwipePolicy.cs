/// <summary>
/// «Личные и рабочие чаты» (spec §5.2, §5.3): which actions a chat row's swipe offers.
///   • The working list, a working chat: «В личные», and today's «Удалить» on WhatsApp only. Telegram (tapi) has no chat/delete
///     endpoint, so a Telegram row never carries a delete (ChatManager.ActiveChannelSupportsChatDelete guards the call too).
///   • The «Личные» folder: «В рабочие» and nothing else. A personal chat found by the working list's search stands in the
///     folder's place too (<see cref="InFolder"/>): the move takes a chat to the other kind wherever it is shown, like
///     «Архив» in WhatsApp.
///   • A chat that is not a person (a group, a channel, a bot, the service account, «Избранное») is never moved: no move,
///     and in the folder's place no swipe at all.
/// A plain static rule (no MonoBehaviour, no UnityEngine), so EditMode pins it. ChatItemView applies it on every bind and
/// whenever the row's place or facts change; ChatListView gives every row its place from the chat's verdict.
/// </summary>
public static class ChatRowSwipePolicy
{
    /// <summary>The row's actions: the move («В личные» in the list's place, «В рабочие» in the folder's) and «Удалить».</summary>
    public static (bool Move, bool Delete) Actions(ChatChannel channel, bool inFolder, NonHumanKind nonHuman) =>
        (nonHuman == NonHumanKind.None, !inFolder && channel == ChatChannel.WhatsApp);

    /// <summary>
    /// A working-list row's place: a personal chat (shown there only among the search results) swipes as it would in the
    /// «Личные» folder; a working chat, or one without a verdict, swipes as the list's.
    /// </summary>
    public static bool InFolder(AudienceView view) => view != null && view.Kind == AudienceKind.Personal;

    /// <summary>The move button's word — where the chat goes: «В рабочие» from the folder's place, «В личные» from the list's.</summary>
    public static string MoveLabel(bool inFolder) => inFolder ? PersonalChatsCopy.SwipeToBusiness : PersonalChatsCopy.SwipeToPersonal;
}
