using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

// EditMode coverage for N8nSuggestionsProvider.BuildPayloadJson (+ MediaText) — the PURE
// payload-assembly seam. No asmdef (compiles into Assembly-CSharp-Editor); no Unity objects,
// no network. Asserts on the serialized JSON via JObject, mirroring the frozen wire contract v1.
public class SuggestRepliesPayloadTests
{
    private static SuggestionRequest Req(long seq = 1, string steer = null,
        string lastIncoming = null, string chatId = "c1@c.us")
        => new SuggestionRequest
        {
            chatId = chatId, requestSeq = seq,
            steerTowardText = steer, lastIncomingText = lastIncoming
        };

    private static MessageViewModel Msg(string text, bool incoming,
        MessageType type = MessageType.Chat, long ts = 1000)
        => new MessageViewModel { text = text, isIncoming = incoming, type = type, timestamp = ts };

    private static List<MessageViewModel> One() => new List<MessageViewModel> { Msg("привет", true) };

    private static JObject Build(SuggestionRequest req, List<MessageViewModel> msgs,
        string profileId = "p1", string botWaId = "wf1", string businessTypeId = "auto_parts",
        string businessName = "Магазин", string ownerPrompt = "", string catalog = "")
        => JObject.Parse(N8nSuggestionsProvider.BuildPayloadJson(
            req, profileId, botWaId, businessTypeId, businessName, ownerPrompt, catalog, msgs));

    // --- version + request passthrough ---------------------------------------

    [Test]
    public void Version_IsOne_And_RequestSeqPassthrough()
    {
        var j = Build(Req(seq: 99), One());
        Assert.AreEqual(1, (int)j["v"]);
        Assert.AreEqual(99L, (long)j["requestSeq"]);
    }

    [Test]
    public void RequestScalarFields_PassThrough()
    {
        var j = Build(Req(seq: 3, chatId: "777@c.us"), One(), profileId: "pid7", businessName: "Цветы");
        Assert.AreEqual("777@c.us", (string)j["chatId"]);
        Assert.AreEqual("pid7", (string)j["profileId"]);
        Assert.AreEqual("Цветы", (string)j["businessName"]);
        Assert.AreEqual("auto_parts", (string)j["businessTypeId"]);
    }

    // --- role mapping + ordering ---------------------------------------------

    [Test]
    public void RoleMapping_IncomingIsClient_OutgoingIsBusiness()
    {
        var msgs = new List<MessageViewModel> { Msg("in", true), Msg("out", false) };
        var arr = (JArray)Build(Req(), msgs)["messages"];
        Assert.AreEqual("client", (string)arr[0]["role"]);
        Assert.AreEqual("business", (string)arr[1]["role"]);
    }

    [Test]
    public void Ordering_OldestToNewest_Preserved()
    {
        var msgs = new List<MessageViewModel>
        {
            Msg("oldest", true), Msg("middle", false), Msg("newest", true)
        };
        var arr = (JArray)Build(Req(), msgs)["messages"];
        Assert.AreEqual("oldest", (string)arr[0]["text"]);
        Assert.AreEqual("newest", (string)arr[2]["text"]);
    }

    [Test]
    public void Timestamp_CarriedThrough()
    {
        var arr = (JArray)Build(Req(), new List<MessageViewModel> { Msg("x", true, ts: 1712345678) })["messages"];
        Assert.AreEqual(1712345678L, (long)arr[0]["ts"]);
    }

    // --- <=12 cap keeps the NEWEST twelve, still oldest->newest --------------

    [Test]
    public void Cap12_KeepsNewestTwelve_OldestToNewest()
    {
        var msgs = new List<MessageViewModel>();
        for (int i = 0; i < 15; i++) msgs.Add(Msg("m" + i, true));
        var arr = (JArray)Build(Req(), msgs)["messages"];
        Assert.AreEqual(12, arr.Count);
        Assert.AreEqual("m3", (string)arr[0]["text"]);    // m0,m1,m2 dropped
        Assert.AreEqual("m14", (string)arr[11]["text"]);  // newest kept, last
    }

