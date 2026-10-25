/// <summary>
/// «Личные и рабочие чаты» (spec §5.2, §5.3): which actions a chat row's swipe offers.
///   • The move: «В личные» on a working chat in the working list; «В рабочие» in the «Личные» folder's place — the folder,
///     and a personal chat found by the working list's search (<see cref="InFolder"/>): the move takes a chat to the other
///     kind wherever it is shown, like «Архив» in WhatsApp. A chat that is not a person (a group, a channel, a bot, the
///     service account, «Избранное») is never moved.
///   • «Удалить» — today's delete — on every WhatsApp chat, in every place: the feature moves chats between the lists, it
///     never takes deletion away. Telegram (tapi) has no chat/delete endpoint, so a Telegram row never carries a delete
///     (ChatManager.ActiveChannelSupportsChatDelete guards the call too); a Telegram chat that is not a person therefore has
///     no swipe at all.
/// A plain static rule (no MonoBehaviour, no UnityEngine), so EditMode pins it. ChatItemView applies it on every bind and
/// whenever the row's place or facts change; ChatListView gives every row its place from the chat's verdict.
/// </summary>
public static class ChatRowSwipePolicy
{
    /// <summary>
    /// The row's actions: the move — for a person, whatever the place — and «Удалить» — on WhatsApp, whatever the place.
    /// <paramref name="inFolder"/>, the row's place, does not change which actions show: it names the move and its direction
    /// (<see cref="MoveLabel"/>; ChatListView.RequestMove).
    /// </summary>
    public static (bool Move, bool Delete) Actions(ChatChannel channel, bool inFolder, NonHumanKind nonHuman) =>
        (nonHuman == NonHumanKind.None, channel == ChatChannel.WhatsApp);

    /// <summary>
    /// A working-list row's place: a personal chat (shown there only among the search results) swipes as it would in the
    /// «Личные» folder; a working chat, or one without a verdict, swipes as the list's.
    /// </summary>
    public static bool InFolder(AudienceView view) => view != null && view.Kind == AudienceKind.Personal;

    /// <summary>The move button's word — where the chat goes: «В рабочие» from the folder's place, «В личные» from the list's.</summary>
    public static string MoveLabel(bool inFolder) => inFolder ? PersonalChatsCopy.SwipeToBusiness : PersonalChatsCopy.SwipeToPersonal;
}
