using System;
using UnityEngine;

/// <summary>
/// Seam for the per-device identity the client sends to n8n as the "AppUserID" form field on
/// bot creation, joining a Wappi profile (bot_profiles.app_user_id) to a subscribers row before
/// any purchase has ever happened — this is what lets a fresh install auto-register a trial row
/// server-side and be capped at the same channel-slot limit before a subscription exists.
///
/// Task 10 rewires <see cref="Source"/> to RevenueCat's appUserID (BillingService) once real
/// purchases exist; until then every profile a device creates is keyed by
/// SystemInfo.deviceUniqueIdentifier, which is stable for the lifetime of the install.
/// </summary>
public static class BillingIdentity
{
    internal static Func<string> Source = () => SystemInfo.deviceUniqueIdentifier;

    internal static void ResetSeamsForTests()
    {
        Source = () => SystemInfo.deviceUniqueIdentifier;
    }

    public static string AppUserId => Source();
}
