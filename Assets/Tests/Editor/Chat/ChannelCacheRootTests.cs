using NUnit.Framework;

// Covers ChannelCachePath.SubDir — the pure channel→cache-subdir mapping that
// ChatManager.GetCacheRoot uses. WhatsApp keeps the legacy root (empty sub-dir,
// byte-identical, no migration); Telegram nests one hardcoded constant sub-dir
// so a dual-channel bot no longer collides chats.json (CHAT-11 / T-0502-02).
public class ChannelCacheRootTests
{
    [Test]
    public void WhatsApp_UsesLegacyRoot_EmptySubDir()
        => Assert.AreEqual(string.Empty, ChannelCachePath.SubDir(ChatChannel.WhatsApp));

    [Test]
    public void Telegram_NestsInTelegramSubDir()
        => Assert.AreEqual("telegram", ChannelCachePath.SubDir(ChatChannel.Telegram));

    [Test]
    public void TelegramSubDir_MatchesTheConstant()
        => Assert.AreEqual(ChannelCachePath.TelegramSubDir, ChannelCachePath.SubDir(ChatChannel.Telegram));

    // The two channels never share a cache sub-dir — the isolation guarantee.
    [Test]
    public void Channels_ResolveToDistinctSubDirs()
        => Assert.AreNotEqual(ChannelCachePath.SubDir(ChatChannel.WhatsApp),
                              ChannelCachePath.SubDir(ChatChannel.Telegram));

    // The channel segment is a hardcoded constant — no path-traversal component
    // (T-0502-03): no separators, no dot-dot.
    [Test]
    public void TelegramSubDir_IsASafeConstantSegment()
    {
        string subDir = ChannelCachePath.SubDir(ChatChannel.Telegram);
        Assert.IsFalse(subDir.Contains("/"), "sub-dir must not contain a path separator");
        Assert.IsFalse(subDir.Contains("\\"), "sub-dir must not contain a path separator");
        Assert.IsFalse(subDir.Contains(".."), "sub-dir must not contain a parent-dir traversal");
    }
}
