using NUnit.Framework;

// Covers ChatRowSwipePolicy.Enabled — the pure channel gate behind the chat-row
// swipe-to-delete affordance. Swipe-delete is WhatsApp-only (Telegram has no chat/delete
// endpoint; the D4 owner decision removes the visual slide on TG rows), so the predicate is
// a straight channel equality: WhatsApp ⇒ enabled, Telegram ⇒ disabled.
public class ChatRowSwipePolicyTests
{
    [Test]
    public void Enabled_WhatsApp_True()
    {
        Assert.IsTrue(ChatRowSwipePolicy.Enabled(ChatChannel.WhatsApp),
            "Swipe-to-delete is enabled on WhatsApp (chat/delete endpoint exists).");
    }

    [Test]
    public void Enabled_Telegram_False()
    {
        Assert.IsFalse(ChatRowSwipePolicy.Enabled(ChatChannel.Telegram),
            "Swipe-to-delete affordance is removed on Telegram (no chat/delete endpoint; D4).");
    }
}
