using Newtonsoft.Json.Linq;
using NUnit.Framework;

// EditMode coverage for DeleteBotFilesPayload — the pure composer for the
// /webhook/DeleteBotFiles body. The server retires bot_profiles rows from
// waProfileId/tgProfileId (slot release) and sweeps RAG chunks from botWaId/botTgId,
// so this seam is the single place the delete-time wire contract is authored;
// the exact-string test below is the composed-VALUE gate for it.
public class DeleteBotFilesPayloadTests
{
    private const string Sentinel = "-1"; // Bot.UnauthedProfileSentinel, pinned literally

    [Test]
    public void SentinelConstant_MatchesBot()
    {
        // The server-side guards exclude exactly '-1'/'' — if the client sentinel ever
        // changed, the retire would silently start matching nothing (or garbage).
        Assert.AreEqual(Sentinel, Bot.UnauthedProfileSentinel);
    }

    [Test]
    public void WhatsappOnlyBot_ComposesExactWireBody()
    {
        string json = DeleteBotFilesPayload.Compose(
            "wf_wa", "-1", "prof_wa", "-1", "user1");

        Assert.AreEqual(
            "{\"botWaId\":\"wf_wa\",\"botTgId\":\"-1\",\"waProfileId\":\"prof_wa\"," +
            "\"tgProfileId\":\"-1\",\"appUserId\":\"user1\"}",
            json);
    }

    [Test]
    public void FullyAuthedBot_CarriesAllIdsVerbatim()
    {
        var j = JObject.Parse(DeleteBotFilesPayload.Compose(
            "wf_wa", "wf_tg", "prof_wa", "prof_tg", "user1"));

        Assert.AreEqual("wf_wa", (string)j["botWaId"]);
        Assert.AreEqual("wf_tg", (string)j["botTgId"]);
        Assert.AreEqual("prof_wa", (string)j["waProfileId"]);
        Assert.AreEqual("prof_tg", (string)j["tgProfileId"]);
        Assert.AreEqual("user1", (string)j["appUserId"]);
    }

    [Test]
    public void NeverAuthedBot_ReturnsNull_ForEverySentinelSpelling()
    {
        // "-1", "", and null are all the same client-side "nothing" — none may
        // trigger a webhook call for a bot with zero server-side trace.
        Assert.IsNull(DeleteBotFilesPayload.Compose("-1", "-1", "-1", "-1", "user1"));
        Assert.IsNull(DeleteBotFilesPayload.Compose("", "", "", "", "user1"));
        Assert.IsNull(DeleteBotFilesPayload.Compose(null, null, null, null, "user1"));
        Assert.IsNull(DeleteBotFilesPayload.Compose("-1", "", null, "-1", "user1"));
    }

    [Test]
    public void ProfileOnlyBot_StillSends()
    {
        // Regression guard for the pre-2026-08-27 skip rule, which looked at the
        // WORKFLOW ids only: a channel whose CreateWorkflow response was lost holds a
        // real profile id (row registered server-side) with a sentinel workflow id —
        // the retire must still fire for it.
        var j = JObject.Parse(DeleteBotFilesPayload.Compose(
            "-1", "-1", "prof_wa", "-1", "user1"));

        Assert.AreEqual("-1", (string)j["botWaId"]);
        Assert.AreEqual("prof_wa", (string)j["waProfileId"]);
    }

    [Test]
    public void EmptyAndNullIds_NormalizeToSentinel()
    {
        // The server guards on the literal '-1'/'' pair; nulls must never reach the
        // wire (an n8n `|| '-1'` fallback sees them, but JSON null round-trips as the
        // STRING "null" through some layers — normalize on our side instead).
        var j = JObject.Parse(DeleteBotFilesPayload.Compose(
            "wf_wa", null, "", null, "user1"));

        Assert.AreEqual("-1", (string)j["botTgId"]);
        Assert.AreEqual("-1", (string)j["waProfileId"]);
        Assert.AreEqual("-1", (string)j["tgProfileId"]);
    }

    [Test]
    public void NullAppUserId_SerializesAsEmptyString()
    {
        // BillingIdentity can legitimately be empty pre-init; the field is audit-only
        // server-side, but it must stay a string — the create-side gate treats a
        // missing AppUserID as "old client", and this payload mirrors that posture.
        var j = JObject.Parse(DeleteBotFilesPayload.Compose(
            "wf_wa", "-1", "prof_wa", "-1", null));

        Assert.AreEqual("", (string)j["appUserId"]);
    }
}
