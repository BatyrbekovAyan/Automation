using NUnit.Framework;

public class WappiEndpointsTests
{
    // --- base segment per channel (api vs tapi) ---

    [Test]
    public void Sync_WhatsApp_ChatsFilter_UsesApiBase() =>
        Assert.AreEqual(
            "https://wappi.pro/api/sync/chats/filter?profile_id=abc",
            WappiEndpoints.Sync(ChatChannel.WhatsApp, "chats/filter?profile_id=abc"));

    [Test]
    public void Sync_Telegram_ChatsFilter_UsesTapiBase() =>
        Assert.AreEqual(
            "https://wappi.pro/tapi/sync/chats/filter?profile_id=abc",
            WappiEndpoints.Sync(ChatChannel.Telegram, "chats/filter?profile_id=abc"));

    // --- representative paths across the pipeline ---

    [Test]
    public void Sync_WhatsApp_MessageSend() =>
        Assert.AreEqual(
            "https://wappi.pro/api/sync/message/send?profile_id=1",
            WappiEndpoints.Sync(ChatChannel.WhatsApp, "message/send?profile_id=1"));

    [Test]
    public void Sync_Telegram_MessageSend() =>
        Assert.AreEqual(
            "https://wappi.pro/tapi/sync/message/send?profile_id=1",
            WappiEndpoints.Sync(ChatChannel.Telegram, "message/send?profile_id=1"));

    [Test]
    public void Sync_WhatsApp_MessagesGet() =>
        Assert.AreEqual(
            "https://wappi.pro/api/sync/messages/get?profile_id=1&chat_id=79@c.us",
            WappiEndpoints.Sync(ChatChannel.WhatsApp, "messages/get?profile_id=1&chat_id=79@c.us"));

    [Test]
    public void Sync_Telegram_MessageReply() =>
        Assert.AreEqual(
            "https://wappi.pro/tapi/sync/message/reply?profile_id=1",
            WappiEndpoints.Sync(ChatChannel.Telegram, "message/reply?profile_id=1"));

    [Test]
    public void Sync_WhatsApp_MessageByIdGet() =>
        Assert.AreEqual(
            "https://wappi.pro/api/sync/messages/id/get?profile_id=1&message_id=9",
            WappiEndpoints.Sync(ChatChannel.WhatsApp, "messages/id/get?profile_id=1&message_id=9"));

    [Test]
    public void Sync_Telegram_MessageByIdGet() =>
        Assert.AreEqual(
            "https://wappi.pro/tapi/sync/messages/id/get?profile_id=1&message_id=9",
            WappiEndpoints.Sync(ChatChannel.Telegram, "messages/id/get?profile_id=1&message_id=9"));

    // --- enum ordinals are persisted as int; keep 0/1 stable ---

    [Test]
    public void ChatChannel_Ordinals_AreZeroAndOne()
    {
        Assert.AreEqual(0, (int)ChatChannel.WhatsApp);
        Assert.AreEqual(1, (int)ChatChannel.Telegram);
    }
}
