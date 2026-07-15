using NUnit.Framework;

/// <summary>
/// Covers <see cref="WappiStatusParser"/> — the pure reader that replaced the fragile
/// substring scan of a Wappi <c>get/status</c> body. Grounded in the 05-09 device UAT
/// (owner screenshot: bot "53" showed a raw JSON blob in the Telegram number field).
///
/// The key fixture is the PRETTY-PRINTED tapi shape with TWO "phone" keys — a nested
/// <c>account.phone</c> AND a top-level <c>phone</c> before <c>platform</c>. The old
/// code grabbed the wrong one (and spilled JSON into the field); the parser must read
/// the top-level phone regardless of key order or whitespace.
///
/// All phone digits here are SYNTHETIC and REDACTED — the real capture in
/// Tools/tapi/samples/status.json is never committed.
/// </summary>
public class WappiStatusParserTests
{
    // Distinct redacted digits so a test can prove top-level phone wins over account.phone.
    private const string TopLevelPhone = "70000000009";
    private const string AccountPhone = "70000000001";

    // Pretty-printed tapi get/status, mirroring the real dual-phone shape (redacted).
    private const string PrettyDualPhone = @"{
  ""account"": {
    ""user_id"": 1000000000,
    ""phone"": ""70000000001"",
    ""name"": ""X"",
    ""username"": ""x""
  },
  ""authorized"": true,
  ""authorized_at"": ""2026-07-14T12:26:33.5305+03:00"",
  ""phone"": ""70000000009"",
  ""platform"": ""tg"",
  ""profile_id"": ""test-0000""
}";

    // Same payload, compact (no whitespace), account key ordered before the top-level phone.
    private const string CompactDualPhone =
        "{\"account\":{\"phone\":\"70000000001\"},\"authorized\":true,\"phone\":\"70000000009\",\"platform\":\"tg\"}";

    // A representative stale blob: the old substring parser started at account.phone's value
    // and ran across the JSON, so the stored "number" was a raw JSON slice.
    private const string StaleBlob =
        "70000000009\",\"name\":\"X\",\"first_name\":\"X\",\"username\":\"x\"},\"app_status\":\"open\",\"authorized\":true";

    // ── TryGetAuthorized ─────────────────────────────────────────────────────

    [Test]
    public void TryGetAuthorized_PrettyDualPhone_TrueAndAuthorized()
    {
        Assert.IsTrue(WappiStatusParser.TryGetAuthorized(PrettyDualPhone, out bool authorized));
        Assert.IsTrue(authorized);
    }

    [Test]
    public void TryGetAuthorized_Compact_TrueAndAuthorized()
    {
        Assert.IsTrue(WappiStatusParser.TryGetAuthorized(CompactDualPhone, out bool authorized));
        Assert.IsTrue(authorized);
    }

    [Test]
    public void TryGetAuthorized_False_ParsesAsFalse()
    {
        Assert.IsTrue(WappiStatusParser.TryGetAuthorized(
            "{\"authorized\":false,\"phone\":\"70000000009\"}", out bool authorized));
        Assert.IsFalse(authorized);
    }

    [Test]
    public void TryGetAuthorized_StringBoolean_Parses()
    {
        Assert.IsTrue(WappiStatusParser.TryGetAuthorized("{\"authorized\":\"true\"}", out bool authorized));
        Assert.IsTrue(authorized);
    }

    [Test]
    public void TryGetAuthorized_Missing_ReturnsFalse()
    {
        Assert.IsFalse(WappiStatusParser.TryGetAuthorized("{\"phone\":\"70000000009\"}", out bool authorized));
        Assert.IsFalse(authorized);
    }

    [Test]
    public void TryGetAuthorized_Malformed_ReturnsFalse()
    {
        Assert.IsFalse(WappiStatusParser.TryGetAuthorized("{not valid json", out bool authorized));
        Assert.IsFalse(authorized);
    }

    [Test]
    public void TryGetAuthorized_EmptyAndNull_ReturnFalse()
    {
        Assert.IsFalse(WappiStatusParser.TryGetAuthorized("", out bool _));
        Assert.IsFalse(WappiStatusParser.TryGetAuthorized(null, out bool _));
    }

    // ── TryGetPhone ──────────────────────────────────────────────────────────

    [Test]
    public void TryGetPhone_PrettyDualPhone_PrefersTopLevel()
    {
        Assert.IsTrue(WappiStatusParser.TryGetPhone(PrettyDualPhone, out string phone));
        Assert.AreEqual(TopLevelPhone, phone);
    }

    [Test]
    public void TryGetPhone_Compact_PrefersTopLevel()
    {
        Assert.IsTrue(WappiStatusParser.TryGetPhone(CompactDualPhone, out string phone));
        Assert.AreEqual(TopLevelPhone, phone);
    }

    [Test]
    public void TryGetPhone_NoTopLevel_FallsBackToAccount()
    {
        Assert.IsTrue(WappiStatusParser.TryGetPhone(
            "{\"account\":{\"phone\":\"70000000001\"},\"authorized\":true}", out string phone));
        Assert.AreEqual(AccountPhone, phone);
    }

    [Test]
    public void TryGetPhone_StripsSingleLeadingPlus()
    {
        Assert.IsTrue(WappiStatusParser.TryGetPhone("{\"phone\":\"+70000000009\"}", out string phone));
        Assert.AreEqual(TopLevelPhone, phone);
    }

    [Test]
    public void TryGetPhone_LonePlus_ReturnsFalseAndEmpty()
    {
        // A lone "+" strips to "" — the contract returns false-when-no-value, never true+empty.
        Assert.IsFalse(WappiStatusParser.TryGetPhone("{\"phone\":\"+\"}", out string phone));
        Assert.AreEqual("", phone);
    }

    [Test]
    public void TryGetPhone_Missing_ReturnsFalseAndEmpty()
    {
        Assert.IsFalse(WappiStatusParser.TryGetPhone("{\"authorized\":true}", out string phone));
        Assert.AreEqual("", phone);
    }

    [Test]
    public void TryGetPhone_MalformedEmptyNull_ReturnFalse()
    {
        Assert.IsFalse(WappiStatusParser.TryGetPhone("{broken", out string _));
        Assert.IsFalse(WappiStatusParser.TryGetPhone("", out string _));
        Assert.IsFalse(WappiStatusParser.TryGetPhone(null, out string _));
    }

    // ── IsPlausiblePhone matrix ──────────────────────────────────────────────

    [Test]
    public void IsPlausiblePhone_RealDigits_True() =>
        Assert.IsTrue(WappiStatusParser.IsPlausiblePhone(TopLevelPhone));

    [Test]
    public void IsPlausiblePhone_LeadingPlus_True() =>
        Assert.IsTrue(WappiStatusParser.IsPlausiblePhone("+70000000009"));

    [Test]
    public void IsPlausiblePhone_StaleBlob_False() =>
        Assert.IsFalse(WappiStatusParser.IsPlausiblePhone(StaleBlob));

    [Test]
    public void IsPlausiblePhone_EmptyAndNull_False()
    {
        Assert.IsFalse(WappiStatusParser.IsPlausiblePhone(""));
        Assert.IsFalse(WappiStatusParser.IsPlausiblePhone(null));
    }

    [Test]
    public void IsPlausiblePhone_LonePlus_False() =>
        Assert.IsFalse(WappiStatusParser.IsPlausiblePhone("+"));

    [Test]
    public void IsPlausiblePhone_Letters_False() =>
        Assert.IsFalse(WappiStatusParser.IsPlausiblePhone("7abc0000000"));

    [Test]
    public void IsPlausiblePhone_TooLong_False() =>
        // 21 digits — beyond any real number, so a long numeric blob is still rejected.
        Assert.IsFalse(WappiStatusParser.IsPlausiblePhone("700000000090000000009"));

    [Test]
    public void IsPlausiblePhone_JsonPunctuation_False()
    {
        Assert.IsFalse(WappiStatusParser.IsPlausiblePhone("{\"phone\""));
        Assert.IsFalse(WappiStatusParser.IsPlausiblePhone("7,7"));
    }
}
