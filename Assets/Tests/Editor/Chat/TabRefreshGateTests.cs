using NUnit.Framework;

public class TabRefreshGateTests
{
    const int WhatsAppTab = 0;

    [Test] public void WhatsAppTab_NotInitial_True()
        => Assert.IsTrue(TabRefreshGate.ShouldRefreshChats(WhatsAppTab, false, WhatsAppTab));

    [Test] public void WhatsAppTab_InitialSelection_False()
        => Assert.IsFalse(TabRefreshGate.ShouldRefreshChats(WhatsAppTab, true, WhatsAppTab));

    [Test] public void OtherTab_NotInitial_False()
        => Assert.IsFalse(TabRefreshGate.ShouldRefreshChats(3, false, WhatsAppTab));

    [Test] public void OtherTab_InitialSelection_False()
        => Assert.IsFalse(TabRefreshGate.ShouldRefreshChats(1, true, WhatsAppTab));

    [Test] public void NonZeroWhatsAppIndex_Matched()
        => Assert.IsTrue(TabRefreshGate.ShouldRefreshChats(2, false, 2));

    [Test] public void NonZeroWhatsAppIndex_OtherTab_False()
        => Assert.IsFalse(TabRefreshGate.ShouldRefreshChats(0, false, 2));
}
