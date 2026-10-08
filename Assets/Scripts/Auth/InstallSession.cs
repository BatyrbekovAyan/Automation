using System;
using System.Text;
using Newtonsoft.Json.Linq;

/// <summary>
/// Pure model of the A1 server session (audit remediation, stage 1 — see
/// docs/superpowers/specs/2026-09-17-a1-server-identity-design.md).
///
/// The server mints an INSTALL (installId + installSecret, returned once) and issues short-lived
/// capability tokens <c>v1.&lt;b64url(payload)&gt;.&lt;b64url(hmac)&gt;</c> whose payload lists the
/// profile / workflow ids this install owns. The client never verifies the signature (it has no
/// secret and must not) — it only READS the payload to know when to refresh and what it owns.
/// Every decision here is a pure function so it is EditMode-testable.
/// </summary>
public static class InstallSession
{
    public sealed class TokenClaims
    {
        public string InstallId;
        public string AppUserId;          // null when the token carries no billing identity yet
        public string[] Profiles = Array.Empty<string>();
        public string[] Workflows = Array.Empty<string>();
        public string[] ClaimPending = Array.Empty<string>();
        public long Exp;                  // unix seconds, exclusive
        public int Version = 1;
    }

    /// <summary>Decodes the payload of a v1 token WITHOUT verifying it. False on any malformation.</summary>
    public static bool TryParse(string token, out TokenClaims claims)
    {
        claims = null;
        if (string.IsNullOrEmpty(token)) return false;
        string[] parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != "v1" || parts[1].Length == 0 || parts[2].Length == 0) return false;
        try
        {
            var jo = JObject.Parse(Encoding.UTF8.GetString(FromBase64Url(parts[1])));
            string i = jo.Value<string>("i");
            var p = jo["p"] as JArray;
            var w = jo["w"] as JArray;
            if (string.IsNullOrEmpty(i) || p == null || w == null || jo["e"] == null || jo["e"].Type != JTokenType.Integer) return false;
            claims = new TokenClaims
            {
                InstallId = i,
                AppUserId = string.IsNullOrEmpty(jo.Value<string>("u")) ? null : jo.Value<string>("u"),
                Profiles = p.ToObject<string[]>() ?? Array.Empty<string>(),
                Workflows = w.ToObject<string[]>() ?? Array.Empty<string>(),
                ClaimPending = (jo["c"] as JArray)?.ToObject<string[]>() ?? Array.Empty<string>(),
                Exp = jo.Value<long>("e"),
                Version = jo["n"] != null && jo["n"].Type == JTokenType.Integer ? jo.Value<int>("n") : 1,
            };
            return true;
        }
        catch
        {
            claims = null;
            return false;
        }
    }

    public static byte[] FromBase64Url(string s)
    {
        string b = s.Replace('-', '+').Replace('_', '/');
        switch (b.Length % 4)
        {
            case 2: b += "=="; break;
            case 3: b += "="; break;
        }
        return Convert.FromBase64String(b);
    }
}

/// <summary>Pure refresh decisions; the AuthClient host applies them.</summary>
public static class SessionRefreshPolicy
{
    /// <summary>Refresh this far before expiry so a request never straddles the deadline (12 h tokens, 2 h lead).</summary>
    public const long LeadSeconds = 2 * 3600;

    /// <summary>The server's positive verdict that this install is gone (wrong secret / unknown / revoked).</summary>
    public const string InstallRevokedError = "install_revoked";

    /// <summary>
    /// R11 (2026-09-27): the install credentials are discarded ONLY on the server's explicit verdict — a refresh
    /// answered 401 with <see cref="InstallRevokedError"/>. A bare 401, a 5xx (503 <c>db_unavailable</c> is what a
    /// database fault answers), a network error or a bootstrap failure keep them: the secret cannot be recovered
    /// once wiped, and every bot would then have to be claimed back one by one.
    /// </summary>
    public static bool ShouldDiscardInstall(string op, long httpStatus, string error)
        => op == "refresh" && httpStatus == 401 && error == InstallRevokedError;

