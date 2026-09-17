using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Drives the legacy-profile claim (A1): bots created before the server identity existed (or on a
/// previous install) are not owned by this install's session, so the proxy refuses their chats.
/// At boot, once the session is ready, the first such bot is claimed: the server sends a code into
/// the bot's own chat (authorised channel) or asks for a fresh pairing (unauthorised), and the
/// sheet guides the owner. A channel that becomes authorised while a claim is pending reports back
/// through <see cref="AfterChannelAuthorized"/>, which is the re-auth binding step.
/// </summary>
public static class ProfileClaimFlow
{
    private static readonly HashSet<string> Snoozed = new HashSet<string>();
    private static bool running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Snoozed.Clear();
        running = false;
    }

    /// <summary>The device's connected profiles, in card order.</summary>
    public static IEnumerable<ProfileClaimPolicy.Candidate> ConnectedProfiles(Transform botsRoot)
    {
        if (botsRoot == null) yield break;
        for (int i = 0; i < botsRoot.childCount; i++)
        {
            var bot = botsRoot.GetChild(i).GetComponent<Bot>();
            if (bot == null) continue;
            string name = bot.BotName != null ? bot.BotName.text : bot.name;
            if (ProfileClaimPolicy.IsAuthedId(bot.whatsappProfileId))
                yield return new ProfileClaimPolicy.Candidate { ProfileId = bot.whatsappProfileId, Channel = ChatChannel.WhatsApp, BotName = name };
            if (ProfileClaimPolicy.IsAuthedId(bot.telegramProfileId))
                yield return new ProfileClaimPolicy.Candidate { ProfileId = bot.telegramProfileId, Channel = ChatChannel.Telegram, BotName = name };
        }
    }

    private static TMP_FontAsset FontFrom(Transform botsRoot)
    {
        if (botsRoot == null || botsRoot.childCount == 0) return null;
        var bot = botsRoot.GetChild(0).GetComponent<Bot>();
        return bot != null && bot.BotName != null ? bot.BotName.font : null;
    }

    /// <summary>Boot entry: wait for the session, then claim the first unowned connected profile (one at a time).</summary>
    public static IEnumerator RunAtBoot(Transform botsRoot)
    {
        if (running) yield break;
        running = true;
        try
        {
            var auth = AuthClient.Instance;
            if (auth == null) yield break;
            yield return auth.EnsureSession();
            while (true)
            {
                var next = ProfileClaimPolicy.NextUnowned(ConnectedProfiles(botsRoot), auth.Claims, Snoozed);
                if (next == null) yield break;
                bool done = false;
                yield return ClaimOne(next.Value, FontFrom(botsRoot), ok => done = ok);
                if (!done) yield break;   // snoozed or failed: stop for this session, the next launch retries
            }
        }
        finally
        {
            running = false;
        }
    }

    private static IEnumerator ClaimOne(ProfileClaimPolicy.Candidate c, TMP_FontAsset font, System.Action<bool> finished)
    {
        var auth = AuthClient.Instance;
        AuthClient.ClaimResult start = null;
        yield return auth.Claim("claim.start", c.ProfileId, c.Channel, null, r => start = r);
        if (start == null || !start.Success)
        {
            Debug.LogWarning($"[ProfileClaim] claim.start for {c.ProfileId} failed: {start?.Error} (http {start?.HttpStatus})");
            finished(false);
            yield break;
        }
        var mode = ProfileClaimPolicy.ModeFor(start.Method);
        if (mode == ProfileClaimPolicy.SheetMode.Owned)
        {
            finished(true);                              // already ours (token was just refreshed)
            yield break;
        }
        bool resolved = false, claimed = false;
        if (mode == ProfileClaimPolicy.SheetMode.Reauth)
        {
            Snoozed.Add(c.ProfileId);                    // bound later by AfterChannelAuthorized; do not nag this session
            ProfileClaimSheet.Show(font, mode, c.BotName, c.Channel, null, () => resolved = true);
            while (!resolved) yield return null;
            finished(false);
            yield break;
        }
        ProfileClaimSheet.Show(font, mode, c.BotName, c.Channel,
            code => auth.StartCoroutine(VerifyCode(c, code, ok => { claimed = ok; if (ok) resolved = true; })),
            () => { Snoozed.Add(c.ProfileId); resolved = true; });
        while (!resolved) yield return null;
        if (claimed) ProfileClaimSheet.Hide();
        finished(claimed);
    }

    private static IEnumerator VerifyCode(ProfileClaimPolicy.Candidate c, string code, System.Action<bool> done)
    {
        AuthClient.ClaimResult verify = null;
        yield return AuthClient.Instance.Claim("claim.verify", c.ProfileId, c.Channel, code, r => verify = r);
        if (verify != null && verify.Success)
        {
            done(true);
            yield break;
        }
        ProfileClaimSheet.ShowError(verify != null && verify.Error == "claim_code_rejected" ? ProfileClaimCopy.ErrorWrongCode : ProfileClaimCopy.ErrorGeneric);
        done(false);
    }

    /// <summary>A channel just reported authorized: if a claim is pending for it, bind it (claim.status re-issues the token).</summary>
    public static IEnumerator AfterChannelAuthorized(string profileId, ChatChannel channel)
    {
        var auth = AuthClient.Existing;
        if (auth == null || !ProfileClaimPolicy.ShouldReportAuthorization(profileId, auth.Claims)) yield break;
        AuthClient.ClaimResult res = null;
        yield return auth.Claim("claim.status", profileId, channel, null, r => res = r);
        if (res == null || !res.Success)
            Debug.LogWarning($"[ProfileClaim] claim.status for {profileId} did not bind: {res?.Error} (http {res?.HttpStatus})");
    }
}
