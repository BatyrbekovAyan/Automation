using NUnit.Framework;

// Mirrors the 4 WhatsAppTabStateResolverTests cases against the channel-neutral
// core the WhatsApp resolver now delegates to. Same precedence, any channel.
public class ChannelTabStateResolverTests
{
    [Test] public void NoBots_WinsOverEverything()
        => Assert.AreEqual(ChannelTabState.NoBots, ChannelTabStateResolver.Resolve(0, true, true));

    [Test] public void NoConnection_WhenBotLacksChannel()
        => Assert.AreEqual(ChannelTabState.NoConnection, ChannelTabStateResolver.Resolve(1, false, false));

    [Test] public void Syncing_WhenConnectedAndInWindow()
        => Assert.AreEqual(ChannelTabState.Syncing, ChannelTabStateResolver.Resolve(1, true, true));

    [Test] public void Ready_WhenConnectedAndWindowClosed()
        => Assert.AreEqual(ChannelTabState.Ready, ChannelTabStateResolver.Resolve(1, true, false));

    // The WhatsApp wrapper maps the channel-neutral core onto its enum (NoConnection => NoWhatsApp).
    [Test] public void WhatsAppWrapper_MapsChannelNeutralCore()
    {
        Assert.AreEqual(WhatsAppTabState.NoBots,     WhatsAppTabStateResolver.Resolve(0, true, true));
        Assert.AreEqual(WhatsAppTabState.NoWhatsApp, WhatsAppTabStateResolver.Resolve(1, false, false));
        Assert.AreEqual(WhatsAppTabState.Syncing,    WhatsAppTabStateResolver.Resolve(1, true, true));
        Assert.AreEqual(WhatsAppTabState.Ready,      WhatsAppTabStateResolver.Resolve(1, true, false));
    }
}
