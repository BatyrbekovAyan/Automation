using System.Collections.Generic;
using NUnit.Framework;

public class ProfileClaimPolicyTests
{
    private static InstallSession.TokenClaims Claims(string[] p, string[] c = null) =>
        new InstallSession.TokenClaims { InstallId = "i", Profiles = p, Workflows = new string[0], ClaimPending = c ?? new string[0], Exp = 1 };

    private static ProfileClaimPolicy.Candidate C(string id, ChatChannel ch = ChatChannel.WhatsApp) =>
        new ProfileClaimPolicy.Candidate { ProfileId = id, Channel = ch, BotName = "Бот" };

    [Test]
    public void IsAuthedId_RejectsSentinelAndEmpty()
    {
        Assert.IsFalse(ProfileClaimPolicy.IsAuthedId(null));
        Assert.IsFalse(ProfileClaimPolicy.IsAuthedId(""));
        Assert.IsFalse(ProfileClaimPolicy.IsAuthedId(Bot.UnauthedProfileSentinel));
        Assert.IsTrue(ProfileClaimPolicy.IsAuthedId("b7a44f5d"));
    }

    [Test]
    public void NextUnowned_SkipsOwnedSnoozedAndUnauthed()
    {
        var connected = new[] { C("-1"), C("owned"), C("snoozed"), C("legacy") };
        var next = ProfileClaimPolicy.NextUnowned(connected, Claims(new[] { "owned" }), new HashSet<string> { "snoozed" });
        Assert.IsTrue(next.HasValue);
        Assert.AreEqual("legacy", next.Value.ProfileId);
    }

    [Test]
    public void NextUnowned_DoesNotSkipAClaimPendingProfile_APairingNeverBindsIt()
    {
        // V2 (2026-09-28): a pending claim used to be skipped because the re-pairing bound it on claim.status. A pairing
        // proves nothing now (Wappi does not say which link session completed it), so an authorised pending profile must
        // get the code step at the next launch — else it would stay unowned forever.
        var connected = new[] { C("owned"), C("pending", ChatChannel.Telegram) };
        var next = ProfileClaimPolicy.NextUnowned(connected, Claims(new[] { "owned" }, new[] { "pending" }), new HashSet<string>());
        Assert.IsTrue(next.HasValue);
        Assert.AreEqual("pending", next.Value.ProfileId);
    }

    [Test]
    public void AfterStart_OnlyACodeOrOwnershipMovesOn_EverythingElseOffersAResend()
    {
        Assert.AreEqual(ProfileClaimPolicy.StartStep.Proceed, ProfileClaimPolicy.AfterStart(true, "owned"));
        Assert.AreEqual(ProfileClaimPolicy.StartStep.EnterCode, ProfileClaimPolicy.AfterStart(true, "code"));
        Assert.AreEqual(ProfileClaimPolicy.StartStep.Resend, ProfileClaimPolicy.AfterStart(true, "reauth"), "the pairing is not visible to the server yet");
        Assert.AreEqual(ProfileClaimPolicy.StartStep.Resend, ProfileClaimPolicy.AfterStart(true, "pending"));
        Assert.AreEqual(ProfileClaimPolicy.StartStep.Resend, ProfileClaimPolicy.AfterStart(false, null), "code_send_failed / locked / no network");
        Assert.AreEqual(ProfileClaimPolicy.StartStep.Resend, ProfileClaimPolicy.AfterStart(false, "code"), "a failed call never asks for a code");
    }

    [Test]
    public void ErrorCopy_NamesEachServerRefusal_InRussian_NeverTheServerText()
    {
        Assert.AreEqual(ProfileClaimCopy.ErrorWrongCode, ProfileClaimPolicy.ErrorCopyFor("claim_code_rejected"));
        Assert.AreEqual(ProfileClaimCopy.ErrorLocked, ProfileClaimPolicy.ErrorCopyFor("claim_code_locked"));
        Assert.AreEqual(ProfileClaimCopy.ErrorSendFailed, ProfileClaimPolicy.ErrorCopyFor("code_send_failed"));
        Assert.AreEqual(ProfileClaimCopy.ErrorGeneric, ProfileClaimPolicy.ErrorCopyFor("transport_unavailable"));
        Assert.AreEqual(ProfileClaimCopy.ErrorGeneric, ProfileClaimPolicy.ErrorCopyFor(null));
        Assert.IsTrue(ProfileClaimPolicy.IsCodeDead("claim_code_locked"));
        Assert.IsFalse(ProfileClaimPolicy.IsCodeDead("claim_code_rejected"), "a wrong answer with attempts left keeps the field");
    }

    [Test]
    public void NextUnowned_NullWithoutASession_AndWhenEverythingIsOwned()
    {
        Assert.IsNull(ProfileClaimPolicy.NextUnowned(new[] { C("a") }, null, null));
        Assert.IsNull(ProfileClaimPolicy.NextUnowned(new[] { C("a"), C("b") }, Claims(new[] { "a", "b" }), null));
    }

    [Test]
    public void ShouldReportAuthorization_OnlyForPendingClaims()
    {
        Assert.IsTrue(ProfileClaimPolicy.ShouldReportAuthorization("p", Claims(new string[0], new[] { "p" })));
        Assert.IsFalse(ProfileClaimPolicy.ShouldReportAuthorization("p", Claims(new[] { "p" })), "already owned");
        Assert.IsFalse(ProfileClaimPolicy.ShouldReportAuthorization("-1", Claims(new string[0], new[] { "-1" })));
        Assert.IsFalse(ProfileClaimPolicy.ShouldReportAuthorization("p", null));
    }

    [Test]
    public void ModeFor_MapsServerMethods()
    {
        Assert.AreEqual(ProfileClaimPolicy.SheetMode.Code, ProfileClaimPolicy.ModeFor("code"));
        Assert.AreEqual(ProfileClaimPolicy.SheetMode.Owned, ProfileClaimPolicy.ModeFor("owned"));
        Assert.AreEqual(ProfileClaimPolicy.SheetMode.Reauth, ProfileClaimPolicy.ModeFor("reauth"));
        Assert.AreEqual(ProfileClaimPolicy.SheetMode.Reauth, ProfileClaimPolicy.ModeFor(null), "unknown → the safe, non-destructive shape");
    }

    [Test]
    public void Copy_FormatsBotNameAndChannel()
    {
        StringAssert.Contains("«Мой бот»", ProfileClaimCopy.BodyCode("Мой бот", ChatChannel.Telegram));
        StringAssert.Contains("Telegram", ProfileClaimCopy.BodyCode("Мой бот", ChatChannel.Telegram));
        StringAssert.Contains("WhatsApp", ProfileClaimCopy.BodyReauth("X", ChatChannel.WhatsApp));
        StringAssert.DoesNotContain("автоматически", ProfileClaimCopy.BodyReauthFormat, "V2: a re-pairing no longer binds by itself");
        StringAssert.Contains("код", ProfileClaimCopy.BodyReauthFormat);
    }
}
