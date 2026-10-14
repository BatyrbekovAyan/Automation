using System;

/// <summary>
/// A3 «защита платящих» (2026-10-14) — WHEN the client asks the server to heal its billing mirror after a reinstall
/// (POST /webhook/VerifyIdentity, <c>Manager.VerifyIdentityRoutine</c>).
///
/// A reinstall gets a new anonymous RevenueCat id. RevenueCat merges it with the old one — CustomerInfo.OriginalAppUserId
/// keeps the customer's FIRST id — but the server's subscription mirror (plan, period, balance, this month's dialogs) stays
/// under the old id until RevenueCat's next event, which can be a month away: trial limits meanwhile, and the lifecycle
/// sweep deleting the bots created after the reinstall. The original id is only a HINT: the server asks RevenueCat itself
/// for the aliases of the token's own billing id and consolidates them INTO that id.
///
/// Pure, EditMode-pinned (IdentityHealTriggerTests); the caller keeps <c>lastCalledPair</c> for the launch.
/// </summary>
public static class IdentityHealTrigger
{
    /// <summary>The latch key of one (original, current) pair — the server is asked once per pair per launch.</summary>
    public static string PairOf(string current, string original) => $"{original}|{current}";

    /// <summary>
    /// True only when RevenueCat reports a merge — <paramref name="original"/> ≠ <paramref name="current"/>, both
    /// non-empty — while the token carries a billing identity (<paramref name="billingConflict"/> false: otherwise there is
    /// no id to heal INTO and the gate would refuse the call), and this pair was not called yet this launch.
    /// </summary>
    public static bool ShouldCall(string current, string original, bool billingConflict, string lastCalledPair)
        => !string.IsNullOrWhiteSpace(current)
        && !string.IsNullOrWhiteSpace(original)
        && !string.Equals(current, original, StringComparison.Ordinal)
        && !billingConflict
        && !string.Equals(lastCalledPair, PairOf(current, original), StringComparison.Ordinal);
}
