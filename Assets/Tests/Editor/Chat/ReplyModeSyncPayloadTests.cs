using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

// EditMode coverage for the PURE SUP-02 client seams on Manager:
//   • BuildReplyModePayload — serialises the /webhook/SetReplyMode body
//     { profileIds:[...], chatId, suppressed } (contract from 09-01 Set_Reply_Mode Validate).
//   • AuthedProfileIds      — collects a bot's authed profile ids, dropping the
//     unauthed sentinel ("-1", Bot.UnauthedProfileSentinel) and blank ids (C1).
// No asmdef (compiles into Assembly-CSharp-Editor); no network, no PlayerPrefs.
// Payload asserted via JObject, mirroring SuggestRepliesPayloadTests.
public class ReplyModeSyncPayloadTests
{
    // --- BuildReplyModePayload -------------------------------------------------

    [Test]
    public void BuildPayload_BotDefaultRow_ChatIdStar_SuppressedTrue()
    {
        var o = JObject.Parse(Manager.BuildReplyModePayload(new[] { "pWA", "pTG" }, "*", true));
        Assert.AreEqual("*", (string)o["chatId"]);
        Assert.IsTrue((bool)o["suppressed"]);
        var ids = (JArray)o["profileIds"];
        Assert.AreEqual(2, ids.Count);
        CollectionAssert.AreEqual(new[] { "pWA", "pTG" }, ids.Select(t => (string)t).ToArray());
    }

    [Test]
    public void BuildPayload_PerChatOverride_RealChatId_SuppressedFalse()
    {
        var o = JObject.Parse(Manager.BuildReplyModePayload(new[] { "pWA" }, "7701@c.us", false));
        Assert.AreEqual("7701@c.us", (string)o["chatId"]);
        Assert.IsFalse((bool)o["suppressed"]);
        var ids = (JArray)o["profileIds"];
        Assert.AreEqual(1, ids.Count);
        Assert.AreEqual("pWA", (string)ids[0]);
    }

    // --- AuthedProfileIds (sentinel / blank filtering, C1) --------------------

    [Test]
    public void AuthedProfileIds_SkipsTelegramSentinel()
    {
        var go = new GameObject("Bot9");
        var bot = go.AddComponent<Bot>();
        bot.whatsappProfileId = "pWA";
        bot.telegramProfileId = Bot.UnauthedProfileSentinel;   // "-1"
        CollectionAssert.AreEqual(new[] { "pWA" }, Manager.AuthedProfileIds(bot));
        Object.DestroyImmediate(go);
    }

    [Test]
    public void AuthedProfileIds_SkipsBlankWhatsapp()
    {
        var go = new GameObject("Bot9");
        var bot = go.AddComponent<Bot>();
        bot.whatsappProfileId = "";
        bot.telegramProfileId = "pTG";
        CollectionAssert.AreEqual(new[] { "pTG" }, Manager.AuthedProfileIds(bot));
        Object.DestroyImmediate(go);
    }

    [Test]
    public void AuthedProfileIds_BothChannels_KeepsBoth()
    {
        var go = new GameObject("Bot9");
        var bot = go.AddComponent<Bot>();
        bot.whatsappProfileId = "pWA";
        bot.telegramProfileId = "pTG";
        CollectionAssert.AreEqual(new[] { "pWA", "pTG" }, Manager.AuthedProfileIds(bot));
        Object.DestroyImmediate(go);
    }
}
