/// <summary>
/// Pure decision seam for re-reading GetUsage after a successful top-up purchase.
///
/// Why it exists (prod evidence, 2026-09-06 15:20:19Z): RevenueCat Events execution 1546 credited
/// the reserve 1 500 → 2 000, and the app's GetUsage read 1547 — fired from the store callback in
/// the SAME second — still returned 1 500. The store's purchase callback lands before RevenueCat's
/// webhook has written the balance, so one immediate read is a coin flip, and the page said
/// «Диалоги начислены» over the old number until it was reopened 22 s later. The settle re-reads
/// on this schedule until the reserve has grown by what was bought. The schedule is FINITE on
/// purpose: a purchase the server never credits (an upstream fault, not this client's) costs five
/// small reads and then stops, instead of polling for the rest of the session.
///
/// Nothing here touches Unity or the network — <c>ProfileSubPages.TopUpSettleRoutine</c> is the
/// thin host. Pinned by <c>TopUpSettlePolicyTests</c>.
/// </summary>
public static class TopUpSettlePolicy
{
    /// <summary>
    /// Seconds to wait BEFORE each read. The first read is immediate (the pre-fix behaviour, and
    /// the one that wins whenever the webhook did land first); then 2/5/10/20 — about 37 s in
    /// total, past the webhook's usual sub-second landing and RevenueCat's first retry.
    /// </summary>
    public static readonly float[] ReadDelaysSeconds = { 0f, 2f, 5f, 10f, 20f };

    /// <summary>Delay before read number <paramref name="readIndex"/> (0-based), or null once the schedule is spent.</summary>
    public static float? DelayBeforeRead(int readIndex)
        => readIndex >= 0 && readIndex < ReadDelaysSeconds.Length ? ReadDelaysSeconds[readIndex] : (float?)null;

    /// <summary>
    /// True once the server has caught up: the reserve grew by at least the packs bought since the
    /// baseline was taken (a second pack bought while the first is still landing raises the bar
    /// rather than restarting the clock). An UNKNOWN baseline never settles — with nothing to
    /// compare against, every read on the schedule is taken and the page simply converges on
    /// whatever the server says.
    /// </summary>
    public static bool Settled(int? balanceBefore, int packsBought, int balanceNow)
        => balanceBefore.HasValue
        && balanceNow >= balanceBefore.Value + packsBought * PlanCatalog.TopUpDialogs;
}
