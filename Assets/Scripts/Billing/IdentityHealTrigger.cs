using System;
using System.Globalization;
using UnityEngine;

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
/// The merge persists for the life of the install (original ≠ current never goes away), so the decision is latched
/// (fix round 2026-10-15): a pair the server HEALED — a 2xx carrying success:true, <see cref="ShouldStamp"/> — is asked
/// again only after <see cref="RecallAfterMs"/> (something may have landed under the old id since); a NEW pair is always
/// asked; a refusal stamps nothing. The latch lives in <see cref="IdentityHealLatch"/> (PlayerPrefs).
///
/// Pure, EditMode-pinned (IdentityHealTriggerTests).
/// </summary>
public static class IdentityHealTrigger
{
    /// <summary>A healed pair is asked again after 24 h at the earliest.</summary>
    public const long RecallAfterMs = 24L * 60 * 60 * 1000;

    /// <summary>The latch key of one (original, current) pair — ordered: a swapped pair is another merge.</summary>
    public static string PairOf(string current, string original) => $"{original}|{current}";

    /// <summary>
    /// True only when RevenueCat reports a merge — <paramref name="original"/> ≠ <paramref name="current"/>, both
    /// non-empty — while the token carries a billing identity (<paramref name="billingConflict"/> false: otherwise there is
    /// no id to heal INTO and the gate would refuse the call), and the persisted latch allows it: this pair is not the one
    /// last healed (<paramref name="lastPair"/>), or that heal is at least <see cref="RecallAfterMs"/> old. A stamp from the
    /// future (the device clock moved back) never blocks.
    /// </summary>
    public static bool ShouldCall(string current, string original, bool billingConflict, string lastPair, long lastHealedAtMs, long nowMs)
        => !string.IsNullOrWhiteSpace(current)
        && !string.IsNullOrWhiteSpace(original)
        && !string.Equals(current, original, StringComparison.Ordinal)
        && !billingConflict
        && (!string.Equals(lastPair, PairOf(current, original), StringComparison.Ordinal)
            || nowMs - lastHealedAtMs >= RecallAfterMs
            || nowMs < lastHealedAtMs);

    /// <summary>
    /// Only the server's positive answer stamps the latch: a 2xx whose body carries the boolean <c>success: true</c> (a
    /// repeat that moved nothing included — the pair IS healed). A refusal (<c>success:false</c>: rc_unavailable,
    /// hint_not_alias, …), a non-2xx, no answer or an unreadable body stamps nothing — the pair is asked again.
    /// </summary>
    public static bool ShouldStamp(long responseCode, string body)
        => responseCode >= 200 && responseCode < 300 && VerifyIdentityRequest.Succeeded(body);
}

/// <summary>
/// The persisted identity-heal latch: the last (original, current) pair the server HEALED and when (unix ms). PlayerPrefs,
/// outside <c>CrAuth.*</c> on purpose — «Удалить все данные» keeps only the install's credentials, so the latch dies with
/// the wipe and costs one extra call. Static Func/Action seams (the ThemePrefs / NotifPrefs pattern) let EditMode tests use
/// an in-memory store.
/// </summary>
public static class IdentityHealLatch
{
    public const string LastPairKey = "CrIdentityHeal.LastPair";
    public const string LastHealedAtMsKey = "CrIdentityHeal.LastHealedAtMs";

    internal static Func<string, string, string> GetString = PlayerPrefs.GetString;
    internal static Action<string, string> SetString = PlayerPrefs.SetString;
    internal static Action Save = PlayerPrefs.Save;

    internal static void ResetSeamsForTests()
    {
        GetString = PlayerPrefs.GetString;
        SetString = PlayerPrefs.SetString;
        Save = PlayerPrefs.Save;
    }

    /// <summary>The pair last healed ("" = none).</summary>
    public static string LastPair => GetString(LastPairKey, "") ?? "";

    /// <summary>When it was healed (unix ms; 0 = never, or an unreadable value).</summary>
    public static long LastHealedAtMs =>
        long.TryParse(GetString(LastHealedAtMsKey, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms) ? ms : 0L;

    /// <summary>Record a heal and flush at once (after backgrounding the OS may kill the app without another callback).</summary>
    public static void Stamp(string pair, long nowMs)
    {
        SetString(LastPairKey, pair ?? "");
        SetString(LastHealedAtMsKey, nowMs.ToString(CultureInfo.InvariantCulture));
        Save();
    }
}
