using System.Collections.Generic;
using NUnit.Framework;

public class MessageMediaMergeTests
{
    private static MessageViewModel Vm(string id, string thumb = "", string video = "",
                                       string media = "", long expire = 0, float aspect = 0f) =>
        new MessageViewModel
        {
            messageId = id, thumbnailUrl = thumb, videoUrl = video,
            mediaUrl = media, expireTime = expire, aspectRatio = aspect, type = MessageType.Video
        };

    [Test]
    public void CarryForward_FillsEmptyMediaFromExisting()
    {
        var incoming = Vm("A");                                   // all empty (aged payload)
        var existing = Vm("A", "thumb://A", "https://s3/v.mp4", "", 123, 0.5625f);

        MessageMediaMerge.CarryForwardMedia(incoming, existing);

        Assert.AreEqual("thumb://A", incoming.thumbnailUrl);
        Assert.AreEqual("https://s3/v.mp4", incoming.videoUrl);
        Assert.AreEqual(123, incoming.expireTime);               // expire travels with videoUrl
        Assert.AreEqual(0.5625f, incoming.aspectRatio);
    }

    [Test]
    public void CarryForward_DoesNotOverwritePopulatedIncoming()
    {
        // A genuine refresh: incoming has a NEW signed url — it must win, not be clobbered.
        var incoming = Vm("A", "thumb://A", "https://s3/NEW.mp4?sig=new", "", 999);
        var existing = Vm("A", "thumb://A", "https://s3/OLD.mp4?sig=old", "", 111);

        MessageMediaMerge.CarryForwardMedia(incoming, existing);

        Assert.AreEqual("https://s3/NEW.mp4?sig=new", incoming.videoUrl);
        Assert.AreEqual(999, incoming.expireTime);
    }

    [Test]
    public void CarryForward_ExpireMovesWithMediaUrl()
    {
        var incoming = Vm("A");
        var existing = Vm("A", "", "", "https://s3/img.jpg", 555);

        MessageMediaMerge.CarryForwardMedia(incoming, existing);

        Assert.AreEqual("https://s3/img.jpg", incoming.mediaUrl);
        Assert.AreEqual(555, incoming.expireTime);
    }

    [Test]
    public void CarryForward_DoesNotCarryExpireWithoutAUrl()
    {
        // existing has an expire but no urls -> nothing to anchor it to -> not carried.
        var incoming = Vm("A");
        var existing = Vm("A", "", "", "", 777);

        MessageMediaMerge.CarryForwardMedia(incoming, existing);

        Assert.AreEqual(0, incoming.expireTime);
    }

    [Test]
    public void ApplyMediaFloor_HealsAgedEntryFromOnDiskGoodEntry()
    {
        var incoming = new List<MessageViewModel> { Vm("A"), Vm("B", "thumb://B", "https://s3/b.mp4", "", 5) };
        var existing = new List<MessageViewModel> { Vm("A", "thumb://A", "https://s3/a.mp4", "", 9, 0.5625f) };

        MessageMediaMerge.ApplyMediaFloor(incoming, existing);

        Assert.AreEqual("thumb://A", incoming[0].thumbnailUrl);   // A healed from disk
        Assert.AreEqual("https://s3/a.mp4", incoming[0].videoUrl);
        Assert.AreEqual("thumb://B", incoming[1].thumbnailUrl);   // B untouched (not on disk)
    }

    [Test]
    public void ApplyMediaFloor_NullOrEmptyExisting_IsNoOp()
    {
        var incoming = new List<MessageViewModel> { Vm("A") };
        Assert.DoesNotThrow(() => MessageMediaMerge.ApplyMediaFloor(incoming, null));
        Assert.DoesNotThrow(() => MessageMediaMerge.ApplyMediaFloor(incoming, new List<MessageViewModel>()));
        Assert.AreEqual("", incoming[0].thumbnailUrl);
    }

    [Test]
    public void ApplyMediaFloor_NoMatchingId_LeavesEntryEmpty()
    {
        var incoming = new List<MessageViewModel> { Vm("A") };
        var existing = new List<MessageViewModel> { Vm("Z", "thumb://Z", "https://s3/z.mp4") };

        MessageMediaMerge.ApplyMediaFloor(incoming, existing);

        Assert.AreEqual("", incoming[0].thumbnailUrl);            // no id match -> unchanged
        Assert.AreEqual("", incoming[0].videoUrl);
    }

