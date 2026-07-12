using NUnit.Framework;
using UnityEngine;

/// <summary>
/// A text outbox retry rebuilds its send URL from the entry's snapshotted channel
/// (RetryRoutine → PostTextMessageRoutine(..., (ChatChannel)entry.channel)). The
/// coroutine itself is not unit-reachable, so these tests lock the pure contract it
/// relies on: (ChatChannel)entry.channel → WappiEndpoints.Sync base. A WhatsApp
/// entry (0) rebuilds an api URL; a Telegram entry (1) rebuilds a tapi URL; a legacy
/// entry with no channel key defaults to WhatsApp (0 → api).
/// </summary>
public class OutboxRetryChannelTests
{
    private static string RetryUrl(OutboxStore.OutboxEntry entry) =>
        WappiEndpoints.Sync((ChatChannel)entry.channel, $"message/send?profile_id={entry.profileId}");

    [Test]
    public void WhatsAppEntry_RebuildsApiUrl()
    {
        var entry = new OutboxStore.OutboxEntry { profileId = "P", channel = (int)ChatChannel.WhatsApp };
        Assert.AreEqual("https://wappi.pro/api/sync/message/send?profile_id=P", RetryUrl(entry));
    }

    [Test]
    public void TelegramEntry_RebuildsTapiUrl()
    {
        var entry = new OutboxStore.OutboxEntry { profileId = "P", channel = (int)ChatChannel.Telegram };
        Assert.AreEqual("https://wappi.pro/tapi/sync/message/send?profile_id=P", RetryUrl(entry));
    }

    [Test]
    public void LegacyEntry_MissingChannel_RebuildsApiUrl()
    {
        // A pre-channel outbox entry has no channel key → deserializes to 0 (WhatsApp).
        var entry = JsonUtility.FromJson<OutboxStore.OutboxEntry>(
            "{\"tempId\":\"t1\",\"chatId\":\"c1\",\"text\":\"hi\",\"profileId\":\"P\"}");
        Assert.AreEqual(0, entry.channel);
        Assert.AreEqual("https://wappi.pro/api/sync/message/send?profile_id=P", RetryUrl(entry));
    }

    [Test]
    public void ReplyRetry_TelegramEntry_RebuildsTapiReplyUrl()
    {
        // A Telegram reply entry (quotedMessageId set) rebuilds the tapi reply endpoint.
        var entry = new OutboxStore.OutboxEntry
        {
            profileId = "P", channel = (int)ChatChannel.Telegram, quotedMessageId = "M9"
        };
        string url = WappiEndpoints.Sync((ChatChannel)entry.channel, $"message/reply?profile_id={entry.profileId}");
        Assert.AreEqual("https://wappi.pro/tapi/sync/message/reply?profile_id=P", url);
    }
}
