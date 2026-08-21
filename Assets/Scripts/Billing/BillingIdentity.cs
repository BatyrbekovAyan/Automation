using System;

/// <summary>
/// Seam for the per-device identity the client sends to n8n as the "AppUserID" form field on
/// bot creation, joining a Wappi profile (bot_profiles.app_user_id) to a subscribers row before
/// any purchase has ever happened — this is what lets a fresh install auto-register a trial row
/// server-side and be capped at the same channel-slot limit before a subscription exists.
///
/// Since Task 10, <see cref="Source"/> is <see cref="BillingService.AppUserId"/>: RevenueCat's own
/// anonymous appUserID once BillingService.Initialize() has configured a real backend, falling back
/// to SystemInfo.deviceUniqueIdentifier whenever no backend is active yet (Editor, keyless
/// secrets.json before Task 0, or Initialize() not yet called) — see BillingService for that fallback.
/// </summary>
public static class BillingIdentity
{
    internal static Func<string> Source = () => BillingService.AppUserId;

    internal static void ResetSeamsForTests()
    {
        Source = () => BillingService.AppUserId;
    }

    public static string AppUserId => Source();
}
