using System;
using System.IO;
using NUnit.Framework;

public class ReactionTargetCacheTests
{
    private const long Ttl = 7L * 24 * 60 * 60; // must match ReactionTargetCache.TtlSeconds

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
        ReactionTargetCache.Put(dir, "r1", "Hello", "chat", 1000);

        bool ok = ReactionTargetCache.TryGet(dir, "r1", 1000, out string text, out string type);

        Assert.IsTrue(ok);
        Assert.AreEqual("Hello", text);
        Assert.AreEqual("chat", type);
    }

    [Test]
    public void Put_WritesFileToDisk()
    {
        string dir = FreshDir("disk");
        ReactionTargetCache.Put(dir, "r1", "Hello", "chat", 1000);

        string path = Path.Combine(dir, "reaction_targets.json");
        Assert.IsTrue(File.Exists(path), "cache file should be written");
        StringAssert.Contains("r1", File.ReadAllText(path));
        StringAssert.Contains("Hello", File.ReadAllText(path));
    }

    [Test]
    public void LoadsFromExistingFile_OnFirstAccess()
    {
        string dir = FreshDir("load");
        File.WriteAllText(Path.Combine(dir, "reaction_targets.json"),
            "{\"entries\":[{\"reactionId\":\"r9\",\"text\":\"Persisted\",\"type\":\"image\",\"resolvedAt\":1000}]}");

        bool ok = ReactionTargetCache.TryGet(dir, "r9", 1000, out string text, out string type);

        Assert.IsTrue(ok);
        Assert.AreEqual("Persisted", text);
        Assert.AreEqual("image", type);
    }

    [Test]
    public void UnknownId_ReturnsFalse()
    {
        string dir = FreshDir("unknown");
        Assert.IsFalse(ReactionTargetCache.TryGet(dir, "nope", 1000, out _, out _));
    }

    [Test]
    public void NotFoundOutcome_IsCachedAndNeverExpires()
    {
        string dir = FreshDir("nf");
        ReactionTargetCache.Put(dir, "r2", "", "", 0); // "resolved: nothing to show"

        bool ok = ReactionTargetCache.TryGet(dir, "r2", 100 * Ttl, out string text, out string type);

        Assert.IsTrue(ok, "an empty (not-found) outcome is a stable hit and exempt from the TTL");
        Assert.AreEqual("", text);
        Assert.AreEqual("", type);
    }

    [Test]
    public void ResolvedEntry_ExpiresAfterTtl()
    {
        string dir = FreshDir("ttl");
        ReactionTargetCache.Put(dir, "rE", "Hi", "chat", 1000);

        Assert.IsTrue(ReactionTargetCache.TryGet(dir, "rE", 1000 + Ttl, out _, out _), "fresh within TTL");
        Assert.IsFalse(ReactionTargetCache.TryGet(dir, "rE", 1000 + Ttl + 1, out _, out _), "stale past TTL");
    }

    [Test]
    public void EvictsOldestBeyondCapacity()
    {
        string dir = FreshDir("cap");
        // 501 entries (cap = 500) with increasing resolvedAt; r0 is the oldest and must evict.
        for (int i = 0; i <= 500; i++)
            ReactionTargetCache.Put(dir, "r" + i, "t", "chat", i);

        Assert.IsFalse(ReactionTargetCache.TryGet(dir, "r0", 500, out _, out _), "oldest evicted past cap");
        Assert.IsTrue(ReactionTargetCache.TryGet(dir, "r1", 500, out _, out _), "second-oldest retained");
        Assert.IsTrue(ReactionTargetCache.TryGet(dir, "r500", 500, out _, out _), "newest retained");
    }

    [Test]
    public void OldSchemaFile_FoundExpires_NotFoundStays()
    {
        // Pre-TTL schema (no resolvedAt) loads with resolvedAt = 0.
        string dir = FreshDir("oldschema");
        File.WriteAllText(Path.Combine(dir, "reaction_targets.json"),
            "{\"entries\":[" +
            "{\"reactionId\":\"rOldFound\",\"text\":\"Hi\",\"type\":\"chat\"}," +
            "{\"reactionId\":\"rOldNF\",\"text\":\"\",\"type\":\"\"}]}");

        Assert.IsFalse(ReactionTargetCache.TryGet(dir, "rOldFound", 10 * Ttl, out _, out _),
            "old found entry (resolvedAt=0) is treated as expired → re-resolve");
        Assert.IsTrue(ReactionTargetCache.TryGet(dir, "rOldNF", 10 * Ttl, out _, out _),
            "old not-found entry stays (exempt from TTL)");
    }
}
