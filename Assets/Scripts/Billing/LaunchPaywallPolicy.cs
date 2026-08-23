/// <summary>
/// The launch-time «чек ценности» decision (spec §3 / Task 15a): should THIS launch open the
/// paywall on <see cref="PaywallTrigger.TrialExpired"/>?
///
/// A pure seam for the same reason as <see cref="BillingGateRows"/>: the caller lives inside
/// Manager's boot coroutine, which EditMode cannot instantiate, so the rule itself is pinned by
/// a truth table instead of being trusted by inspection.
/// </summary>
public static class LaunchPaywallPolicy
{
    /// <summary>
    /// True only when all four hold: this launch has not shown it yet, the entitlement set is
    /// RESOLVED, the effective tier is <see cref="PlanTier.None"/> (nothing purchased and no
    /// trial left) and the local trial clock has actually run out.
    ///
    /// <para><paramref name="entitlementsKnown"/> is the load-bearing one. On a keyed device
    /// <see cref="BillingService.EntitlementsKnown"/> is false until the first CustomerInfo
    /// round-trip lands, and <see cref="EntitlementGate.CurrentTier"/> answers Trial GRACE for
    /// that whole window — so evaluating at boot without this guard would silently decide
    /// «not expired» for a customer whose real entitlement simply had not arrived yet, and the
    /// check would never run again. The caller instead waits for the first
    /// <see cref="BillingService.OnEntitlementChanged"/> callback and re-asks.</para>
    ///
    /// <para><paramref name="trialExpired"/> is deliberately NOT inferable from the tier: the
    /// ledger keeps ticking (and expiring) behind a purchase, and None can also be reached with
    /// a trial that never started at all — both must be false here.</para>
    /// </summary>
    public static bool ShouldShowExpiry(PlanTier tier, bool entitlementsKnown, bool trialExpired,
        bool alreadyShownThisLaunch)
    {
        if (alreadyShownThisLaunch) return false;
        if (!entitlementsKnown) return false;
        if (tier != PlanTier.None) return false;
        return trialExpired;
    }
}
