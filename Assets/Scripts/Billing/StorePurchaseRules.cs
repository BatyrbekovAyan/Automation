/// <summary>
/// Pure store-purchase rules shared by <see cref="RevenueCatBackend"/> and the paywall / «Подписка»
/// surfaces (2026-09-06, Google Play track). UnityEngine-free so every branch is pinned by
/// AndroidBillingPathTests.
/// </summary>
public static class StoreProductKey
{
    /// <summary>
    /// The bare product id behind a store identifier. Google Play StoreProduct ids carry the base
    /// plan («sub.start.month:monthly», options «…:monthly:offer»), while <see cref="PlanCatalog"/>
    /// and the paywall key everything by the bare id — without this the Android price map never
    /// matched a single subscription and the paywall silently kept its KZT literals. App Store ids
    /// never contain ':', so this is the identity there.
    /// </summary>
    public static string Normalize(string storeIdentifier)
    {
        if (string.IsNullOrEmpty(storeIdentifier)) return storeIdentifier;
        int colon = storeIdentifier.IndexOf(':');
        return colon < 0 ? storeIdentifier : storeIdentifier.Substring(0, colon);
    }
}

/// <summary>
/// The failure reasons a purchase callback may carry. Both are LOGIC SENTINELS the paywall and
/// the «Подписка» page compare against (a cancelled sheet shows no notice; a real failure does),
/// so they live here as shared constants rather than as literals at each site.
/// </summary>
public static class BillingFailure
{
    public const string UserCancelled = "user_cancelled";
    public const string NotInitialized = "not_initialized";
}

/// <summary>What a store purchase call must be made with.</summary>
public readonly struct PurchaseParams
{
    /// <summary>Play Billing product type: «subs» for the six plans, «inapp» for the top-up.</summary>
    public readonly string Type;
    /// <summary>The currently owned subscription this purchase REPLACES; null when it is a plain buy.</summary>
    public readonly string OldSku;

    public bool Replace => !string.IsNullOrEmpty(OldSku);

    public PurchaseParams(string type, string oldSku)
    {
        Type = type;
        OldSku = oldSku;
    }
}

public static class PurchaseParamsPolicy
{
    public const string SubscriptionType = "subs";
    public const string OneTimeType = "inapp";

    /// <summary>
    /// Resolves the parameters for buying <paramref name="sku"/> while
    /// <paramref name="activeSubscriptionProductId"/> (bare id, null when none) is owned.
    ///
    /// <para><b>Google Play needs the replacement spelled out.</b> Every tier × period is a
    /// DISTINCT Play product, and a purchase without an old-sku is a brand-new subscription
    /// alongside the old one — the owner would pay Start AND Business every month until they
    /// found the first in Play's subscription settings. On the App Store the shared subscription
    /// group swaps automatically, and passing an old sku there is meaningless, so the rule is
    /// Play-only. The top-up is a one-time product and never replaces anything; buying the
    /// product already owned is not a replacement either (Play answers ITEM_ALREADY_OWNED).</para>
    /// </summary>
    public static PurchaseParams Resolve(string activeSubscriptionProductId, string sku, bool googlePlay)
    {
        if (sku == PlanCatalog.SkuTopUp) return new PurchaseParams(OneTimeType, null);

        bool replace = googlePlay
                       && !string.IsNullOrEmpty(activeSubscriptionProductId)
                       && activeSubscriptionProductId != sku;
        return new PurchaseParams(SubscriptionType, replace ? activeSubscriptionProductId : null);
    }

    /// <summary>
    /// The reason to report for a failed purchase. A user cancel outranks the error object —
    /// the Android wrapper sends BOTH (an error plus userCancelled=true) for a dismissed Play
    /// sheet, and checking the error first turned every deliberate cancel into
    /// «Не удалось оформить подписку».
    /// </summary>
    public static string FailureReason(bool userCancelled, string errorMessage)
        => userCancelled ? BillingFailure.UserCancelled
                         : string.IsNullOrEmpty(errorMessage) ? "unknown_error" : errorMessage;
}
