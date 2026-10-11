using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Source guard for V2 (iteration 2026-09-28): a pairing never binds a claim-pending profile — Wappi does not say which
/// link session completed it. After a channel reports authorised, the client asks the server to send a code into the
/// bot's own chat (claim.start) and binds only with claim.verify; the auth flow may create a workflow only once that
/// succeeded. EditMode cannot run the sheet or the network, so the wiring is pinned in the source.
///
/// M4 (follow-ups 2026-10-09): the boot loop asked a RETIRED profile forever — the server answered «owned» for the
/// install's own retired row, the token (live rows only) never listed it, and «owned» meant «done, next». The loop now
/// asks each profile at most once per run, and a 410 profile_retired marks the bot's channel and moves on.
/// </summary>
public class ProfileClaimWiringTests
{
    private static string Src(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, rel));

    private static string MethodBody(string text, string signature)
    {
        int i = text.IndexOf(signature, System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(i, 0, $"{signature} not found");
        int open = text.IndexOf('{', i);
        int depth = 0;
        for (int k = open; k < text.Length; k++)
        {
            if (text[k] == '{') depth++;
            else if (text[k] == '}' && --depth == 0) return text.Substring(open, k - open + 1);
        }
        Assert.Fail($"{signature}: unbalanced braces");
        return null;
    }

    [Test]
    public void AfterChannelAuthorized_AsksForTheCode_NeverPollsClaimStatus()
    {
        string flow = Src("Scripts/Auth/ProfileClaimFlow.cs");
        string after = MethodBody(flow, "public static IEnumerator AfterChannelAuthorized(");
        Assert.IsTrue(after.Contains("RunCodeClaim("), "the pairing must lead into the code step");
        Assert.IsTrue(after.Contains("ProfileClaimCopy.Cancel"), "right after a pairing the sheet cancels the connection, it does not snooze it");
        Assert.IsFalse(after.Contains("claim.status"), "claim.status never binds (V2) — a pairing alone must not be reported as a claim");
        Assert.IsFalse(flow.Contains("\"claim.status\""), "no client path may rely on claim.status any more");
        string run = MethodBody(flow, "private static IEnumerator RunCodeClaim(");
        Assert.IsTrue(run.Contains("\"claim.start\""), "the code is requested with claim.start");
        Assert.IsTrue(run.Contains("SheetMode.Code"), "the owner enters the code from the bot's own chat");
        Assert.IsTrue(MethodBody(flow, "private static IEnumerator VerifyCode(").Contains("\"claim.verify\""), "only claim.verify binds");
    }

    [Test]
    public void RunAtBoot_NeverAsksTheSameProfileTwiceInOneRun()
    {
        string flow = Src("Scripts/Auth/ProfileClaimFlow.cs");
        string run = MethodBody(flow, "public static IEnumerator RunAtBoot(");
        int create = run.IndexOf("new ProfileClaimRun(", System.StringComparison.Ordinal);
        int loop = run.IndexOf("while (true)", System.StringComparison.Ordinal);
        int take = run.IndexOf(".TakeNext(", System.StringComparison.Ordinal);
        int claim = run.IndexOf("ClaimOne(", System.StringComparison.Ordinal);
        Assert.IsTrue(create >= 0 && loop > create, "M4: ONE run per boot run, created before the loop (a per-turn one would forget what it asked)");
        Assert.IsTrue(take > loop && claim > take, "M4: the next profile comes from the run — recorded as asked BEFORE ClaimOne");
        Assert.IsFalse(run.Contains("ProfileClaimPolicy.NextUnowned("), "M4: the loop must not pick a profile past the run's asked set");
        // F2 (T8): a marker the M4 build wrote (ids kept) is moved aside once the session is known — before the loop reads
        // the connected profiles, so a moved channel is never asked again and its reconnect takes the fresh path.
        int session = run.IndexOf("yield return auth.EnsureSession()", System.StringComparison.Ordinal);
        int legacy = run.IndexOf("ProfileRetirement.TransferLegacyMarkers(botsRoot, auth.Claims)", System.StringComparison.Ordinal);
        Assert.IsTrue(session >= 0 && legacy > session && loop > legacy,
            "F2: the M4 markers are moved aside after the session (the token still decides whether a marker applies) and before the loop");
        string takeNext = MethodBody(Src("Scripts/Auth/ProfileClaimPolicy.cs"), "TakeNext(");
        int pick = takeNext.IndexOf("NextUnowned(", System.StringComparison.Ordinal);
        int add = takeNext.IndexOf(".Add(", System.StringComparison.Ordinal);
        Assert.IsTrue(pick >= 0 && add > pick, "M4: TakeNext skips the asked profiles and records the one it hands out");
    }

    [Test]
    public void ConnectedProfiles_CarryTheBotKeyAndItsRetiredMarker()
    {
        string connected = MethodBody(Src("Scripts/Auth/ProfileClaimFlow.cs"), "public static IEnumerable<ProfileClaimPolicy.Candidate> ConnectedProfiles(");
        Assert.IsTrue(connected.Contains("BotKey = bot.name"), "M4: the marker lives under the bot GameObject's name (the PlayerPrefs prefix)");
        Assert.IsTrue(connected.Contains("ProfileRetirement.MarkerId(bot.name, ChatChannel.WhatsApp)")
                      && connected.Contains("ProfileRetirement.MarkerId(bot.name, ChatChannel.Telegram)"),
            "M4: a channel marked retired is skipped at the next launch — both channels read their own marker");
    }

    [Test]
    public void ClaimOne_OnProfileRetired_MarksTheChannelAndMovesOn()
    {
        string flow = Src("Scripts/Auth/ProfileClaimFlow.cs");
        string one = MethodBody(flow, "private static IEnumerator ClaimOne(");
        int retired = one.IndexOf("ProfileClaimPolicy.IsRetired(start", System.StringComparison.Ordinal);
        // F2 (T8): the mark now moves the channel's ids aside, so it needs the bot itself (its fields and its PlayerPrefs
        // are written in one place) — resolved from the bot key the candidate carries.
        int mark = retired < 0 ? -1 : one.IndexOf("ProfileRetirement.Mark(BotFor(botsRoot, c.BotKey), c.Channel, c.ProfileId, start.RetiredReason)", retired, System.StringComparison.Ordinal);
        int moveOn = mark < 0 ? -1 : one.IndexOf("finished(true)", mark, System.StringComparison.Ordinal);
        int stop = one.IndexOf("if (!usable)", System.StringComparison.Ordinal);
        Assert.IsTrue(retired >= 0 && mark > retired && moveOn > mark,
            "M4: profile_retired → mark the bot's channel (with the server's reason) → finished(true): the loop goes on to the next bot");
        Assert.IsTrue(stop > moveOn, "M4: the retired branch must precede the generic «not usable → stop» (that stop starved every later bot)");
        int owned = one.IndexOf("SheetMode.Owned", System.StringComparison.Ordinal);
        Assert.IsTrue(owned >= 0 && one.IndexOf("auth.Owns(c.ProfileId)", owned, System.StringComparison.Ordinal) > owned,
            "M4: «owned» without the profile in the token is a server anomaly — logged, never re-asked (the run's asked set)");
        // The claim flow itself never writes a bot's ids: the one place that moves them aside is ProfileRetirement.
        foreach (string write in new[] { "whatsappProfileId =", "telegramProfileId =", "whatsappWorkflowId =", "telegramWorkflowId =",
                                         "\"WhatsappProfileId\"", "\"TelegramProfileId\"", "\"WhatsappWorkflowId\"", "\"TelegramWorkflowId\"" })
            Assert.IsFalse(flow.Contains(write), $"the claim flow must not write the bot's ids itself ({write}) — ProfileRetirement.Mark does");
    }

    [Test]
    public void Retirement_MovesTheIdsAside_InOnePlace_WithOneSave()
    {
        // F2 (T8) replaces M4's «ids stay»: a retired channel's ids go to {bot}{Ch}Retired*, the current ones become -1 and
        // the channel is off — every reader of the ~110 sites then sees «not connected» by itself. The Bot's fields and its
        // PlayerPrefs are written in ONE method with ONE Save (plan T8 risk 2), and the decision is the pure policy's.
        string retirement = Src("Scripts/Auth/ProfileRetirement.cs");
        StringAssert.Contains("RetiredChannelPolicy.Retire(", MethodBody(retirement, "public static void Mark("),
            "the transfer decision (stale answer, app_delete, idempotence) belongs to the pure policy");
        string store = MethodBody(retirement, "private static void Store(");
        foreach (string write in new[] { "bot.whatsappProfileId =", "bot.telegramProfileId =", "bot.whatsappWorkflowId =", "bot.telegramWorkflowId =" })
        {
            StringAssert.Contains(write, store, $"Store writes the Bot field ({write})");
            Assert.AreEqual(1, Regex.Matches(retirement, Regex.Escape(write)).Count, $"{write}: only Store may write it");
        }
        StringAssert.Contains("PlayerPrefs.Save()", store, "one Save after the batch");
    }

    [Test]
    public void AuthClient_Claim_ReadsRetiredReason()
    {
        string client = Src("Scripts/Auth/AuthClient.cs");
        Assert.IsTrue(client.Contains("public string RetiredReason;"), "M4: ClaimResult carries the server's retiredReason");
        string claim = MethodBody(client, "public IEnumerator Claim(");
        Assert.IsTrue(claim.Contains("RetiredReason = jo.Value<string>(\"retiredReason\")"), "M4: the reason is read from the 410 body");
    }

    [Test]
    public void AuthPollers_CreateNothing_UntilTheClaimSucceeded()
    {
        string m = Src("Scripts/Main/Manager.cs");
        foreach (var (poller, back, completed) in new[]
                 {
                     ("private IEnumerator GetWhatsappProfileStatus(", "WhatsappAuthBackButton.onClick.Invoke()", "whatsappAuthCompleted = true"),
                     ("private IEnumerator GetTelegramProfileStatus(", "TelegramAuthBackButton.onClick.Invoke()", "telegramAuthCompleted = true"),
                 })
        {
            string body = MethodBody(m, poller);
            int claim = body.IndexOf("ProfileClaimFlow.AfterChannelAuthorized(", System.StringComparison.Ordinal);
            int gate = body.IndexOf("if (!claimOk)", System.StringComparison.Ordinal);
            int cancel = body.IndexOf(back, System.StringComparison.Ordinal);
            int success = body.IndexOf("ShowAuthSuccess(", System.StringComparison.Ordinal);
            int done = body.IndexOf(completed, System.StringComparison.Ordinal);
            Assert.IsTrue(claim >= 0 && gate > claim && cancel > gate, $"{poller}: a cancelled claim must undo the connection");
            Assert.IsTrue(success > cancel && done > success, $"{poller}: auth success (→ Create) only after the claim gate");
        }
    }
}
