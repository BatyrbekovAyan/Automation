using System.Collections.Generic;
using NUnit.Framework;

public class ThumbnailKeyResolverTests
{
    private static System.Func<string, bool> CachedSet(params string[] keys)
    {
        var set = new HashSet<string>(keys);
        return set.Contains;
    }

    [Test]
    public void PopulatedThumbnailUrl_ReturnedUnchanged()
    {
        // A healthy pointer wins even if the reconstructed keys would also be cached.
        string r = ThumbnailKeyResolver.Resolve("base64://abc", "A", CachedSet("vthumb://A", "thumb://A"));
        Assert.AreEqual("base64://abc", r);
    }

    [Test]
    public void EmptyPointer_PrefersVthumbOverThumb()
    {
        string r = ThumbnailKeyResolver.Resolve("", "A", CachedSet("vthumb://A", "thumb://A"));
        Assert.AreEqual("vthumb://A", r);
    }

    [Test]
    public void EmptyPointer_FallsBackToThumbWhenOnlyThatIsCached()
    {
        string r = ThumbnailKeyResolver.Resolve("", "A", CachedSet("thumb://A"));
        Assert.AreEqual("thumb://A", r);
    }

    [Test]
    public void EmptyPointer_NothingCached_ReturnsNull()
    {
        string r = ThumbnailKeyResolver.Resolve("", "A", CachedSet());
        Assert.IsNull(r);
    }

    [Test]
    public void EmptyMessageId_ReturnsNull()
    {
        Assert.IsNull(ThumbnailKeyResolver.Resolve("", "", CachedSet("thumb://A")));
        Assert.IsNull(ThumbnailKeyResolver.Resolve("", null, CachedSet("thumb://A")));
    }

    [Test]
    public void NullIsCachedPredicate_WithEmptyPointer_ReturnsNull()
    {
        Assert.IsNull(ThumbnailKeyResolver.Resolve("", "A", null));
    }

    // --- VideoThumbKey (05-06-REVIEW WR-02): TG message ids are 1-5 digit counters that
    // repeat across accounts/chats, so the Telegram key namespaces by profile + chat while
    // the WhatsApp key stays the legacy global form (existing WA caches remain valid). ---

    [Test]
    public void VideoThumbKey_WhatsApp_LegacyGlobalKey_ByteIdentical()
    {
        Assert.AreEqual("vthumb://3EB0ABC123",
            VideoThumbKey.For(ChatChannel.WhatsApp, "prof-1", "79995579399@c.us", "3EB0ABC123"));
    }

    [Test]
    public void VideoThumbKey_Telegram_NamespacedByProfileAndChat()
    {
        Assert.AreEqual("vthumb://tg/prof-1/555/17",
            VideoThumbKey.For(ChatChannel.Telegram, "prof-1", "555", "17"));
    }

    [Test]
    public void VideoThumbKey_Telegram_SameMessageIdDifferentChatOrProfile_DistinctKeys()
    {
        // The exact WR-02 collisions: one account's channel post vs private chat sharing a
        // numeric id, and two TG bots (different accounts) sharing a numeric id.
        string channelPost = VideoThumbKey.For(ChatChannel.Telegram, "prof-1", "111", "5");
        string privateChat = VideoThumbKey.For(ChatChannel.Telegram, "prof-1", "222", "5");
        string otherBot    = VideoThumbKey.For(ChatChannel.Telegram, "prof-2", "111", "5");

        Assert.AreNotEqual(channelPost, privateChat);
        Assert.AreNotEqual(channelPost, otherBot);
    }

    [Test]
    public void VideoThumbKey_Telegram_NullParts_Deterministic()
    {
        Assert.AreEqual("vthumb://tg///5", VideoThumbKey.For(ChatChannel.Telegram, null, null, "5"));
    }
}
