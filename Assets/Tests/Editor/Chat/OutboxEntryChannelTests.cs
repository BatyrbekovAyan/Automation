using NUnit.Framework;
using UnityEngine;

public class OutboxEntryChannelTests
{
    [Test]
    public void Entry_Channel_RoundTrips()
    {
        var entry = new OutboxStore.OutboxEntry
        {
            tempId = "t1", chatId = "c1", text = "hi",
            channel = (int)ChatChannel.Telegram
        };
        var back = JsonUtility.FromJson<OutboxStore.OutboxEntry>(JsonUtility.ToJson(entry));
        Assert.AreEqual(1, back.channel);
        Assert.AreEqual((int)ChatChannel.Telegram, back.channel);
    }

    [Test]
    public void LegacyEntry_MissingChannel_DefaultsToWhatsApp()
    {
        // A pre-part-c / pre-channel outbox entry has no channel key.
        var entry = JsonUtility.FromJson<OutboxStore.OutboxEntry>(
            "{\"tempId\":\"t1\",\"chatId\":\"c1\",\"text\":\"hi\"," +
            "\"timestamp\":123,\"attemptCount\":1,\"profileId\":\"P\"}");
        Assert.AreEqual(0, entry.channel);                        // missing field defaults to 0
        Assert.AreEqual((int)ChatChannel.WhatsApp, entry.channel); // 0 == WhatsApp
    }
}
