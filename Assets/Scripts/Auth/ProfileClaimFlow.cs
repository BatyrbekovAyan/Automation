using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Drives the legacy-profile claim (A1): bots created before the server identity existed (or on a
/// previous install) are not owned by this install's session, so the proxy refuses their chats.
/// At boot, once the session is ready, the first such bot is claimed: the server sends a code into
/// the bot's own chat (authorised channel) or asks for a fresh pairing (unauthorised), and the
/// sheet guides the owner.
///
/// V2 (2026-09-28): a pairing NEVER binds a profile — Wappi does not say which link session completed
/// it, so someone else's re-connection inside the window would hand the profile over. The code in the
/// bot's own chat is the only proof: a channel that becomes authorised while a claim is pending reports
/// back through <see cref="AfterChannelAuthorized"/>, which asks for that code (claim.start →
/// claim.verify) before the caller may create a workflow for the profile.
///
/// M4 (2026-10-09): a profile the server RETIRED (the sweep after a trial or a subscription ended, gone from Wappi, an
/// unfinished delete) answers 410 profile_retired: the bot's channel is marked (<see cref="ProfileRetirement"/>), the
/// card says the channel is off, and the loop goes on to the next bot. Each profile is asked at most once per run
/// (<see cref="ProfileClaimRun"/>) — the server used to answer «owned» for such a row, which spun this loop forever.
///
/// F2 (plan 2026-10-11, task T8): the mark now MOVES the channel's ids aside (<see cref="ProfileRetirement.Mark"/>), so the
/// channel reads as disconnected everywhere and its reconnect takes the fresh path. Markers the M4 build wrote (ids kept)
/// are moved the same way at boot, once the session is known.
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
                yield return new ProfileClaimPolicy.Candidate
                {
                    ProfileId = bot.whatsappProfileId, Channel = ChatChannel.WhatsApp, BotName = name,
                    BotKey = bot.name, RetiredMarkerId = ProfileRetirement.MarkerId(bot.name, ChatChannel.WhatsApp),
                };
            if (ProfileClaimPolicy.IsAuthedId(bot.telegramProfileId))
                yield return new ProfileClaimPolicy.Candidate
                {
                    ProfileId = bot.telegramProfileId, Channel = ChatChannel.Telegram, BotName = name,
                    BotKey = bot.name, RetiredMarkerId = ProfileRetirement.MarkerId(bot.name, ChatChannel.Telegram),
                };
        }
    }

    private static TMP_FontAsset FontFrom(Transform botsRoot)
    {
        if (botsRoot == null || botsRoot.childCount == 0) return null;
        var bot = botsRoot.GetChild(0).GetComponent<Bot>();
        return bot != null && bot.BotName != null ? bot.BotName.font : null;
    }

    /// <summary>The bot card whose GameObject name is <paramref name="botKey"/> (null when it is gone).</summary>
    private static Bot BotFor(Transform botsRoot, string botKey)
    {
        if (botsRoot == null || string.IsNullOrEmpty(botKey)) return null;
        for (int i = 0; i < botsRoot.childCount; i++)
        {
            var bot = botsRoot.GetChild(i).GetComponent<Bot>();
            if (bot != null && bot.name == botKey) return bot;
        }
        return null;
    }

    private static ProfileClaimPolicy.Candidate CandidateFor(Transform botsRoot, string profileId, ChatChannel channel)
    {
        foreach (var c in ConnectedProfiles(botsRoot))
            if (c.ProfileId == profileId) return c;
        return new ProfileClaimPolicy.Candidate { ProfileId = profileId, Channel = channel, BotName = "" };
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
            // F2: an M4 marker kept the channel's ids — move them aside before the loop reads the connected profiles.
            ProfileRetirement.TransferLegacyMarkers(botsRoot, auth.Claims);
            var run = new ProfileClaimRun();   // M4: each profile at most once per run — bounded by the connected profiles
            while (true)
            {
                var next = run.TakeNext(ConnectedProfiles(botsRoot), auth.Claims, Snoozed);
                if (next == null) yield break;
                bool done = false;
                yield return ClaimOne(next.Value, botsRoot, ok => done = ok);
                if (!done) yield break;   // snoozed or failed: stop for this session, the next launch retries
            }
        }
        finally
        {
            running = false;
        }
    }

    private static IEnumerator ClaimOne(ProfileClaimPolicy.Candidate c, Transform botsRoot, System.Action<bool> finished)
    {
        var auth = AuthClient.Instance;
        TMP_FontAsset font = FontFrom(botsRoot);
        AuthClient.ClaimResult start = null;
        yield return auth.Claim("claim.start", c.ProfileId, c.Channel, null, r => start = r);
        if (start != null && ProfileClaimPolicy.IsRetired(start.Error))
        {
            // M4: the server retired this profile. Record it on the bot's channel (the card says so, the next launch
            // does not ask again) and go on to the next bot. F2: the channel's ids move aside to its retired set (V12
            // deletes the server traces by those too); an unfinished app delete keeps them.
            ProfileRetirement.Mark(BotFor(botsRoot, c.BotKey), c.Channel, c.ProfileId, start.RetiredReason);
            finished(true);
            yield break;
        }
        bool usable = start != null && (start.Success || start.Error == "code_send_failed" || ProfileClaimPolicy.IsCodeDead(start.Error));
        if (!usable)
        {
            Debug.LogWarning($"[ProfileClaim] claim.start for {c.ProfileId} failed: {start?.Error} (http {start?.HttpStatus})");
            finished(false);
            yield break;
        }
        if (start.Success && ProfileClaimPolicy.ModeFor(start.Method) == ProfileClaimPolicy.SheetMode.Owned)
        {
            // already ours (token was just refreshed). «Owned» without the profile in the token is a server anomaly
            // (M4's shape): logged, and never asked again in this run — the run's asked set guarantees it.
            if (!auth.Owns(c.ProfileId))
                Debug.LogWarning($"[ProfileClaim] claim.start for {c.ProfileId} answered «owned» but the token does not list it — not asked again this run");
            finished(true);
            yield break;
        }
        if (start.Success && ProfileClaimPolicy.ModeFor(start.Method) == ProfileClaimPolicy.SheetMode.Reauth)
        {
            bool resolved = false;
            Snoozed.Add(c.ProfileId);                    // the owner re-pairs from the bot's settings, then enters the code there
            ProfileClaimSheet.Show(font, ProfileClaimPolicy.SheetMode.Reauth, c.BotName, c.Channel, null, () => resolved = true);
            while (!resolved) yield return null;
            finished(false);
            yield break;
        }
        bool claimed = false;
        yield return RunCodeClaim(c, font, ProfileClaimCopy.Later, start, ok => claimed = ok);
        if (!claimed) Snoozed.Add(c.ProfileId);          // «Позже»: do not nag this session
        finished(claimed);
    }

    /// <summary>
    /// The code sheet over a claim: a code is asked for, verified, and — when there is no usable code (not sent,
    /// killed by wrong answers, the pairing not visible to the server yet) — re-requested with claim.start.
    /// <paramref name="first"/> is a claim.start answer already in hand (boot) or null to send it now (after a pairing).
    /// </summary>
    private static IEnumerator RunCodeClaim(ProfileClaimPolicy.Candidate c, TMP_FontAsset font, string dismissLabel,
                                            AuthClient.ClaimResult first, System.Action<bool> finished)
    {
        var auth = AuthClient.Instance;
        bool resolved = false, claimed = false, busy = false;
        System.Action resend = null;

        void ApplyStart(AuthClient.ClaimResult r)
        {
            busy = false;
            switch (ProfileClaimPolicy.AfterStart(r != null && r.Success, r?.Method))
            {
                case ProfileClaimPolicy.StartStep.Proceed: claimed = true; resolved = true; break;
                case ProfileClaimPolicy.StartStep.EnterCode: ProfileClaimSheet.ShowCodeEntry(); break;
                default: ProfileClaimSheet.ShowResend(ProfileClaimPolicy.ErrorCopyFor(r?.Error), resend); break;
            }
        }

        resend = () =>
        {
            if (busy) return;
            busy = true;
            auth.StartCoroutine(auth.Claim("claim.start", c.ProfileId, c.Channel, null, ApplyStart));
        };

        ProfileClaimSheet.Show(font, ProfileClaimPolicy.SheetMode.Code, c.BotName, c.Channel,
            code =>
            {
                if (busy) return;
                busy = true;
                auth.StartCoroutine(VerifyCode(c, code, resend, ok => { busy = false; if (ok) { claimed = true; resolved = true; } }));
            },
            () => resolved = true,
            dismissLabel);
        if (first == null)
        {
            busy = true;
            yield return auth.Claim("claim.start", c.ProfileId, c.Channel, null, ApplyStart);
        }
        else
        {
            ApplyStart(first);
        }
        while (!resolved) yield return null;
        if (claimed) ProfileClaimSheet.Hide();
        finished(claimed);
    }

    private static IEnumerator VerifyCode(ProfileClaimPolicy.Candidate c, string code, System.Action resend, System.Action<bool> done)
    {
        AuthClient.ClaimResult verify = null;
        yield return AuthClient.Instance.Claim("claim.verify", c.ProfileId, c.Channel, code, r => verify = r);
        if (verify != null && verify.Success)
        {
            done(true);
            yield break;
        }
        string error = verify?.Error;
        if (ProfileClaimPolicy.IsCodeDead(error)) ProfileClaimSheet.ShowResend(ProfileClaimPolicy.ErrorCopyFor(error), resend);
        else ProfileClaimSheet.ShowError(ProfileClaimPolicy.ErrorCopyFor(error));
        done(false);
    }

    /// <summary>
    /// A channel just reported authorized. A claim-pending profile is NOT bound by that pairing (V2): claim.start
    /// sends a code into the paired account's own chat and only claim.verify binds it. <paramref name="done"/>(true)
    /// = the caller may go on (the profile is this install's, or needed no claim); (false) = the owner cancelled —
    /// the caller must not create a workflow for a profile this install does not own (Create's gate would refuse it).
    /// </summary>
    public static IEnumerator AfterChannelAuthorized(string profileId, ChatChannel channel, Transform botsRoot, System.Action<bool> done)
    {
        var auth = AuthClient.Existing;
        if (auth == null || !ProfileClaimPolicy.ShouldReportAuthorization(profileId, auth.Claims))
        {
            done?.Invoke(true);
            yield break;
        }
        bool claimed = false;
        yield return RunCodeClaim(CandidateFor(botsRoot, profileId, channel), FontFrom(botsRoot), ProfileClaimCopy.Cancel, null, ok => claimed = ok);
        if (!claimed) Debug.LogWarning($"[ProfileClaim] {profileId}: the code step after the pairing was cancelled — the profile stays unclaimed");
        done?.Invoke(claimed);
    }
}
