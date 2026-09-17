using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class WappiProxyRequestTests
{
    [Test]
    public void ComposeBody_MergesOpAndChannelIntoTheArgs()
    {
        var jo = JObject.Parse(WappiProxyRequest.ComposeBody(WappiProxy.Op.MessagesGet, ChatChannel.Telegram,
            new { profile_id = "p1", chat_id = "c@c.us", limit = 15, offset = 0 }));
        Assert.AreEqual("messages.get", (string)jo["op"]);
        Assert.AreEqual("telegram", (string)jo["channel"]);
        Assert.AreEqual("p1", (string)jo["profile_id"]);
        Assert.AreEqual("c@c.us", (string)jo["chat_id"]);
        Assert.AreEqual(15, (int)jo["limit"]);
        Assert.AreEqual(0, (int)jo["offset"]);
    }

    [Test]
    public void ComposeBody_NullArgs_StillCarriesOpAndChannel()
    {
        var jo = JObject.Parse(WappiProxyRequest.ComposeBody(WappiProxy.Op.ProfileAdd, ChatChannel.WhatsApp, null));
        Assert.AreEqual("profile.add", (string)jo["op"]);
        Assert.AreEqual("whatsapp", (string)jo["channel"]);
    }

    [Test]
    public void ComposeBody_ArgsCannotOverrideOpOrChannel()
    {
        var jo = JObject.Parse(WappiProxyRequest.ComposeBody(WappiProxy.Op.Status, ChatChannel.WhatsApp, new { op = "profile.delete", channel = "telegram", profile_id = "p" }));
        Assert.AreEqual("status", (string)jo["op"]);
        Assert.AreEqual("whatsapp", (string)jo["channel"]);
    }

    [Test]
    public void DigitsOnly_StripsFormatting()
    {
        Assert.AreEqual("7771234567", WappiProxyRequest.DigitsOnly("+7 (771) 23-45-67"));
        Assert.AreEqual("", WappiProxyRequest.DigitsOnly(null));
    }

    [Test]
    public void MediaArgs_MapKindsAndKeepTheDocumentFileNameContract()
    {
        Assert.AreEqual("img", WappiMediaRequestFactory.ProxyKind(AttachmentKind.Photo));
        Assert.AreEqual("img", WappiMediaRequestFactory.ProxyKind(AttachmentKind.GalleryImage));
        Assert.AreEqual("video", WappiMediaRequestFactory.ProxyKind(AttachmentKind.GalleryVideo));
        Assert.AreEqual("document", WappiMediaRequestFactory.ProxyKind(AttachmentKind.Document));

        var doc = WappiMediaRequestFactory.BuildProxyArgs(AttachmentKind.Document, "p1", "77001234567@c.us", null, "", "AAAA");
        Assert.AreEqual("p1", doc.profile_id);
        Assert.AreEqual("document", doc.kind);
        Assert.AreEqual("file", doc.file_name, "documents always carry a file_name");
        Assert.AreEqual("", doc.caption);
        Assert.AreEqual("AAAA", doc.b64_file);
        Assert.AreEqual(WappiMediaRequestFactory.NormalizeRecipient("77001234567@c.us"), doc.recipient);

        var img = WappiMediaRequestFactory.BuildProxyArgs(AttachmentKind.Photo, "p1", "77001234567@c.us", "hi", "x.jpg", "BBBB");
        Assert.IsNull(img.file_name, "images never send file_name (Wappi contract)");
        var jo = JObject.Parse(WappiProxyRequest.ComposeBody(WappiProxy.Op.MediaSend, ChatChannel.WhatsApp, img));
        Assert.AreEqual("media.send", (string)jo["op"]);
        Assert.AreEqual("img", (string)jo["kind"]);
    }

    [Test]
    public void N8nResponse_ReadsRefusalEnvelopes()
    {
        Assert.IsTrue(N8nResponse.IsFailureBody("{\"success\":false,\"error\":\"unauthorized\"}"));
        Assert.AreEqual("unauthorized", N8nResponse.ErrorOf("{\"success\":false,\"error\":\"unauthorized\"}"));
        Assert.IsFalse(N8nResponse.IsFailureBody("{\"success\":true,\"written\":1}"));
        Assert.IsFalse(N8nResponse.IsFailureBody("{\"id\":\"abc\"}"), "a create response without success is not a refusal");
        Assert.IsFalse(N8nResponse.IsFailureBody("not json"));
        Assert.IsFalse(N8nResponse.IsFailureBody(null));
    }
}