    /// <summary>No install credentials on this device yet → bootstrap (never refresh).</summary>
    public static bool NeedsBootstrap(string installId, string installSecret)
        => string.IsNullOrEmpty(installId) || string.IsNullOrEmpty(installSecret);

    /// <summary>True when the token is missing, expired, or inside the lead window.</summary>
    public static bool ShouldRefresh(long nowUnix, InstallSession.TokenClaims claims)
        => claims == null || claims.Exp - nowUnix < LeadSeconds;

    /// <summary>
    /// The client's billing identity changed since the token was requested (RevenueCat initialised after
    /// bootstrap, or an alias/transfer): the gates compare the body's id with the token's, so the token must be
    /// re-issued before the next billing-bound call. R2 (2026-09-27): the server binds a billing id to ONE install —
    /// a token may carry no billing identity (u = null) because the id it was ASKED for is held by another install
    /// (billingConflict) or is not a RevenueCat id. That is a server verdict, not a change: once a session op has
    /// asked for <paramref name="currentAppUserId"/> (<paramref name="lastRequestedAppUserId"/>), asking again on
    /// every call would only repeat it (a refresh storm of ~2 s round trips). The token's expiry refresh re-asks.
    /// </summary>
    public static bool IdentityChanged(InstallSession.TokenClaims claims, string currentAppUserId, string lastRequestedAppUserId)
        => claims != null && !string.IsNullOrEmpty(currentAppUserId) && claims.AppUserId != currentAppUserId
           && currentAppUserId != lastRequestedAppUserId;

    /// <summary>
    /// V3 (2026-09-28): the server binds a billing id ONLY on a refresh — an install that has proven its secret.
    /// Bootstrap is unauthenticated, so it binds nothing (an orphaned bootstrap whose answer was lost can never hold
    /// anyone's id). A bootstrap that named an id is therefore followed by a refresh in the same session op.
    /// </summary>
    public static bool RefreshAfterBootstrap(string op, string requestedAppUserId)
        => op == "bootstrap" && !string.IsNullOrEmpty(requestedAppUserId);

    /// <summary>
    /// What to record as «asked for» after a successful session op: a bootstrap asked for nothing the server could
    /// grant (V3), so it records none — the next <see cref="IdentityChanged"/> check then asks with a refresh.
    /// </summary>
    public static string RequestedIdToRecord(string op, string requestedAppUserId)
        => op == "bootstrap" ? "" : (requestedAppUserId ?? "");

    /// <summary>Only a RevenueCat anonymous id is bindable (the server's rule — a device-id fallback never is).</summary>
    public static bool IsBindableBillingId(string appUserId)
    {
        const string prefix = "$RCAnonymousID:";
        if (appUserId == null || appUserId.Length != prefix.Length + 32 || !appUserId.StartsWith(prefix, StringComparison.Ordinal)) return false;
        for (int i = prefix.Length; i < appUserId.Length; i++)
        {
            char ch = appUserId[i];
            if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'))) return false;
        }
        return true;
    }

    /// <summary>Minimum gap between two resume-time retries of a declined billing id.</summary>
    public const long BillingRetryGapSeconds = 60;

    /// <summary>
    /// V3: the token carries no (or another) billing identity for this device's RevenueCat id — billingConflict, or a
    /// bootstrap whose follow-up refresh did not happen. The only cure on the server is a revoke of the holding install
    /// (support), after which a refresh takes the id over; without a retry on resume the client would wait for the
    /// planned refresh (~10 h). At most once a minute, never while a session op is in flight.
    /// </summary>
    public static bool ShouldRetryBillingOnResume(bool hasFocus, bool hasInstall, bool inFlight, InstallSession.TokenClaims claims,
                                                  string currentAppUserId, long nowSec, long lastAttemptSec)
        => hasFocus && hasInstall && !inFlight && claims != null && IsBindableBillingId(currentAppUserId)
           && claims.AppUserId != currentAppUserId && nowSec - lastAttemptSec >= BillingRetryGapSeconds;
}
