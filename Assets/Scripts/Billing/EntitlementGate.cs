using System;
using System.Collections.Generic;
using UnityEngine;

public enum PaywallTrigger { BotLimit, ChannelLimit, TrialExpired, Browse }

public static class EntitlementGate
{
    // Task 10 (BillingService) rewires this once real purchases exist; until then
    // nothing is purchased, so CurrentTier falls back to the trial/none split below.
    internal static Func<PlanTier> PurchasedTierSource = () => PlanTier.None;

    internal static void ResetSeamsForTests()
    {
        PurchasedTierSource = () => PlanTier.None;
    }

    // With nothing purchased and a trial that never started this yields Trial
    // (pre-auth grace, 3 bots / 3 channels) rather than None — deliberate, so
    // current dev flows (and a brand-new install's first-bot wizard) keep working
    // until the paywall UI (Task 12/14) lands.
    public static PlanTier CurrentTier =>
        EntitlementPolicy.EffectiveTier(PurchasedTierSource(), TrialLedger.HasStarted, TrialLedger.IsExpired);

    public static event Action<PaywallTrigger> OnPaywallRequested;

    public static void RequestPaywall(PaywallTrigger trigger) => OnPaywallRequested?.Invoke(trigger);

    public static bool CanCreateBot(int existingBots) =>
        EntitlementPolicy.CanCreateBot(CurrentTier, existingBots);

    public static bool CanConnectChannel(int connectedChannels) =>
        EntitlementPolicy.CanConnectChannel(CurrentTier, connectedChannels);

    // Pure seam: sum of true flags across (whatsapp, telegram) occupancy pairs.
    // Test-pinned so the counting rule can never silently drift from CanConnectChannel.
    public static int CountChannels(IEnumerable<(bool wa, bool tg)> bots)
    {
        int count = 0;
        foreach (var (wa, tg) in bots)
        {
            if (wa) count++;
            if (tg) count++;
        }
        return count;
    }

    // A channel SLOT = a non-empty, non-sentinel profileId — a created-but-unauthorized
    // profile still rents a Wappi profile, so it occupies a slot the same as an authorized
    // one. Safe with no scene loaded (EditMode tests, or a cold Manager.Instance): degrades
    // to 0 rather than throwing.
    public static int ConnectedChannelCount()
    {
        Transform root = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (root == null) return 0;

        var bots = new List<(bool wa, bool tg)>(root.childCount);
        for (int i = 0; i < root.childCount; i++)
        {
            Bot bot = root.GetChild(i).GetComponent<Bot>();
            if (bot == null) continue;
            bots.Add((IsOccupied(bot.whatsappProfileId), IsOccupied(bot.telegramProfileId)));
        }
        return CountChannels(bots);
    }

    private static bool IsOccupied(string profileId) =>
        !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;
}
