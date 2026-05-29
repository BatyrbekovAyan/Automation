using NUnit.Framework;

public class MediaGhostMatchTests
{
    // ── ToMessageType ─────────────────────────────────────────────

    [TestCase(AttachmentKind.Photo,        MessageType.Image)]
    [TestCase(AttachmentKind.GalleryImage, MessageType.Image)]
    [TestCase(AttachmentKind.GalleryVideo, MessageType.Video)]
    [TestCase(AttachmentKind.Document,     MessageType.Document)]
    public void ToMessageType_Returns_Expected(AttachmentKind kind, MessageType expected)
    {
        Assert.AreEqual(expected, MediaGhostMatch.ToMessageType(kind));
    }

    // ── IsKindMatch (true: media entry whose kind maps to the server type) ──

    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.Photo,        MessageType.Image,    true)]
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.GalleryImage, MessageType.Image,    true)]
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.GalleryVideo, MessageType.Video,    true)]
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.Document,     MessageType.Document, true)]
    // false: cross-kind mismatches
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.GalleryVideo, MessageType.Image,    false)]
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.Photo,        MessageType.Video,    false)]
    [TestCase((int)OutboxKind.Media, (int)AttachmentKind.Document,     MessageType.Image,    false)]
    // false: a Text-kind entry never matches a media server message
    [TestCase((int)OutboxKind.Text,  (int)AttachmentKind.Photo,        MessageType.Image,    false)]
    public void IsKindMatch_Returns_Expected(int kind, int attachmentKind, MessageType serverType, bool expected)
    {
        var entry = new OutboxStore.OutboxEntry { kind = kind, attachmentKind = attachmentKind };
        Assert.AreEqual(expected, MediaGhostMatch.IsKindMatch(entry, serverType));
    }

    [Test]
    public void IsKindMatch_NullEntry_ReturnsFalse()
    {
        Assert.IsFalse(MediaGhostMatch.IsKindMatch(null, MessageType.Image));
    }
}
