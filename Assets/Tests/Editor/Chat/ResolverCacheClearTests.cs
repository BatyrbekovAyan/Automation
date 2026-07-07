using System.IO;
using NUnit.Framework;

// Contract for the new privacy-clear API on both resolver caches: Clear must
// drop the on-disk file AND the static in-memory layer — a surviving memory
// entry would keep serving stale data and re-persist the old map on next Put.
public class ResolverCacheClearTests
{
    private string _baseDir;

    [SetUp]
    public void SetUp()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "cache_clear_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_baseDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, recursive: true);
    }

    [Test]
    public void QuotedCache_Clear_DropsDiskAndMemory()
    {
        QuotedMessageCache.Put(_baseDir, "msg1", "привет", "Алия", MessageType.Chat, nowUnix: 1000);
        Assert.IsTrue(File.Exists(Path.Combine(_baseDir, "quoted_messages.json")));

        QuotedMessageCache.Clear(_baseDir);

        Assert.IsFalse(File.Exists(Path.Combine(_baseDir, "quoted_messages.json")));
        Assert.IsFalse(QuotedMessageCache.TryGet(_baseDir, "msg1", 1001, out _, out _, out _));
    }

    [Test]
    public void QuotedCache_PutAfterClear_DoesNotResurrectOldEntries()
    {
        QuotedMessageCache.Put(_baseDir, "old", "старое", "X", MessageType.Chat, nowUnix: 1000);
        QuotedMessageCache.Clear(_baseDir);
        QuotedMessageCache.Put(_baseDir, "new", "новое", "Y", MessageType.Chat, nowUnix: 2000);

        Assert.IsFalse(QuotedMessageCache.TryGet(_baseDir, "old", 2001, out _, out _, out _));
        Assert.IsTrue(QuotedMessageCache.TryGet(_baseDir, "new", 2001, out string text, out _, out _));
        Assert.AreEqual("новое", text);
    }

    [Test]
    public void ReactionCache_Clear_DropsDiskAndMemory()
    {
        ReactionTargetCache.Put(_baseDir, "r1", "текст", "chat", "Алия", nowUnix: 1000);
        Assert.IsTrue(File.Exists(Path.Combine(_baseDir, "reaction_targets.json")));

        ReactionTargetCache.Clear(_baseDir);

        Assert.IsFalse(File.Exists(Path.Combine(_baseDir, "reaction_targets.json")));
        Assert.IsFalse(ReactionTargetCache.TryGet(_baseDir, "r1", 1001, out _, out _, out _));
    }

    [Test]
    public void Clear_MissingDirOrNull_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => QuotedMessageCache.Clear(Path.Combine(_baseDir, "nope")));
        Assert.DoesNotThrow(() => QuotedMessageCache.Clear(null));
        Assert.DoesNotThrow(() => ReactionTargetCache.Clear(null));
    }
}
