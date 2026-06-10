using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

public class MediaPinPersistenceTests
{
    private const string ChatId = "12345@c.us";
    private const string PinnedUrl = "https://wappi.pro/files/0c1f9a52-7e34-4b6e-9d2c-5a8f13e7b4a1.webp";

    private string dir;

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "MediaPinPersistenceTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private static MessageViewModel Sticker(string id, string media = "", long expire = 0) =>
        new MessageViewModel
        {
            messageId = id, chatId = ChatId, type = MessageType.Sticker,
            mediaUrl = media, expireTime = expire, text = "", thumbnailUrl = ""
        };

    private void SaveHistory(params MessageViewModel[] messages) =>
        ChatHistoryCache.SaveHistory(dir, ChatId, new List<MessageViewModel>(messages));

    private List<MessageViewModel> LoadHistory() => ChatHistoryCache.LoadHistory(dir, ChatId);

    [Test]
    public void Persist_PatchesMatchingEntryOnDisk()
    {
        SaveHistory(Sticker("A"), Sticker("B", "https://s3.host/store/keep-this-one.webp", 111));

        bool persisted = MediaPinPersistence.PersistMediaUrl(dir, ChatId, "A", PinnedUrl, 999);

        Assert.IsTrue(persisted);
        var reloaded = LoadHistory();
        var a = reloaded.Find(m => m.messageId == "A");
        Assert.AreEqual(PinnedUrl, a.mediaUrl);
        Assert.AreEqual(999, a.expireTime);
    }

    [Test]
    public void Persist_DoesNotDisturbOtherEntries()
    {
        SaveHistory(Sticker("A"), Sticker("B", "https://s3.host/store/keep-this-one.webp", 111));

        MediaPinPersistence.PersistMediaUrl(dir, ChatId, "A", PinnedUrl, 999);

        var b = LoadHistory().Find(m => m.messageId == "B");
        Assert.AreEqual("https://s3.host/store/keep-this-one.webp", b.mediaUrl);
        Assert.AreEqual(111, b.expireTime);
    }

    [Test]
    public void Persist_ReturnsFalseWhenMessageNotInHistory()
    {
        SaveHistory(Sticker("A"));

        bool persisted = MediaPinPersistence.PersistMediaUrl(dir, ChatId, "MISSING", PinnedUrl, 999);

        Assert.IsFalse(persisted);
        var a = LoadHistory().Find(m => m.messageId == "A");
        Assert.AreEqual("", a.mediaUrl); // file untouched
    }

    [Test]
    public void Persist_ReturnsFalseOnBadArgs()
    {
        Assert.IsFalse(MediaPinPersistence.PersistMediaUrl(null, ChatId, "A", PinnedUrl, 1));
        Assert.IsFalse(MediaPinPersistence.PersistMediaUrl(dir, null, "A", PinnedUrl, 1));
        Assert.IsFalse(MediaPinPersistence.PersistMediaUrl(dir, ChatId, null, PinnedUrl, 1));
        Assert.IsFalse(MediaPinPersistence.PersistMediaUrl(dir, ChatId, "A", null, 1));
        Assert.IsFalse(MediaPinPersistence.PersistMediaUrl(dir, ChatId, "A", "", 1));
    }

    [Test]
    public void Persist_IsIdempotent()
    {
        SaveHistory(Sticker("A"));
        Assert.IsTrue(MediaPinPersistence.PersistMediaUrl(dir, ChatId, "A", PinnedUrl, 999));

        // Re-running with identical values still reports success and leaves the pin intact
        // (a re-bind can re-run recovery for an already-pinned sticker).
        Assert.IsTrue(MediaPinPersistence.PersistMediaUrl(dir, ChatId, "A", PinnedUrl, 999));

        var a = LoadHistory().Find(m => m.messageId == "A");
        Assert.AreEqual(PinnedUrl, a.mediaUrl);
        Assert.AreEqual(999, a.expireTime);
    }

    [Test]
    public void Persist_SurvivesSaveHistoryMediaFloor()
    {
        // The media floor at the SaveHistory boundary must never let a later save with an
        // empty mediaUrl wipe the persisted pin: simulate the next sync persisting entries
        // whose media fields came back empty (aged Wappi payload).
        SaveHistory(Sticker("A"));
        MediaPinPersistence.PersistMediaUrl(dir, ChatId, "A", PinnedUrl, 999);

        SaveHistory(Sticker("A")); // empty-media save over the pinned entry

        var a = LoadHistory().Find(m => m.messageId == "A");
        Assert.AreEqual(PinnedUrl, a.mediaUrl);
        Assert.AreEqual(999, a.expireTime);
    }
}
