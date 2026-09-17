using System.Collections.Generic;

/// <summary>
/// Pure decisions of the legacy-profile claim flow (A1): which of the device's bot profiles the
/// current session does NOT own yet, and when a channel authorisation should be reported back as
/// a claim step. No Unity types — EditMode-tested.
/// </summary>
public static class ProfileClaimPolicy
{
    public struct Candidate
    {
        public string ProfileId;
        public ChatChannel Channel;
        public string BotName;
    }

    /// <summary>A profile id the client treats as connected (not the unauthed sentinel, not empty).</summary>
    public static bool IsAuthedId(string profileId) =>
        !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    /// <summary>
    /// The first connected profile the token neither owns nor already has a pending claim for,
    /// skipping ones snoozed for this session. Null when everything is owned (the common case).
    /// </summary>
    public static Candidate? NextUnowned(IEnumerable<Candidate> connected, InstallSession.TokenClaims claims, ISet<string> snoozed)
    {
        if (claims == null) return null;                       // no session yet — nothing can be claimed
        foreach (var c in connected)
        {
            if (!IsAuthedId(c.ProfileId)) continue;
            if (snoozed != null && snoozed.Contains(c.ProfileId)) continue;
            if (System.Array.IndexOf(claims.Profiles, c.ProfileId) >= 0) continue;
            if (System.Array.IndexOf(claims.ClaimPending, c.ProfileId) >= 0) continue;   // re-auth in progress: bound on claim.status
            return c;
        }
        return null;
    }

    /// <summary>A channel just reported authorized: only a claim-pending profile needs the claim.status round trip.</summary>
    public static bool ShouldReportAuthorization(string profileId, InstallSession.TokenClaims claims) =>
        claims != null && IsAuthedId(profileId) && System.Array.IndexOf(claims.ClaimPending, profileId) >= 0;

    /// <summary>Maps the server's claimMethod to what the sheet shows.</summary>
    public enum SheetMode { Code, Reauth, Owned }

    public static SheetMode ModeFor(string claimMethod) => claimMethod switch
    {
        "code" => SheetMode.Code,
        "owned" => SheetMode.Owned,
        _ => SheetMode.Reauth,
    };
}
