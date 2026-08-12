using NUnit.Framework;

// Wire shape of the read receipt (message/mark/read). Sits alongside ReadAckLedgerTests,
// which covers the LOCAL read bookkeeping; this file covers what actually goes on the wire.
//
// Pinned because the two channels differ in exactly one place — the mark_all query lever —
// and each side has a documented reason to stay as it is:
//   WhatsApp (api)  : mark_all=true, "помечает все непрочитанные сообщения в чате прочитанными".
//   Telegram (tapi) : no mark_all on this endpoint at all (its bulk lever lives on messages/get).
public class MarkReadRequestTests
{
    private const string Profile = "abc123";

    // --- WhatsApp ------------------------------------------------------------------------

    [Test]
    public void WhatsApp_CarriesMarkAll() =>
        Assert.AreEqual(
            "message/mark/read?profile_id=abc123&mark_all=true",
            MarkReadRequest.Path(ChatChannel.WhatsApp, Profile));

    [Test]
    public void WhatsApp_UsesMarkAll() =>
        Assert.IsTrue(MarkReadRequest.UsesMarkAll(ChatChannel.WhatsApp));

    // --- Telegram ------------------------------------------------------------------------

    [Test]
    public void Telegram_OmitsMarkAll() =>
        Assert.AreEqual(
            "message/mark/read?profile_id=abc123",
            MarkReadRequest.Path(ChatChannel.Telegram, Profile));

    [Test]
    public void Telegram_DoesNotUseMarkAll() =>
        Assert.IsFalse(MarkReadRequest.UsesMarkAll(ChatChannel.Telegram));

    // The Telegram branch is deliberately frozen: tapi documents no mark_all on mark/read,
    // so a group-read fix on the WhatsApp side must never leak across. Asserted on the
    // string itself rather than via UsesMarkAll so a future rewrite of the builder that
    // hardcodes the query still trips this.
    [Test]
    public void Telegram_NeverGainsMarkAll_EvenAsASubstring() =>
        Assert.IsFalse(MarkReadRequest.Path(ChatChannel.Telegram, Profile).Contains("mark_all"));

    // --- composed wire URLs (the value that actually reaches Wappi) -----------------------
    //
    // Asserting the COMPOSED url, not just the path fragment: the path alone cannot catch a
    // channel landing on the wrong base, and the base is the other half of the contract.

    [Test]
    public void ComposedUrl_WhatsApp_UsesApiBaseAndMarkAll() =>
        Assert.AreEqual(
            "https://wappi.pro/api/sync/message/mark/read?profile_id=abc123&mark_all=true",
            WappiEndpoints.Sync(ChatChannel.WhatsApp, MarkReadRequest.Path(ChatChannel.WhatsApp, Profile)));

    [Test]
    public void ComposedUrl_Telegram_UsesTapiBaseAndNoMarkAll() =>
        Assert.AreEqual(
            "https://wappi.pro/tapi/sync/message/mark/read?profile_id=abc123",
            WappiEndpoints.Sync(ChatChannel.Telegram, MarkReadRequest.Path(ChatChannel.Telegram, Profile)));

    // --- profile id plumbing -------------------------------------------------------------

    [Test]
    public void ProfileId_IsInterpolatedVerbatim() =>
        Assert.AreEqual(
            "message/mark/read?profile_id=p-9_ZZ&mark_all=true",
            MarkReadRequest.Path(ChatChannel.WhatsApp, "p-9_ZZ"));
}
