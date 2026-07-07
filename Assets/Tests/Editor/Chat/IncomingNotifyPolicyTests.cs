using NUnit.Framework;

// Contract for the sound/vibration cue on chat-list syncs: fires only for a
// genuinely new, incoming, unread message in a chat the user is NOT currently
// reading. Each guard is exercised by flipping exactly one input off the
// passing baseline.
public class IncomingNotifyPolicyTests
{
    private static bool Notify(
        bool isInitialLoad = false,
        bool lastIdChanged = true,
        bool lastMessageIsMine = false,
        int unreadCount = 2,
        string chatId = "111@c.us",
        string openChatId = "222@c.us",
        bool chatPanelVisible = false)
        => IncomingNotifyPolicy.ShouldNotify(
            isInitialLoad, lastIdChanged, lastMessageIsMine, unreadCount, chatId, openChatId, chatPanelVisible);

    [Test]
    public void Baseline_NewIncomingUnread_Notifies() => Assert.IsTrue(Notify());

    [Test]
    public void InitialCacheLoad_NeverNotifies() => Assert.IsFalse(Notify(isInitialLoad: true));

    [Test]
    public void UnchangedLastMessage_DoesNotNotify() => Assert.IsFalse(Notify(lastIdChanged: false));

    [Test]
    public void OwnOutgoingEcho_DoesNotNotify() => Assert.IsFalse(Notify(lastMessageIsMine: true));

    [Test]
    public void NoUnread_DoesNotNotify() => Assert.IsFalse(Notify(unreadCount: 0));

    [Test]
    public void OpenChatOnScreen_Suppressed()
    {
        Assert.IsFalse(Notify(chatId: "111@c.us", openChatId: "111@c.us", chatPanelVisible: true));
    }

    [Test]
    public void SameChatButPanelHidden_StillNotifies()
    {
        // currentChatId is never cleared on back-navigation, so the stale id
        // must not suppress cues while the user is on the chat list.
        Assert.IsTrue(Notify(chatId: "111@c.us", openChatId: "111@c.us", chatPanelVisible: false));
    }

    [Test]
    public void OtherChatWhilePanelVisible_Notifies()
    {
        Assert.IsTrue(Notify(chatId: "111@c.us", openChatId: "222@c.us", chatPanelVisible: true));
    }
}
