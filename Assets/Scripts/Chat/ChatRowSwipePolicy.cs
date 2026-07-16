/// <summary>
/// Pure channel gate for the chat-row swipe-to-delete affordance.
///
/// Swipe-to-delete is a WhatsApp-ONLY gesture: Telegram (tapi) exposes no chat/delete
/// endpoint, so the 05-03 network guard (<c>ActiveChannelSupportsChatDelete</c>) already
/// no-ops a Telegram delete — and per the D4 owner decision the VISUAL swipe slide must be
/// removed on Telegram rows too, not merely turned into a no-op.
///
/// Plain static predicate (no MonoBehaviour, no UnityEngine) so it stays EditMode-unit-
/// testable alongside ChannelResolver / ChannelSwitcherModel. ChatItemView.Bind reads it on
/// every (pooled) bind to enable/disable the SwipeToDelete component for the active channel.
/// </summary>
public static class ChatRowSwipePolicy
{
    /// <summary>
    /// True when the swipe-to-delete affordance should be enabled for <paramref name="channel"/>:
    /// WhatsApp ⇒ true (chat/delete endpoint exists); every other channel ⇒ false (no delete
    /// endpoint — D4). Mirrors <c>ChatManager.ActiveChannelSupportsChatDelete</c>.
    /// </summary>
    public static bool Enabled(ChatChannel channel) => channel == ChatChannel.WhatsApp;
}
