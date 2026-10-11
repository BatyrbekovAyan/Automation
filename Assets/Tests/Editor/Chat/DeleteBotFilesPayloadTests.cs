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

    // ---- F2 (T8): the retired set rides Delete Bot Files ---------------------------------------------------

    private static BotServerDeletion.ChannelServerIds Ch(string profile, string workflow, string retiredProfile = null, string retiredWorkflow = null) =>
        new BotServerDeletion.ChannelServerIds(profile, workflow, retiredProfile, retiredWorkflow);

    private static System.Collections.Generic.IReadOnlyList<string> All(BotServerDeletion.ChannelServerIds wa, BotServerDeletion.ChannelServerIds tg) =>
        DeleteBotFilesPayload.ComposeAll(new BotServerDeletion.BotServerIds(wa, tg), "user1");

    [Test]
    public void ABotWithOnlyRetiredIds_SendsThemInOneCall()
    {
        // The state after the move: the current ids are the sentinel, the former ones in the retired set. Without them the
        // server would keep this channel's memory, history, chunks and slot row (the V12 regression).
        var bodies = All(Ch("-1", "-1", "wa-retired", "wfRetiredWa001"), Ch("-1", "-1"));
        Assert.AreEqual(1, bodies.Count);
        Assert.AreEqual(
            "{\"botWaId\":\"wfRetiredWa001\",\"botTgId\":\"-1\",\"waProfileId\":\"wa-retired\"," +
            "\"tgProfileId\":\"-1\",\"appUserId\":\"user1\"}",
            bodies[0]);
    }

    [Test]
    public void OneChannelLiveAndTheOtherRetired_StillOneCall()
    {
        var bodies = All(Ch("wa-1", "wfCloneWa001"), Ch("-1", "-1", "tg-retired", "wfRetiredTg001"));
        Assert.AreEqual(1, bodies.Count, "each channel holds one set — it rides its own slot");
        var j = JObject.Parse(bodies[0]);
        Assert.AreEqual("wfCloneWa001", (string)j["botWaId"]);
        Assert.AreEqual("wa-1", (string)j["waProfileId"]);
        Assert.AreEqual("wfRetiredTg001", (string)j["botTgId"]);
        Assert.AreEqual("tg-retired", (string)j["tgProfileId"]);
    }

    [Test]
    public void AChannelHoldingBothSets_GetsASecondCall_ForItsRetiredIdsOnly()
    {
        // A reconnect whose Create did not adopt the retired profile: the channel has its new ids AND the retired set.
        var bodies = All(Ch("wa-2", "wfCloneWa002", "wa-1", "wfCloneWa001"), Ch("tg-1", "wfCloneTg001"));
        Assert.AreEqual(2, bodies.Count);
        var first = JObject.Parse(bodies[0]);
        Assert.AreEqual("wfCloneWa002", (string)first["botWaId"], "the first call carries the current ids");
        Assert.AreEqual("wa-2", (string)first["waProfileId"]);
        Assert.AreEqual("wfCloneTg001", (string)first["botTgId"]);
        var second = JObject.Parse(bodies[1]);
        Assert.AreEqual("wfCloneWa001", (string)second["botWaId"], "the second, the retired set of that channel");
        Assert.AreEqual("wa-1", (string)second["waProfileId"]);
        Assert.AreEqual("-1", (string)second["botTgId"], "the other channel was covered by the first call");
        Assert.AreEqual("-1", (string)second["tgProfileId"]);
    }

    [Test]
    public void NoRetiredSet_ExactlyTheOldSingleBody()
    {
        var bodies = All(Ch("prof_wa", "wf_wa"), Ch("-1", "-1"));
        CollectionAssert.AreEqual(new[] { DeleteBotFilesPayload.Compose("wf_wa", "-1", "prof_wa", "-1", "user1") }, bodies,
            "a bot without a retired set sends what it always sent");
        CollectionAssert.IsEmpty(All(Ch("-1", "-1"), Ch("", null)), "a never-authorised bot sends nothing");
        CollectionAssert.AreEqual(new[] { DeleteBotFilesPayload.Compose("wf_wa", "-1", "prof_wa", "-1", "user1") },
            All(Ch("prof_wa", "wf_wa", "prof_wa", ""), Ch("-1", "-1")),
            "an app-delete marker names the current profile — no second call");
    }
}
