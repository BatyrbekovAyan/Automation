using System;
using System.IO;
using NUnit.Framework;

public class ReactionTargetCacheTests
{
    private static string FreshDir(string tag)
    {
        string dir = Path.Combine(Path.GetTempPath(), "rtc_" + tag + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public void PutThenGet_ReturnsValues()
    {
        string dir = FreshDir("putget");
        ReactionTargetCache.Put(dir, "r1", "Hello", "chat");

        bool ok = ReactionTargetCache.TryGet(dir, "r1", out string text, out string type);

        Assert.IsTrue(ok);
        Assert.AreEqual("Hello", text);
        Assert.AreEqual("chat", type);
    }

    [Test]
    public void Put_WritesFileToDisk()
    {
        string dir = FreshDir("disk");
        ReactionTargetCache.Put(dir, "r1", "Hello", "chat");

        string path = Path.Combine(dir, "reaction_targets.json");
        Assert.IsTrue(File.Exists(path), "cache file should be written");
        StringAssert.Contains("r1", File.ReadAllText(path));
        StringAssert.Contains("Hello", File.ReadAllText(path));
    }

    [Test]
    public void LoadsFromExistingFile_OnFirstAccess()
    {
        // A never-seen dir with a pre-existing file exercises the disk-load path
        // (in-memory map is keyed per dir, so this dir starts cold).
        string dir = FreshDir("load");
        File.WriteAllText(Path.Combine(dir, "reaction_targets.json"),
            "{\"entries\":[{\"reactionId\":\"r9\",\"text\":\"Persisted\",\"type\":\"image\"}]}");

        bool ok = ReactionTargetCache.TryGet(dir, "r9", out string text, out string type);

        Assert.IsTrue(ok);
        Assert.AreEqual("Persisted", text);
        Assert.AreEqual("image", type);
    }

    [Test]
    public void UnknownId_ReturnsFalse()
    {
        string dir = FreshDir("unknown");
        Assert.IsFalse(ReactionTargetCache.TryGet(dir, "nope", out _, out _));
    }

    [Test]
    public void EmptyOutcome_IsCachedAndDistinctFromMiss()
    {
        string dir = FreshDir("emptyok");
        ReactionTargetCache.Put(dir, "r2", "", ""); // "resolved: nothing to show"

        bool ok = ReactionTargetCache.TryGet(dir, "r2", out string text, out string type);

        Assert.IsTrue(ok, "an empty outcome is still a cached hit (no refetch)");
        Assert.AreEqual("", text);
        Assert.AreEqual("", type);
    }
}
