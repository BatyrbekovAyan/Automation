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
        /// <summary>The bot GameObject's name — the PlayerPrefs prefix of its keys (M4: where the retired marker lives).</summary>
        public string BotKey;
        /// <summary>M4: the profile id this bot's channel was marked retired for ("" = never).</summary>
        public string RetiredMarkerId;
    }

    /// <summary>A profile id the client treats as connected (not the unauthed sentinel, not empty).</summary>
    public static bool IsAuthedId(string profileId) =>
        !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    /// <summary>
    /// The first connected profile the token does not own, skipping ones snoozed for this session, ones in
    /// <paramref name="skip"/> (M4: already asked in this run) and channels marked retired. Null when everything is
    /// owned (the common case). V2 (2026-09-28): a claim-pending profile is NOT skipped any more — a pairing never binds
    /// it (Wappi does not say which link session completed a pairing), so an authorised pending profile needs the code
    /// step at the next launch, and an unauthorised one answers «reauth» (snoozed, no loop).
    /// </summary>
    public static Candidate? NextUnowned(IEnumerable<Candidate> connected, InstallSession.TokenClaims claims, ISet<string> snoozed,
                                         ISet<string> skip = null)
    {
        if (claims == null) return null;                       // no session yet — nothing can be claimed
        foreach (var c in connected)
        {
            if (!IsAuthedId(c.ProfileId)) continue;
            if (snoozed != null && snoozed.Contains(c.ProfileId)) continue;
            if (skip != null && skip.Contains(c.ProfileId)) continue;
            if (System.Array.IndexOf(claims.Profiles, c.ProfileId) >= 0) continue;
            if (RetiredMarkerApplies(c.RetiredMarkerId, c.ProfileId, claims)) continue;
            return c;
        }
        return null;
    }

    /// <summary>M4: the server's refusal for a profile it retired (410) — the sweep, an unfinished delete, a refused channel.</summary>
    public const string ErrorRetired = "profile_retired";

    public static bool IsRetired(string error) => error == ErrorRetired;

    /// <summary>
    /// M4: a retired-channel marker counts only for the id it was written for (a new connection gets a new id and the
    /// marker no longer matches), and it yields to the token — a token that lists the profile again wins.
    /// </summary>
    public static bool RetiredMarkerApplies(string markerId, string profileId, InstallSession.TokenClaims claims) =>
        IsAuthedId(profileId) && markerId == profileId
        && (claims == null || System.Array.IndexOf(claims.Profiles, profileId) < 0);

    /// <summary>A channel just reported authorized: only a claim-pending profile needs the claim (code) step.</summary>
    public static bool ShouldReportAuthorization(string profileId, InstallSession.TokenClaims claims) =>
        claims != null && IsAuthedId(profileId) && System.Array.IndexOf(claims.ClaimPending, profileId) >= 0;

    /// <summary>What a claim.start answer asks the code sheet to do next (V2).</summary>
    public enum StartStep
    {
        /// <summary>The profile is already this install's — nothing to confirm.</summary>
        Proceed,
        /// <summary>A code is in the bot's own chat (sent now or still valid): ask for it.</summary>
        EnterCode,
        /// <summary>No usable code (not sent, dead after wrong answers, the pairing not visible yet, no network): offer a resend.</summary>
        Resend,
    }

    public static StartStep AfterStart(bool success, string claimMethod)
    {
        if (success && claimMethod == "owned") return StartStep.Proceed;
        if (success && claimMethod == "code") return StartStep.EnterCode;
        return StartStep.Resend;
    }

    /// <summary>The server killed the code (five wrong answers): only a new one, after its window, can bind.</summary>
    public static bool IsCodeDead(string error) => error == "claim_code_locked";

    /// <summary>The RU line the sheet shows for a server refusal (never the server's own text).</summary>
    public static string ErrorCopyFor(string error) => error switch
    {
        "claim_code_rejected" => ProfileClaimCopy.ErrorWrongCode,
        "claim_code_locked" => ProfileClaimCopy.ErrorLocked,
        "code_send_failed" => ProfileClaimCopy.ErrorSendFailed,
        _ => ProfileClaimCopy.ErrorGeneric,
    };

    /// <summary>Maps the server's claimMethod to what the sheet shows.</summary>
    public enum SheetMode { Code, Reauth, Owned }

    public static SheetMode ModeFor(string claimMethod) => claimMethod switch
    {
        "code" => SheetMode.Code,
        "owned" => SheetMode.Owned,
        _ => SheetMode.Reauth,
    };
}

/// <summary>
/// M4 (follow-ups 2026-10-09): one boot run of the claim loop. Every profile is handed out at most ONCE per run and is
/// recorded as asked BEFORE the call, so no server answer — «owned» for a row the token never lists was the one that
/// spun the loop for the whole launch — can make it ask the same profile again. The loop is therefore bounded by the
/// number of connected profiles. Pure — EditMode-tested.
/// </summary>
public sealed class ProfileClaimRun
{
    private readonly HashSet<string> asked = new HashSet<string>();

    public int AskedCount => asked.Count;

    public bool WasAsked(string profileId) => asked.Contains(profileId);

    public ProfileClaimPolicy.Candidate? TakeNext(IEnumerable<ProfileClaimPolicy.Candidate> connected,
                                                  InstallSession.TokenClaims claims, ISet<string> snoozed)
    {
        var next = ProfileClaimPolicy.NextUnowned(connected, claims, snoozed, asked);
        if (next.HasValue) asked.Add(next.Value.ProfileId);
        return next;
    }
}
