/// <summary>
/// When a store call (purchase / restore) may be declared HUNG. Pure seam, pinned by
/// PurchaseWatchdogPolicyTests; the coroutine that applies it lives on the calling screen.
///
/// Why it exists (App Review rejection №2, 2026-09-15, iPad Air 11-inch (M3), iPadOS 26.6: «the
/// in-app purchases were not responsive»): a StoreKit 2 purchase on that device reached storekitd
/// and then never presented a sheet nor called back, so the paywall's busy latch held forever
/// and every further tap did nothing. StoreKit 1 sidesteps that hang (RevenueCatBackend), and
/// this is the belt to those braces: a store call that has shown NO system UI within
/// <see cref="SheetGraceSeconds"/> is released with a visible failure notice instead of a dead
/// screen.
///
/// «Showed system UI» is read from the app losing focus — every StoreKit sheet (payment, sandbox
/// sign-in, Ask to Buy) resigns the app's active state, which Unity reports as
/// OnApplicationFocus(false). Once that happened the call is never timed out: the user may sit
/// on the sheet, type a password or wait for Ask to Buy for as long as they like.
/// </summary>
public static class PurchaseWatchdogPolicy
{
    /// <summary>
    /// Seconds a store call may run without ANY system UI before it is declared hung. On a
    /// healthy device the sheet is up well inside 5 s (measured ~1 s on the iPad simulator);
    /// a slow sandbox login still resigns focus long before this elapses.
    /// </summary>
    public const float SheetGraceSeconds = 12f;

    /// <summary>Failure reason the watchdog hands to the callers' notice logic (never a cancel).</summary>
    public const string HungReason = "store_timeout";

    public static bool ShouldGiveUp(bool callbackArrived, bool sheetAppeared, float elapsedSeconds)
    {
        if (callbackArrived) return false;
        if (sheetAppeared) return false;
        return elapsedSeconds >= SheetGraceSeconds;
    }
}
