using System;
using System.Text;
using NUnit.Framework;

public class InstallSessionTests
{
    private static string B64Url(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Token(string payloadJson) => "v1." + B64Url(payloadJson) + ".c2ln";

    [Test]
    public void TryParse_ReadsEveryClaim()
    {
        Assert.IsTrue(InstallSession.TryParse(Token("{\"i\":\"inst\",\"u\":\"rc-1\",\"p\":[\"wa\",\"tg\"],\"w\":[\"wf\"],\"c\":[\"old\"],\"e\":1800000000,\"n\":3}"), out var c));
        Assert.AreEqual("inst", c.InstallId);
        Assert.AreEqual("rc-1", c.AppUserId);
        CollectionAssert.AreEqual(new[] { "wa", "tg" }, c.Profiles);
        CollectionAssert.AreEqual(new[] { "wf" }, c.Workflows);
        CollectionAssert.AreEqual(new[] { "old" }, c.ClaimPending);
        Assert.AreEqual(1800000000L, c.Exp);
        Assert.AreEqual(3, c.Version);
    }

    [Test]
    public void TryParse_MissingBillingIdIsNull_AndDefaultsHold()
    {
        Assert.IsTrue(InstallSession.TryParse(Token("{\"i\":\"inst\",\"u\":\"\",\"p\":[],\"w\":[],\"e\":5}"), out var c));
        Assert.IsNull(c.AppUserId);
        Assert.IsEmpty(c.ClaimPending);
        Assert.AreEqual(1, c.Version);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("v1.abc")]
    [TestCase("v2.e30.c2ln")]
    [TestCase("v1..c2ln")]
    [TestCase("v1.!!!.c2ln")]
    public void TryParse_RejectsMalformedShapes(string token)
    {
        Assert.IsFalse(InstallSession.TryParse(token, out var c));
        Assert.IsNull(c);
    }

    [Test]
    public void TryParse_RejectsPayloadsWithoutTheRequiredClaims()
    {
        Assert.IsFalse(InstallSession.TryParse(Token("{\"u\":\"x\",\"p\":[],\"w\":[],\"e\":5}"), out _), "no install id");
        Assert.IsFalse(InstallSession.TryParse(Token("{\"i\":\"a\",\"p\":\"nope\",\"w\":[],\"e\":5}"), out _), "profiles not a list");
        Assert.IsFalse(InstallSession.TryParse(Token("{\"i\":\"a\",\"p\":[],\"w\":[],\"e\":\"soon\"}"), out _), "exp not a number");
    }

    [Test]
    public void FromBase64Url_HandlesEveryPaddingRemainder()
    {
        foreach (var s in new[] { "", "a", "ab", "abc", "abcd", "abcde" })
        {
            string enc = B64Url(s);
            Assert.AreEqual(s, Encoding.UTF8.GetString(InstallSession.FromBase64Url(enc)), $"round trip of '{s}'");
        }
    }

    // ---- refresh policy ----

    [Test]
    public void NeedsBootstrap_WhenEitherCredentialIsMissing()
    {
        Assert.IsTrue(SessionRefreshPolicy.NeedsBootstrap(null, null));
        Assert.IsTrue(SessionRefreshPolicy.NeedsBootstrap("id", ""));
        Assert.IsTrue(SessionRefreshPolicy.NeedsBootstrap("", "secret"));
        Assert.IsFalse(SessionRefreshPolicy.NeedsBootstrap("id", "secret"));
    }

    [Test]
    public void ShouldRefresh_InsideTheLeadWindow_AndWhenMissingOrExpired()
    {
        var claims = new InstallSession.TokenClaims { Exp = 10_000 };
        Assert.IsTrue(SessionRefreshPolicy.ShouldRefresh(0, null), "no token");
        Assert.IsTrue(SessionRefreshPolicy.ShouldRefresh(10_000, claims), "expired (exclusive)");
        Assert.IsTrue(SessionRefreshPolicy.ShouldRefresh(10_000 - SessionRefreshPolicy.LeadSeconds + 1, claims), "inside the lead");
        Assert.IsFalse(SessionRefreshPolicy.ShouldRefresh(10_000 - SessionRefreshPolicy.LeadSeconds, claims), "exactly at the lead boundary is fresh");
        Assert.IsFalse(SessionRefreshPolicy.ShouldRefresh(0, claims), "fresh");
    }

    [Test]
    public void IdentityChanged_OnlyWhenBothSidesKnown_AndDifferent()
    {
        var withU = new InstallSession.TokenClaims { AppUserId = "rc-1" };
        var withoutU = new InstallSession.TokenClaims { AppUserId = null };
        Assert.IsFalse(SessionRefreshPolicy.IdentityChanged(null, "rc-1"));
        Assert.IsFalse(SessionRefreshPolicy.IdentityChanged(withU, ""), "client has no id yet");
        Assert.IsFalse(SessionRefreshPolicy.IdentityChanged(withU, "rc-1"));
        Assert.IsTrue(SessionRefreshPolicy.IdentityChanged(withU, "rc-2"), "RevenueCat id rotated");
        Assert.IsTrue(SessionRefreshPolicy.IdentityChanged(withoutU, "rc-1"), "token minted before the billing id existed");
    }
}
