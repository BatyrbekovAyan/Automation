using NUnit.Framework;

public class TelegramAuthResponseParserTests
{
    // --- ExtractDetail -----------------------------------------------------

    [Test]
    public void ExtractDetail_TwoFactor_ReturnsDetailValue()
    {
        Assert.AreEqual("2fa",
            TelegramAuthResponseParser.ExtractDetail("{\"detail\":\"2fa\",\"uuid\":\"x\"}"));
    }

    [Test]
    public void ExtractDetail_AuthSuccess_ReturnsDetailValue()
    {
        Assert.AreEqual("auth_success",
            TelegramAuthResponseParser.ExtractDetail("{\"status\":\"done\",\"detail\":\"auth_success\",\"uuid\":\"x\"}"));
    }

    [Test]
    public void ExtractDetail_ToleratesWhitespaceAfterColon()
    {
        Assert.AreEqual("2fa",
            TelegramAuthResponseParser.ExtractDetail("{\"detail\": \"2fa\"}"));
    }

    [Test]
    public void ExtractDetail_NoDetailKey_ReturnsEmpty()
    {
        Assert.AreEqual("",
            TelegramAuthResponseParser.ExtractDetail("{\"status\":\"done\",\"uuid\":\"x\"}"));
    }

    [Test]
    public void ExtractDetail_MalformedBody_ReturnsEmptyAndDoesNotThrow()
    {
        Assert.DoesNotThrow(() => TelegramAuthResponseParser.ExtractDetail("{\"detail\":"));
        Assert.AreEqual("", TelegramAuthResponseParser.ExtractDetail("{\"detail\":"));
        Assert.AreEqual("", TelegramAuthResponseParser.ExtractDetail("not json at all"));
    }

    [Test]
    public void ExtractDetail_NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual("", TelegramAuthResponseParser.ExtractDetail(null));
        Assert.AreEqual("", TelegramAuthResponseParser.ExtractDetail(""));
    }

    [Test]
    public void ExtractDetail_NonStringDetailValue_ReturnsEmpty()
    {
        // A malformed non-string detail value must not throw or capture garbage.
        Assert.AreEqual("", TelegramAuthResponseParser.ExtractDetail("{\"detail\":2}"));
    }

    // --- IsTwoFactor -------------------------------------------------------

    [Test]
    public void IsTwoFactor_TrueOnlyForExact2fa()
    {
        Assert.IsTrue(TelegramAuthResponseParser.IsTwoFactor("2fa"));
    }

    [Test]
    public void IsTwoFactor_FalseForAuthSuccessEmptyAndUnknown()
    {
        Assert.IsFalse(TelegramAuthResponseParser.IsTwoFactor("auth_success"));
        Assert.IsFalse(TelegramAuthResponseParser.IsTwoFactor(""));
        Assert.IsFalse(TelegramAuthResponseParser.IsTwoFactor(null));
        Assert.IsFalse(TelegramAuthResponseParser.IsTwoFactor("2fa_needed"));
        Assert.IsFalse(TelegramAuthResponseParser.IsTwoFactor("PASSWORD_HASH_INVALID"));
    }

    // --- IsAuthSuccess -----------------------------------------------------

    [Test]
    public void IsAuthSuccess_TrueWhenDetailStartsWithAuthSuccess()
    {
        Assert.IsTrue(TelegramAuthResponseParser.IsAuthSuccess("auth_success"));
        Assert.IsTrue(TelegramAuthResponseParser.IsAuthSuccess("auth_success for chatID=1"));
    }

    [Test]
    public void IsAuthSuccess_FalseFor2faEmptyAndUnknown()
    {
        Assert.IsFalse(TelegramAuthResponseParser.IsAuthSuccess("2fa"));
        Assert.IsFalse(TelegramAuthResponseParser.IsAuthSuccess(""));
        Assert.IsFalse(TelegramAuthResponseParser.IsAuthSuccess(null));
        Assert.IsFalse(TelegramAuthResponseParser.IsAuthSuccess("failed_auth_success")); // must START with it
    }

    // --- Combined error/re-prompt semantics --------------------------------

    [Test]
    public void UnknownOrWrongPasswordDetail_IsNeitherTwoFactorNorSuccess()
    {
        // A wrong-password / error detail -> both classifiers false -> caller re-prompts.
        foreach (var detail in new[] { "PASSWORD_HASH_INVALID", "error", "", "waiting for code" })
        {
            Assert.IsFalse(TelegramAuthResponseParser.IsTwoFactor(detail), $"IsTwoFactor('{detail}')");
            Assert.IsFalse(TelegramAuthResponseParser.IsAuthSuccess(detail), $"IsAuthSuccess('{detail}')");
        }
    }
}
