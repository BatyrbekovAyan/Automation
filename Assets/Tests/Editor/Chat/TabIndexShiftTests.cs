using NUnit.Framework;

// Locks the post-06-02 nav-restructure tab-index contract. Removing the Telegram tab
// (index 1) shifts Сводка/Bots/Profile from 2/3/4 to 1/2/3 while Chats stays at 0.
// These guards fail the moment anyone reverts BottomTabManager.BotsTabIndex (the const
// is inlined at compile time, so a changed value flips the expected/actual comparison),
// keeping the runtime index seam pinned to the 06-02 scene tab array. The TabRefreshGate
// asserts confirm the paired rule: the Chats tab quietly re-syncs chats; the Bots tab never does.
public class TabIndexShiftTests
{
    [Test]
    public void BotsTabIndex_IsPostRestructureValue_Two()
        => Assert.AreEqual(2, BottomTabManager.BotsTabIndex);

    [Test]
    public void WhatsAppTabIndex_StaysZero()
        => Assert.AreEqual(0, BottomTabManager.WhatsAppTabIndex);

    // Navigating to the Chats tab (post-restructure index 0) quietly refreshes the chat list.
    [Test]
    public void ChatsTab_TriggersChatRefresh()
        => Assert.IsTrue(TabRefreshGate.ShouldRefreshChats(0, isInitialSelection: false, whatsAppTabIndex: 0));

    // Navigating to the Bots tab (post-restructure index 2) never triggers a chat re-sync.
    [Test]
    public void BotsTab_NeverTriggersChatRefresh()
        => Assert.IsFalse(TabRefreshGate.ShouldRefreshChats(2, isInitialSelection: false, whatsAppTabIndex: 0));
}
