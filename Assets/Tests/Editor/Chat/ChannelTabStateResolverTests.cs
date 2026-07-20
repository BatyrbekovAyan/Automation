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

// D12-ext (08-REVIEW CR-01): the empty-state card's NoBots-coercion seam. BeginLoadForActiveBot
// fires BotHasNo{Channel} even when ZERO bots exist (FindBotByName("_default") == null → the
// connect reason), which re-wires the create-bot CTA to the silent OpenCurrentBotAuth. Effective
// promotes such a raw reason back to NoBotsExist ONLY when the authoritative resolver
// (ComputeCurrentEmptyState) also says NoBots — a genuine connect card for a real bot is preserved
// byte-identically (the WhatsApp invariant, pinned below).
public class EmptyStateReasonPolicyTests
{
    [Test] public void CoercesTelegramConnectReason_WhenResolverSaysNoBots()
        => Assert.AreEqual(EmptyStateReason.NoBotsExist,
            EmptyStateReasonPolicy.Effective(EmptyStateReason.BotHasNoTelegram, EmptyStateReason.NoBotsExist));

    [Test] public void CoercesWhatsAppConnectReason_WhenResolverSaysNoBots()
        => Assert.AreEqual(EmptyStateReason.NoBotsExist,
            EmptyStateReasonPolicy.Effective(EmptyStateReason.BotHasNoWhatsApp, EmptyStateReason.NoBotsExist));

    [Test] public void NoBots_StaysNoBots()
        => Assert.AreEqual(EmptyStateReason.NoBotsExist,
            EmptyStateReasonPolicy.Effective(EmptyStateReason.NoBotsExist, EmptyStateReason.NoBotsExist));

    // WhatsApp INVARIANT: a real WA-less bot keeps its connect reason (resolver agrees) — never hijacked.
    [Test] public void PreservesWhatsAppConnectReason_WhenResolverAgrees()
        => Assert.AreEqual(EmptyStateReason.BotHasNoWhatsApp,
            EmptyStateReasonPolicy.Effective(EmptyStateReason.BotHasNoWhatsApp, EmptyStateReason.BotHasNoWhatsApp));

    [Test] public void PreservesTelegramConnectReason_WhenResolverAgrees()
        => Assert.AreEqual(EmptyStateReason.BotHasNoTelegram,
            EmptyStateReasonPolicy.Effective(EmptyStateReason.BotHasNoTelegram, EmptyStateReason.BotHasNoTelegram));

    // Resolver undecided (null) ⇒ trust the raw event, never hijack the card.
    [Test] public void TrustsRawReason_WhenResolverUndecided()
        => Assert.AreEqual(EmptyStateReason.BotHasNoWhatsApp,
            EmptyStateReasonPolicy.Effective(EmptyStateReason.BotHasNoWhatsApp, null));
}
