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
    // ── TryGetProfileId — profile/add (both channels) ─────────────────────────
    // Regression cover for the "+14 up to \",\"status\":" scan: negative length (throw,
    // hanging the awaiting parent coroutine) on reversed key order, and a leading-quote
    // corrupted id from a pretty-printed body.

    [Test]
    public void TryGetProfileId_CompactBody_ReadsId()
    {
        Assert.IsTrue(WappiStatusParser.TryGetProfileId("{\"profile_id\":\"abc123\",\"status\":\"done\"}", out string id));
        Assert.AreEqual("abc123", id);
    }

    [Test]
    public void TryGetProfileId_StatusBeforeProfileId_ReadsId_DoesNotThrow()
    {
        // Reversed order made the old scan compute a NEGATIVE length and throw.
        Assert.IsTrue(WappiStatusParser.TryGetProfileId("{\"status\":\"done\",\"profile_id\":\"abc123\"}", out string id));
        Assert.AreEqual("abc123", id);
    }

    [Test]
    public void TryGetProfileId_PrettyPrinted_HasNoLeadingQuote()
    {
        Assert.IsTrue(WappiStatusParser.TryGetProfileId("{\n  \"profile_id\": \"abc123\",\n  \"status\": \"done\"\n}", out string id));
        Assert.AreEqual("abc123", id, "The old +14 offset stored a leading quote from a pretty body.");
    }

    [Test]
    public void TryGetProfileId_MissingBlankOrMalformed_ReturnsFalse()
    {
        Assert.IsFalse(WappiStatusParser.TryGetProfileId("{\"status\":\"done\"}", out string missing));
        Assert.AreEqual("", missing);
        Assert.IsFalse(WappiStatusParser.TryGetProfileId("{\"profile_id\":\"\"}", out _));
        Assert.IsFalse(WappiStatusParser.TryGetProfileId("not json", out _));
        Assert.IsFalse(WappiStatusParser.TryGetProfileId(null, out _));
    }

    // ── TryGetQrPng — WA qr/get ("qrCode", data URI) + tapi auth/qr ("detail", raw) ──
    // Regression cover for the unguarded Convert.FromBase64String on the SUCCESS path
    // (FormatException killed the QR coroutine, leaving the spinner up forever).

    // "hi" base64-encoded — a valid, non-empty payload; LoadImage rejecting it is the
    // caller's already-checked concern, decoding without throwing is this method's.
    private const string ValidBase64 = "aGk=";

    [Test]
    public void TryGetQrPng_WhatsAppDataUri_StripsPrefixAndDecodes()
    {
        Assert.IsTrue(WappiStatusParser.TryGetQrPng(
            "{\"status\":\"done\",\"qrCode\":\"data:image/png;base64," + ValidBase64 + "\",\"uuid\":\"x\"}",
            "qrCode", out byte[] png));
        Assert.AreEqual(new byte[] { 0x68, 0x69 }, png);
    }

    [Test]
    public void TryGetQrPng_TelegramRawBase64UnderDetail_Decodes()
    {
        Assert.IsTrue(WappiStatusParser.TryGetQrPng(
            "{\"detail\":\"" + ValidBase64 + "\",\"uuid\":\"x\"}", "detail", out byte[] png));
        Assert.AreEqual(new byte[] { 0x68, 0x69 }, png);
    }

    [Test]
    public void TryGetQrPng_NonBase64Detail_ReturnsFalse_DoesNotThrow()
    {
        // The exact payloads that used to throw FormatException on the success path.
        Assert.IsFalse(WappiStatusParser.TryGetQrPng("{\"detail\":\"2fa\"}", "detail", out byte[] a));
        Assert.IsNull(a);
        Assert.IsFalse(WappiStatusParser.TryGetQrPng("{\"detail\":\"auth_success\"}", "detail", out _));
        Assert.IsFalse(WappiStatusParser.TryGetQrPng("{\"detail\":\"not base64 !!\"}", "detail", out _));
    }

    [Test]
    public void TryGetQrPng_KeyOrderIrrelevant()
        => Assert.IsTrue(WappiStatusParser.TryGetQrPng(
            "{\"uuid\":\"x\",\"status\":\"done\",\"qrCode\":\"data:image/png;base64," + ValidBase64 + "\"}",
            "qrCode", out _), "Old slice was bounded by a token that had to come AFTER the payload.");

    [Test]
    public void TryGetQrPng_MissingKeyBlankOrMalformed_ReturnsFalse()
    {
        Assert.IsFalse(WappiStatusParser.TryGetQrPng("{\"status\":\"done\"}", "qrCode", out _));
        Assert.IsFalse(WappiStatusParser.TryGetQrPng("{\"qrCode\":\"\"}", "qrCode", out _));
        Assert.IsFalse(WappiStatusParser.TryGetQrPng("{\"qrCode\":\"data:image/png;base64,\"}", "qrCode", out _),
            "A data URI with an empty payload is not a QR.");
        Assert.IsFalse(WappiStatusParser.TryGetQrPng("not json", "qrCode", out _));
        Assert.IsFalse(WappiStatusParser.TryGetQrPng(null, "qrCode", out _));
        Assert.IsFalse(WappiStatusParser.TryGetQrPng("{\"qrCode\":\"" + ValidBase64 + "\"}", null, out _),
            "A null key is a caller bug, not a crash.");
    }

    // ── TryGetCode — WhatsApp pairing code (auth/code) ────────────────────────
    // Regression cover for the hard-coded Substring(startIndex, 9) that threw
    // ArgumentOutOfRangeException on any code shorter than "XXXX-XXXX", stranding the
    // LoadingPanel with the request button disabled.

    [Test]
    public void TryGetCode_CanonicalNineCharCode_ReadsVerbatim()
    {
        Assert.IsTrue(WappiStatusParser.TryGetCode("{\"status\":\"done\",\"code\":\"ABCD-EFGH\"}", out string code));
        Assert.AreEqual("ABCD-EFGH", code);
    }

    [Test]
    public void TryGetCode_ShorterThanNine_ReadsExactValue_DoesNotThrow()
    {
        // The old fixed-length read threw here (fewer than 9 chars remained after the token).
        Assert.IsTrue(WappiStatusParser.TryGetCode("{\"code\":\"12345\"}", out string code));
        Assert.AreEqual("12345", code, "Short code must read exactly, not drag in trailing JSON.");
    }

    [Test]
    public void TryGetCode_LongerThanNine_IsNotTruncated()
    {
        Assert.IsTrue(WappiStatusParser.TryGetCode("{\"code\":\"ABCDE-FGHIJ-KL\"}", out string code));
        Assert.AreEqual("ABCDE-FGHIJ-KL", code);
    }

    [Test]
    public void TryGetCode_PrettyPrintedBody_StillReads()
    {
        Assert.IsTrue(WappiStatusParser.TryGetCode("{\n  \"status\": \"done\",\n  \"code\": \"WXYZ-1234\"\n}", out string code));
        Assert.AreEqual("WXYZ-1234", code);
    }

    [Test]
    public void TryGetCode_MissingBlankOrMalformed_ReturnsFalse()
    {
        Assert.IsFalse(WappiStatusParser.TryGetCode("{\"status\":\"done\"}", out string missing));
        Assert.AreEqual("", missing);
        Assert.IsFalse(WappiStatusParser.TryGetCode("{\"code\":\"\"}", out _), "Blank code is not a code.");
        Assert.IsFalse(WappiStatusParser.TryGetCode("{\"code\":\"   \"}", out _), "Whitespace-only is not a code.");
        Assert.IsFalse(WappiStatusParser.TryGetCode("not json at all", out _));
        Assert.IsFalse(WappiStatusParser.TryGetCode("", out _));
        Assert.IsFalse(WappiStatusParser.TryGetCode(null, out _));
    }

    [Test]
    public void TryGetCode_NonScalarCode_ReturnsFalse()
        => Assert.IsFalse(WappiStatusParser.TryGetCode("{\"code\":{\"value\":\"ABCD-EFGH\"}}", out _),
            "An object/array 'code' is treated as absent rather than stringified into the label.");

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
