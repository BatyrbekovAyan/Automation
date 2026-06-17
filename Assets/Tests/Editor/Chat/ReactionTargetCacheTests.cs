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
    public void PutThenGet_ReturnsAllFields()
    {
        string dir = FreshDir("putget");
        ReactionTargetCache.Put(dir, "r1", "Hello", "chat", "Bumer", 1000);

        bool ok = ReactionTargetCache.TryGet(dir, "r1", 1000, out string text, out string type, out string name);

        Assert.IsTrue(ok);
        Assert.AreEqual("Hello", text);
        Assert.AreEqual("chat", type);
        Assert.AreEqual("Bumer", name);
    }

    [Test]
    public void Put_WritesFileToDisk()
    {
        string dir = FreshDir("disk");
        ReactionTargetCache.Put(dir, "r1", "Hello", "chat", "Bumer", 1000);

        string path = Path.Combine(dir, "reaction_targets.json");
        Assert.IsTrue(File.Exists(path), "cache file should be written");
        string json = File.ReadAllText(path);
        StringAssert.Contains("r1", json);
        StringAssert.Contains("Bumer", json);
    }

    [Test]
    public void LoadsFromExistingFile_OnFirstAccess()
    {
        string dir = FreshDir("load");
        File.WriteAllText(Path.Combine(dir, "reaction_targets.json"),
            "{\"entries\":[{\"reactionId\":\"r9\",\"text\":\"Persisted\",\"type\":\"image\",\"senderName\":\"Alibek\",\"resolvedAt\":1000}]}");

        bool ok = ReactionTargetCache.TryGet(dir, "r9", 1000, out string text, out string type, out string name);

        Assert.IsTrue(ok);
        Assert.AreEqual("Persisted", text);
        Assert.AreEqual("image", type);
        Assert.AreEqual("Alibek", name);
    }

    [Test]
    public void UnknownId_ReturnsFalse()
    {
        string dir = FreshDir("unknown");
        Assert.IsFalse(ReactionTargetCache.TryGet(dir, "nope", 1000, out _, out _, out _));
    }

    [Test]
    public void SenderNameOnly_NoTargetText_IsStableAndExemptFromTtl()
    {
        // A normal group message: a resolved name but no reaction target — stable, never expires.
        string dir = FreshDir("nameonly");
        ReactionTargetCache.Put(dir, "m1", "", "", "Bumer", 0);

        bool ok = ReactionTargetCache.TryGet(dir, "m1", 100 * Ttl, out string text, out string type, out string name);

        Assert.IsTrue(ok, "name-only entry never expires");
        Assert.AreEqual("", text);
        Assert.AreEqual("", type);
        Assert.AreEqual("Bumer", name);
    }

    [Test]
    public void ResolvedTargetText_ExpiresAfterTtl()
    {
        string dir = FreshDir("ttl");
        ReactionTargetCache.Put(dir, "rE", "Hi", "chat", "Bumer", 1000);

        Assert.IsTrue(ReactionTargetCache.TryGet(dir, "rE", 1000 + Ttl, out _, out _, out _), "fresh within TTL");
        Assert.IsFalse(ReactionTargetCache.TryGet(dir, "rE", 1000 + Ttl + 1, out _, out _, out _), "stale past TTL");
    }

    [Test]
    public void EvictsOldestBeyondCapacity()
    {
        string dir = FreshDir("cap");
        // 501 entries (cap = 500) with increasing resolvedAt; r0 is the oldest and must evict.
        for (int i = 0; i <= 500; i++)
            ReactionTargetCache.Put(dir, "r" + i, "t", "chat", "", i);

        Assert.IsFalse(ReactionTargetCache.TryGet(dir, "r0", 500, out _, out _, out _), "oldest evicted past cap");
        Assert.IsTrue(ReactionTargetCache.TryGet(dir, "r1", 500, out _, out _, out _), "second-oldest retained");
        Assert.IsTrue(ReactionTargetCache.TryGet(dir, "r500", 500, out _, out _, out _), "newest retained");
    }

    [Test]
    public void OldSchemaFile_TargetExpires_NameOnlyStays()
    {
        // Pre-senderName/pre-resolvedAt schema loads with those fields defaulted ("" / 0).
        string dir = FreshDir("oldschema");
        File.WriteAllText(Path.Combine(dir, "reaction_targets.json"),
            "{\"entries\":[" +
            "{\"reactionId\":\"rOldText\",\"text\":\"Hi\",\"type\":\"chat\"}," +
            "{\"reactionId\":\"rOldName\",\"text\":\"\",\"type\":\"\",\"senderName\":\"Bumer\"}]}");

        Assert.IsFalse(ReactionTargetCache.TryGet(dir, "rOldText", 10 * Ttl, out _, out _, out _),
            "old entry with target text (resolvedAt=0) is treated as expired");
        Assert.IsTrue(ReactionTargetCache.TryGet(dir, "rOldName", 10 * Ttl, out _, out _, out string n),
            "old name-only entry stays (exempt)");
        Assert.AreEqual("Bumer", n);
    }
}
