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

// 2026-09-04 stale-card fix: the pure re-derive rule behind EmptyStateView's OnEnable catch-up,
// its frame-1 reassert and its channel switch — three sites that each carried their own guard,
// and whose divergence was the bug. The card's visibility is a CanvasGroup alpha that survives
// the GameObject being deactivated, so a re-derive must be able to take DOWN a card it has no
// memory of raising, and must decide that from the resolver alone — never from the chat count.
public class EmptyStateCardPolicyTests
{
    // The bug: resolver says "no empty card" (Syncing / Ready) while a stale card is on screen.
    // This used to be reachable only when the chat list was non-empty, so a just-created bot kept
    // «Создайте первого бота» over the list for the whole 300s sync window — and forever on an
    // account with no chats.
    [Test] public void HidesVisibleCard_WhenResolverSaysNoEmptyState()
        => Assert.AreEqual(EmptyStateCardAction.Hide,
            EmptyStateCardPolicy.Decide(null, cardVisible: true));

    [Test] public void DoesNothing_WhenNoCardShowingAndNoEmptyState()
        => Assert.AreEqual(EmptyStateCardAction.None,
            EmptyStateCardPolicy.Decide(null, cardVisible: false));

    [Test] public void ShowsCard_WhenResolverGivesAReasonAndNothingIsShowing()
        => Assert.AreEqual(EmptyStateCardAction.Show,
            EmptyStateCardPolicy.Decide(EmptyStateReason.NoBotsExist, cardVisible: false));

    // Show, NOT None, when the same reason is already up: the re-derive sites are also the
    // channel-switch sites, where the reason survives but the card must re-theme (Telegram accent)
    // and re-wire its CTA. Deduping duplicate work belongs to the event path's _lastReason guard.
    [Test] public void ReshowsCard_WhenTheSameReasonIsAlreadyOnScreen()
        => Assert.AreEqual(EmptyStateCardAction.Show,
            EmptyStateCardPolicy.Decide(EmptyStateReason.BotHasNoTelegram, cardVisible: true));

    [Test] public void ShowsConnectCard_ForEitherChannel()
    {
        Assert.AreEqual(EmptyStateCardAction.Show,
            EmptyStateCardPolicy.Decide(EmptyStateReason.BotHasNoWhatsApp, cardVisible: false));
        Assert.AreEqual(EmptyStateCardAction.Show,
            EmptyStateCardPolicy.Decide(EmptyStateReason.BotHasNoTelegram, cardVisible: false));
    }
}
