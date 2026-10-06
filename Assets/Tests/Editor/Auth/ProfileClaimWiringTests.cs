using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Source guard for V2 (iteration 2026-09-28): a pairing never binds a claim-pending profile — Wappi does not say which
/// link session completed it. After a channel reports authorised, the client asks the server to send a code into the
/// bot's own chat (claim.start) and binds only with claim.verify; the auth flow may create a workflow only once that
/// succeeded. EditMode cannot run the sheet or the network, so the wiring is pinned in the source.
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
