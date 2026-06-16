using NUnit.Framework;
using UnityEngine;

public class OutboxEntryReplyTests
{
    [Test]
    public void LegacyEntry_MissingQuotedId_DeserializesNull()
    {
        var entry = JsonUtility.FromJson<OutboxStore.OutboxEntry>(
            "{\"tempId\":\"t1\",\"chatId\":\"c1\",\"text\":\"hi\"}");
        Assert.IsNull(entry.quotedMessageId);
    }

    [Test]
    public void Entry_QuotedId_RoundTrips()
    {
        var entry = new OutboxStore.OutboxEntry { tempId = "t1", chatId = "c1", text = "hi", quotedMessageId = "q1" };
        var back = JsonUtility.FromJson<OutboxStore.OutboxEntry>(JsonUtility.ToJson(entry));
        Assert.AreEqual("q1", back.quotedMessageId);
    }
}
