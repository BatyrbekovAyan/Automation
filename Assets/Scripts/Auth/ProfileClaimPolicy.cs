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
    /// The first connected profile the token does not own, skipping ones snoozed for this session. Null when
    /// everything is owned (the common case). V2 (2026-09-28): a claim-pending profile is NOT skipped any more — a
    /// pairing never binds it (Wappi does not say which link session completed a pairing), so an authorised pending
    /// profile needs the code step at the next launch, and an unauthorised one answers «reauth» (snoozed, no loop).
    /// </summary>
    public static Candidate? NextUnowned(IEnumerable<Candidate> connected, InstallSession.TokenClaims claims, ISet<string> snoozed)
    {
        if (claims == null) return null;                       // no session yet — nothing can be claimed
        foreach (var c in connected)
        {
            if (!IsAuthedId(c.ProfileId)) continue;
            if (snoozed != null && snoozed.Contains(c.ProfileId)) continue;
            if (System.Array.IndexOf(claims.Profiles, c.ProfileId) >= 0) continue;
            return c;
        }
        return null;
    }

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