    // --- RefreshPresentation (05-07-REVIEW WR-01): a sync that re-encounters a row cached by
    // --- an older build must backfill the new presentation fields (refined type, кружок/GIF
    // --- flags, mime) so the pre-update cache — the exact UAT probe messages — re-renders.

    private static NormalizedMessage Norm(MessageType type, string mime = null,
                                          bool note = false, bool gif = false, bool sticker = false) =>
        new NormalizedMessage
        {
            id = "A", messageType = type, mimeType = mime,
            isVideoNote = note, isGif = gif, isSticker = sticker
        };

    [Test]
    public void RefreshPresentation_TgsCachedAsDocument_BecomesStickerWithIsSticker()
    {
        // Pre-refine cache: 05-06 stamped the tgs mime but typed the row Document.
        var cached = new MessageViewModel { messageId = "A", type = MessageType.Document,
                                            mimeType = "application/x-tgsticker" };
        var fresh = Norm(MessageType.Sticker, "application/x-tgsticker", sticker: true);

        Assert.IsTrue(MessageMediaMerge.RefreshPresentation(fresh, cached));
        Assert.AreEqual(MessageType.Sticker, cached.type);
        Assert.IsTrue(cached.isSticker);                          // travels with the type
    }

    [Test]
    public void RefreshPresentation_BackfillsVideoNoteFlag_TypeUntouched()
    {
        // Pre-update cache: the note was already Video, but isVideoNote didn't exist yet.
        var cached = new MessageViewModel { messageId = "A", type = MessageType.Video,
                                            mimeType = "video/mp4" };
        var fresh = Norm(MessageType.Video, "video/mp4", note: true);

        Assert.IsTrue(MessageMediaMerge.RefreshPresentation(fresh, cached));
        Assert.IsTrue(cached.isVideoNote);
        Assert.AreEqual(MessageType.Video, cached.type);
        Assert.IsFalse(cached.isSticker);                         // type unchanged => untouched
    }

    [Test]
    public void RefreshPresentation_BackfillsGifFlagAndMime()
    {
        var cached = new MessageViewModel { messageId = "A", type = MessageType.Video, mimeType = "" };
        var fresh = Norm(MessageType.Video, "video/mp4", gif: true);

        Assert.IsTrue(MessageMediaMerge.RefreshPresentation(fresh, cached));
        Assert.IsTrue(cached.isGif);
        Assert.AreEqual("video/mp4", cached.mimeType);
    }

    [Test]
    public void RefreshPresentation_WhatsAppRow_NoOp_NullVsEmptyMimeIsEqual()
    {
        // WA neutrality: flags are false on both sides, and JsonUtility round-trips a null
        // mime as "" — that alone must NOT count as a change, or every no-mime WA media row
        // would dirty the cache and re-bind on every sync.
        var cached = new MessageViewModel { messageId = "A", type = MessageType.Image, mimeType = "" };
        var fresh = Norm(MessageType.Image, mime: null);

        Assert.IsFalse(MessageMediaMerge.RefreshPresentation(fresh, cached));
        Assert.AreEqual(MessageType.Image, cached.type);
        Assert.IsFalse(cached.isVideoNote);
        Assert.IsFalse(cached.isGif);
        Assert.AreEqual("", cached.mimeType);                     // not clobbered to null
    }

    [Test]
    public void RefreshPresentation_SecondRun_IsIdempotent()
    {
        var cached = new MessageViewModel { messageId = "A", type = MessageType.Document,
                                            mimeType = "application/x-tgsticker" };
        var fresh = Norm(MessageType.Sticker, "application/x-tgsticker", sticker: true);

        Assert.IsTrue(MessageMediaMerge.RefreshPresentation(fresh, cached));
        Assert.IsFalse(MessageMediaMerge.RefreshPresentation(fresh, cached));   // converged
    }

    [Test]
    public void RefreshPresentation_UnknownRefreshedType_NeverClobbersCachedType()
    {
        var cached = new MessageViewModel { messageId = "A", type = MessageType.Video,
                                            mimeType = "video/mp4" };
        var fresh = Norm(MessageType.Unknown, "video/mp4");

        Assert.IsFalse(MessageMediaMerge.RefreshPresentation(fresh, cached));
        Assert.AreEqual(MessageType.Video, cached.type);
    }

    [Test]
    public void RefreshPresentation_NullArgs_False()
    {
        Assert.IsFalse(MessageMediaMerge.RefreshPresentation(null, new MessageViewModel()));
        Assert.IsFalse(MessageMediaMerge.RefreshPresentation(Norm(MessageType.Video), null));
    }
}
