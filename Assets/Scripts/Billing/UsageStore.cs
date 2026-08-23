using System;
using Newtonsoft.Json;

/// <summary>
/// Wire model for a Task 11 GetUsage response. Field names match the JSON verbatim
/// (camelCase, same convention as DashboardResponse/DashboardOutcome) so Newtonsoft's
/// default contract resolver round-trips them with no attributes.
/// </summary>
[Serializable]
public class UsageSnapshot
{
    public bool success;
    public string plan;
    public string status;
    public int quota;
    public int used;
    public int topupBalance;
    public int botsRegistered;
    public int channelsConnected;
    public string periodEnd;   // ISO-8601 or null (no active/known period end)

    /// <summary>
    /// Raw store SKU behind the current subscription (<c>sub.business.year</c>, …), or null.
    /// Carried for diagnosis only — nothing branches on it client-side; the server already
    /// reduced it to <see cref="interval"/>, so the suffix rule lives in exactly one place.
    /// </summary>
    public string productId;

    /// <summary>
    /// «month» / «year» / null — the billing period, derived server-side from the SKU suffix
    /// (Get Usage's Shape Response). Null means UNKNOWN (unrecognised SKU, or a payload from
    /// before Task 15a), never «month»: the fallback belongs to the display seam, which treats
    /// monthly as the known default (<see cref="SubscriptionPageRows.ActiveSubline"/>).
    /// </summary>
    public string interval;
}

/// <summary>
/// Client-side cache of the last successful <c>/webhook/GetUsage</c> read (Task 11) —
/// plan/quota/usage snapshot for the current device's <see cref="BillingIdentity.AppUserId"/>.
/// Purely an in-memory mirror (no PlayerPrefs persistence): a cold boot starts with
/// <see cref="Current"/> null until <see cref="UsageClient.FetchRoutine"/> lands its first
/// response, and every caller must treat that as "unknown yet", not "zero usage".
///
/// House pattern (BillingService/EntitlementGate/BillingIdentity): <see cref="Parse"/> and
/// <see cref="Apply"/> are split so a test can exercise the pure JSON→model mapping without
/// touching <see cref="Current"/> or firing <see cref="OnUsageChanged"/>, and so
/// <see cref="UsageClient"/> owns the HTTP/error-handling decision of WHETHER to apply a
/// result (a non-200 or a garbage body must keep the existing cache, never null it out).
/// </summary>
public static class UsageStore
{
    /// <summary>Last snapshot successfully applied; null until the first successful fetch.</summary>
    public static UsageSnapshot Current { get; private set; }

    /// <summary>Fires after every <see cref="Apply"/> that actually replaces <see cref="Current"/>.</summary>
    public static event Action OnUsageChanged;

    internal static void ResetSeamsForTests()
    {
        Current = null;
        OnUsageChanged = null;   // a leaked subscriber from one test would fire in every test after it
    }

    /// <summary>
    /// Parses a GetUsage response body into a <see cref="UsageSnapshot"/>. Returns null on
    /// empty input or malformed/unexpected JSON — never throws (mirrors DashboardResponse.Parse).
    /// </summary>
    public static UsageSnapshot Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonConvert.DeserializeObject<UsageSnapshot>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stores a freshly parsed snapshot as <see cref="Current"/> and notifies subscribers.
    /// No-op on null (a failed/garbage parse must never clear an already-cached snapshot).
    /// </summary>
    public static void Apply(UsageSnapshot snapshot)
    {
        if (snapshot == null) return;
        Current = snapshot;
        OnUsageChanged?.Invoke();
    }
}