    // --- media placeholders ---------------------------------------------------

    [Test]
    public void MediaPlaceholder_ImageBecomesPhoto()
    {
        var arr = (JArray)Build(Req(), new List<MessageViewModel> { Msg("", true, MessageType.Image) })["messages"];
        Assert.AreEqual("[фото]", (string)arr[0]["text"]);
    }

    [Test]
    public void MediaPlaceholder_VoiceWithCaption_AppendsCaption()
    {
        var arr = (JArray)Build(Req(), new List<MessageViewModel> { Msg("послушайте", true, MessageType.Voice) })["messages"];
        Assert.AreEqual("[голосовое сообщение] послушайте", (string)arr[0]["text"]);
    }

    [Test]
    public void MediaText_EveryBranch()
    {
        Assert.AreEqual("текст", N8nSuggestionsProvider.MediaText(MessageType.Chat, "текст"));
        Assert.AreEqual("[фото]", N8nSuggestionsProvider.MediaText(MessageType.Image, ""));
        Assert.AreEqual("[видео]", N8nSuggestionsProvider.MediaText(MessageType.Video, null));
        Assert.AreEqual("[голосовое сообщение]", N8nSuggestionsProvider.MediaText(MessageType.Voice, ""));
        Assert.AreEqual("[голосовое сообщение]", N8nSuggestionsProvider.MediaText(MessageType.Audio, ""));
        Assert.AreEqual("[документ]", N8nSuggestionsProvider.MediaText(MessageType.Document, ""));
        Assert.AreEqual("[стикер]", N8nSuggestionsProvider.MediaText(MessageType.Sticker, ""));
        Assert.AreEqual("[сообщение]", N8nSuggestionsProvider.MediaText(MessageType.Unknown, ""));
        Assert.AreEqual("[сообщение]", N8nSuggestionsProvider.MediaText(MessageType.Reaction, ""));
        Assert.AreEqual("[фото] подпись", N8nSuggestionsProvider.MediaText(MessageType.Image, "подпись"));
    }

    // --- truncations ----------------------------------------------------------

    [Test]
    public void OwnerPrompt_ClampedTo500()
    {
        var j = Build(Req(), One(), ownerPrompt: new string('п', 600));
        Assert.AreEqual(500, ((string)j["ownerPrompt"]).Length);
    }

    [Test]
    public void Catalog_ClampedTo1500()
    {
        var j = Build(Req(), One(), catalog: new string('к', 2000));
        Assert.AreEqual(1500, ((string)j["catalog"]).Length);
    }

    [Test]
    public void MessageText_ClampedTo500()
    {
        var arr = (JArray)Build(Req(), new List<MessageViewModel> { Msg(new string('т', 600), true) })["messages"];
        Assert.AreEqual(500, ((string)arr[0]["text"]).Length);
    }

    // --- sentinel + steer passthrough ----------------------------------------

    [Test]
    public void SentinelBotWaId_PassedVerbatim()
    {
        var j = Build(Req(), One(), botWaId: "-1");
        Assert.AreEqual("-1", (string)j["botWaId"]);
    }

    [Test]
    public void Steer_Set_AppearsInPayload()
    {
        var j = Build(Req(steer: "оформить заказ"), One());
        Assert.AreEqual("оформить заказ", (string)j["steerTowardText"]);
    }

    [Test]
    public void Steer_Null_SerializesAsJsonNull()
    {
        var j = Build(Req(steer: null), One());
        Assert.IsTrue(j["steerTowardText"] == null || j["steerTowardText"].Type == JTokenType.Null);
    }

    [Test]
    public void LastIncomingText_Passthrough()
    {
        var j = Build(Req(lastIncoming: "есть в наличии?"), One());
        Assert.AreEqual("есть в наличии?", (string)j["lastIncomingText"]);
    }
}
