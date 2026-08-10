using System.Collections.Generic;
using System.Linq;
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

    // channel defaults to WhatsApp so every pre-v1.1 test stays byte-identical: the `profileId`
    // arg maps to whatsappProfileId and `botWaId` to whatsappWorkflowId, so with channel=WhatsApp
    // the emitted profileId==profileId and botWaId==botWaId exactly as before. Telegram ids default
    // to distinct sentinels so a channel=Telegram test proves the SELECTION, not a coincidence.
    private static JObject Build(SuggestionRequest req, List<MessageViewModel> msgs,
        string profileId = "p1", string botWaId = "wf1", string businessTypeId = "auto_parts",
        string businessName = "Магазин", string ownerPrompt = "", string catalog = "",
        ChatChannel channel = ChatChannel.WhatsApp,
        string telegramProfileId = "tgpid", string telegramWorkflowId = "wf_tg",
        string businessKnowledge = "", string now = "")
        => JObject.Parse(N8nSuggestionsProvider.BuildPayloadJson(
            req, channel, profileId, telegramProfileId, botWaId, telegramWorkflowId,
            businessTypeId, businessName, ownerPrompt, catalog, msgs,
            businessKnowledge, now));

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

    // --- <=24 cap keeps the NEWEST twenty-four, still oldest->newest ---------
    // (Raised from 12 in the 2026-08 audit F8 — order threads outgrow 12 turns.)

    [Test]
    public void Cap24_KeepsNewestTwentyFour_OldestToNewest()
    {
        var msgs = new List<MessageViewModel>();
        for (int i = 0; i < 30; i++) msgs.Add(Msg("m" + i, true));
        var arr = (JArray)Build(Req(), msgs)["messages"];
        Assert.AreEqual(24, arr.Count);
        Assert.AreEqual("m6", (string)arr[0]["text"]);    // m0..m5 dropped
        Assert.AreEqual("m29", (string)arr[23]["text"]);  // newest kept, last
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
    public void OwnerPrompt_ClampedTo2000()
    {
        // 500 silently truncated chip-composed prompts mid-line (audit F8).
        var j = Build(Req(), One(), ownerPrompt: new string('п', 2200));
        Assert.AreEqual(2000, ((string)j["ownerPrompt"]).Length);
    }

    [Test]
    public void Catalog_ClampedTo2500()
    {
        var j = Build(Req(), One(), catalog: new string('к', 3000));
        Assert.AreEqual(2500, ((string)j["catalog"]).Length);
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

    // --- v1.1 channel-selection matrix (SUGG-01) -----------------------------

    [Test]
    public void TelegramChat_SelectsTelegramProfileAndChannel()
    {
        var j = Build(Req(), One(), profileId: "wap",
            channel: ChatChannel.Telegram, telegramProfileId: "tgp");
        Assert.AreEqual("tgp", (string)j["profileId"]);       // channel-resolved to the TG profile
        Assert.AreEqual("telegram", (string)j["channel"]);
    }

    [Test]
    public void WhatsAppChat_SelectsWhatsAppProfileAndChannel()
    {
        var j = Build(Req(), One(), profileId: "wap",
            channel: ChatChannel.WhatsApp, telegramProfileId: "tgp");
        Assert.AreEqual("wap", (string)j["profileId"]);       // stays the WA profile
        Assert.AreEqual("whatsapp", (string)j["channel"]);
    }

    [Test]
    public void BotWaId_AlwaysPresent_EvenOnTelegram()
    {
        // botWaId == whatsappWorkflowId is ALWAYS sent (server's default WA RAG branch / backward compat).
        var j = Build(Req(), One(), botWaId: "wf_wa", channel: ChatChannel.Telegram);
        Assert.AreEqual("wf_wa", (string)j["botWaId"]);
    }

    [Test]
    public void BotTgId_CarriesTelegramWorkflowId()
    {
        var j = Build(Req(), One(), channel: ChatChannel.Telegram, telegramWorkflowId: "wf_tg");
        Assert.AreEqual("wf_tg", (string)j["botTgId"]);
    }

    [Test]
    public void TelegramOnlyBot_WaSentinelPassesThrough()
    {
        // The TG-only bot from the CONTEXT matrix: no WA workflow (sentinel), a live TG workflow.
        var j = Build(Req(), One(),
            botWaId: "-1", channel: ChatChannel.Telegram,
            telegramProfileId: "tgp", telegramWorkflowId: "wf_tg");
        Assert.AreEqual("tgp", (string)j["profileId"]);
        Assert.AreEqual("-1", (string)j["botWaId"]);          // WA sentinel verbatim (server skips WA RAG)
        Assert.AreEqual("wf_tg", (string)j["botTgId"]);
        Assert.AreEqual("telegram", (string)j["channel"]);
    }

    [Test]
    public void ChannelField_IsLowercaseEnumDerived()
    {
        var wa = Build(Req(), One(), channel: ChatChannel.WhatsApp);
        var tg = Build(Req(), One(), channel: ChatChannel.Telegram);
        Assert.AreEqual("whatsapp", (string)wa["channel"]);
        Assert.AreEqual("telegram", (string)tg["channel"]);
        // Never the enum's PascalCase ToString() — the wire value is a fixed lowercase constant
        // (T-07-01-01: no free-form / user-supplied string can select the server's RAG metadata key).
        Assert.AreNotEqual("WhatsApp", (string)wa["channel"]);
        Assert.AreNotEqual("Telegram", (string)tg["channel"]);
    }

    // --- v1.2 grounding keys (audit F1/F2: the Авто-bot's knowledge + local time) ---

    [Test]
    public void BusinessKnowledge_Passthrough()
    {
        var j = Build(Req(), One(), businessKnowledge: "About Business:\nЦветы\n\nКонтакты:\nАдрес: ул. Абая 1");
        Assert.AreEqual("About Business:\nЦветы\n\nКонтакты:\nАдрес: ул. Абая 1", (string)j["businessKnowledge"]);
    }

    [Test]
    public void BusinessKnowledge_ComposedValue_CarriesContactBlock()
    {
        // verify-the-composed-output: the payload must carry the SAME composed string the
        // Авто-mode workflows ingest — assert the composed VALUE end-to-end, not just a key.
        string composed = Manager.ComposeBusinessKnowledge(
            "Магазин цветов в Астане", "+7 777 123 45 67", "09:00–19:00", "ул. Абая 1", "", "");
        var j = Build(Req(), One(), businessKnowledge: composed);
        var wire = (string)j["businessKnowledge"];
        StringAssert.Contains("About Business:\nМагазин цветов в Астане", wire);
        StringAssert.Contains("Контакты:", wire);
        StringAssert.Contains("Телефон: +7 777 123 45 67", wire);
        StringAssert.Contains("Часы работы: 09:00–19:00", wire);
        StringAssert.Contains("Адрес: ул. Абая 1", wire);
    }

    [Test]
    public void BusinessKnowledge_ClampedTo1200()
    {
        var j = Build(Req(), One(), businessKnowledge: new string('б', 1500));
        Assert.AreEqual(1200, ((string)j["businessKnowledge"]).Length);
    }

    [Test]
    public void Now_Passthrough()
    {
        var j = Build(Req(), One(), now: "2026-08-10 15:04, воскресенье");
        Assert.AreEqual("2026-08-10 15:04, воскресенье", (string)j["now"]);
    }

    // --- additive-identity: a WhatsApp request is structurally identical to v1 -

    [Test]
    public void WhatsAppRequest_AdditivelyIdenticalToV1()
    {
        var req = Req(seq: 42, steer: "оформить", lastIncoming: "в наличии?", chatId: "500@c.us");
        var msgs = new List<MessageViewModel> { Msg("привет", true), Msg("здравствуйте", false) };
        var j = Build(req, msgs,
            profileId: "wap", botWaId: "wf_wa", businessTypeId: "flowers",
            businessName: "Цветочный", ownerPrompt: "будь вежлив", catalog: "• Роза — 500",
            channel: ChatChannel.WhatsApp, telegramProfileId: "tgp", telegramWorkflowId: "wf_tg",
            businessKnowledge: "About Business:\nЦветы", now: "2026-08-10 15:04, воскресенье");

        // The v1.1 keys (channel/botTgId) AND v1.2 keys (businessKnowledge/now) ARE present...
        Assert.AreEqual("whatsapp", (string)j["channel"]);
        Assert.IsNotNull(j["botTgId"]);
        Assert.AreEqual("wf_tg", (string)j["botTgId"]);
        Assert.IsNotNull(j["businessKnowledge"]);
        Assert.IsNotNull(j["now"]);

        // ...and removing EXACTLY those four yields the frozen v1 object again. This test proves
        // STRUCTURAL identity: JToken.DeepEquals is property-order-insensitive and the key-set
        // assertion below sorts before comparing — byte order is NOT asserted here (it follows
        // separately from Json.NET's declaration-order emission over the appended-last fields).
        j.Remove("channel");
        j.Remove("botTgId");
        j.Remove("businessKnowledge");
        j.Remove("now");

        var expectedV1 = new JObject
        {
            ["v"]                = 1,
            ["requestSeq"]       = 42L,
            ["profileId"]        = "wap",
            ["chatId"]           = "500@c.us",
            ["botWaId"]          = "wf_wa",
            ["businessTypeId"]   = "flowers",
            ["businessName"]     = "Цветочный",
            ["ownerPrompt"]      = "будь вежлив",
            ["catalog"]          = "• Роза — 500",
            ["steerTowardText"]  = "оформить",
            ["lastIncomingText"] = "в наличии?",
            ["messages"]         = new JArray
            {
                new JObject { ["role"] = "client",   ["text"] = "привет",       ["ts"] = 1000L },
                new JObject { ["role"] = "business", ["text"] = "здравствуйте", ["ts"] = 1000L },
            },
        };

        Assert.IsTrue(JToken.DeepEquals(expectedV1, j),
            $"Residual object must deep-equal v1.\nExpected: {expectedV1}\nActual:   {j}");

        // Belt-and-suspenders: the residual is EXACTLY the frozen 12-key v1 set (no rename, no extra add).
        var residualKeys = j.Properties().Select(p => p.Name)
            .OrderBy(n => n, System.StringComparer.Ordinal).ToArray();
        var v1Keys = expectedV1.Properties().Select(p => p.Name)
            .OrderBy(n => n, System.StringComparer.Ordinal).ToArray();
        Assert.AreEqual(12, residualKeys.Length);
        CollectionAssert.AreEqual(v1Keys, residualKeys);
    }
}
