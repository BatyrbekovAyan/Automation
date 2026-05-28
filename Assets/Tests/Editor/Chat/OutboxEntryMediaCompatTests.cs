using NUnit.Framework;
using UnityEngine;

public class OutboxEntryMediaCompatTests
{
    [Test]
    public void LegacyTextJson_DeserializesAsTextKind()
    {
        // A pre-part-c outbox entry has none of the media fields.
        string legacy =
            "{\"tempId\":\"sending_1\",\"chatId\":\"79@c.us\",\"text\":\"hi\"," +
            "\"timestamp\":123,\"attemptCount\":1,\"profileId\":\"P\"}";

        var e = JsonUtility.FromJson<OutboxStore.OutboxEntry>(legacy);

        Assert.AreEqual("sending_1", e.tempId);
        Assert.AreEqual("hi", e.text);
        Assert.AreEqual(0, e.kind);                       // missing field defaults to 0
        Assert.AreEqual((int)OutboxKind.Text, e.kind);    // 0 == Text
    }

    [Test]
    public void MediaEntry_RoundTrips()
    {
        var orig = new OutboxStore.OutboxEntry
        {
            tempId = "staging_2", chatId = "79@c.us", text = "cap",
            timestamp = 456, attemptCount = 1, profileId = "P",
            kind = (int)OutboxKind.Media, attachmentKind = (int)AttachmentKind.GalleryVideo,
            mediaPath = "/tmp/v.mp4", mimeType = "video/mp4", fileName = "v.mp4",
            mediaUrl = "", thumbnailUrl = "thumb://staged/staging_2", videoUrl = "file:///tmp/v.mp4",
            aspectRatio = 1.77f, duration = 12
        };

        var rt = JsonUtility.FromJson<OutboxStore.OutboxEntry>(JsonUtility.ToJson(orig));

        Assert.AreEqual((int)OutboxKind.Media, rt.kind);
        Assert.AreEqual((int)AttachmentKind.GalleryVideo, rt.attachmentKind);
        Assert.AreEqual("/tmp/v.mp4", rt.mediaPath);
        Assert.AreEqual("video/mp4", rt.mimeType);
        Assert.AreEqual("v.mp4", rt.fileName);
        Assert.AreEqual("thumb://staged/staging_2", rt.thumbnailUrl);
        Assert.AreEqual("file:///tmp/v.mp4", rt.videoUrl);
        Assert.AreEqual(1.77f, rt.aspectRatio, 0.0001f);
        Assert.AreEqual(12, rt.duration);
    }
}
