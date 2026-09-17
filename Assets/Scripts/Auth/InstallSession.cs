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

    /// <summary>No install credentials on this device yet → bootstrap (never refresh).</summary>
    public static bool NeedsBootstrap(string installId, string installSecret)
        => string.IsNullOrEmpty(installId) || string.IsNullOrEmpty(installSecret);

    /// <summary>True when the token is missing, expired, or inside the lead window.</summary>
    public static bool ShouldRefresh(long nowUnix, InstallSession.TokenClaims claims)
        => claims == null || claims.Exp - nowUnix < LeadSeconds;

    /// <summary>
    /// The billing identity the token was minted for no longer matches the one the client sends
    /// (RevenueCat initialised after bootstrap, or an alias/transfer): the gates compare the two,
    /// so the token must be re-issued before the next billing-bound call.
    /// </summary>
    public static bool IdentityChanged(InstallSession.TokenClaims claims, string currentAppUserId)
        => claims != null && !string.IsNullOrEmpty(currentAppUserId) && claims.AppUserId != currentAppUserId;
}
