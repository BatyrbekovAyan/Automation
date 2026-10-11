using System;

/// <summary>
/// F2 (plan 2026-10-11 §5.1–5.2, task T9): two pure decisions around a purchase, no Unity, EditMode-tested.
///
/// <b>The wait before a pairing.</b> The store's purchase callback lands before RevenueCat's webhook has written the
/// subscriber row (prod evidence 2026-09-06, <see cref="TopUpSettlePolicy"/>), and Create counts the channel slots from
/// that row: a pairing started in that window ends in Create's <c>channel_limit</c> — and the server then deletes the
/// profile the owner has just paired. So the settings' fresh path (<c>profile.add</c> → pairing → Create) asks the last
/// GetUsage snapshot first: when it DEFINITELY says «no free channel» (expired / grace, or every slot of the plan taken)
/// the path re-reads on the top-up schedule (0/2/5/10/20 s) and starts only once the server agrees; when the schedule is
/// spent it gives up without pairing. A snapshot that says nothing (none yet, an unrecognised plan) never blocks — the
/// <see cref="ServerAccountStatus"/> rule: the server may take a channel away, an unknown never does.
///
/// <b>The purchase itself.</b> <see cref="BillingService.OnEntitlementChanged"/> also fires for the boot resolution of a
/// plan the owner already had; only a move from no paid plan to a paid one is a purchase (the post-purchase sheet).
/// </summary>
public static class ReconnectSettlePolicy
{
    /// <summary>
    /// Channel slots of a server plan — Create's own <c>Compute Slot Limit</c> table (Create WA/TG): expired / grace give
    /// none, otherwise the plan's <see cref="PlanCatalog"/> channels; null for a plan name this client does not know.
    /// </summary>
    public static int? ChannelLimit(string plan, string status)
    {
        if (IsStatus(status, QuotaFallbackPolicy.StatusExpired) || IsStatus(status, QuotaFallbackPolicy.StatusGrace)) return 0;
        switch ((plan ?? "").Trim().ToLowerInvariant())
        {
            case "trial": return PlanCatalog.Get(PlanTier.Trial).MaxChannels;
            case "start": return PlanCatalog.Get(PlanTier.Start).MaxChannels;
            case "business": return PlanCatalog.Get(PlanTier.Business).MaxChannels;
            case "network": return PlanCatalog.Get(PlanTier.Network).MaxChannels;
            case "none": return 0;
            default: return null;
        }
    }

    /// <summary>
    /// Whether the server, by this snapshot, has a channel slot for one more profile: true / false, or null when the
    /// snapshot tells nothing (none yet, an unsuccessful body, an unknown plan).
    /// </summary>
    public static bool? ServerHasFreeChannel(UsageSnapshot usage)
    {
        if (usage == null || !usage.success) return null;
        int? limit = ChannelLimit(usage.plan, usage.status);
        if (limit == null) return null;
        return usage.channelsConnected < limit.Value;
    }

    /// <summary>Wait (and re-read) before <c>profile.add</c> only when the snapshot definitely says «no slot».</summary>
    public static bool ShouldWaitBeforePairing(UsageSnapshot usage) => ServerHasFreeChannel(usage) == false;

    /// <summary>Delay before re-read number <paramref name="readIndex"/> (0-based) — the top-up schedule — or null once spent.</summary>
    public static float? DelayBeforeRead(int readIndex) => TopUpSettlePolicy.DelayBeforeRead(readIndex);

    /// <summary>A plan the owner pays for — never the trial (a ledger fact, not an entitlement) and never none.</summary>
    public static bool IsPaid(PlanTier tier) =>
        tier == PlanTier.Start || tier == PlanTier.Business || tier == PlanTier.Network;

    /// <summary>
    /// The entitlement moved from no paid plan to a paid one. <paramref name="before"/> is null until the first resolution
    /// of the session — that one is the boot answer for a plan the owner may have had for months, never a purchase.
    /// </summary>
    public static bool IsPurchaseTransition(PlanTier? before, PlanTier after) =>
        before.HasValue && !IsPaid(before.Value) && IsPaid(after);

    private static bool IsStatus(string status, string expected) =>
        string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
