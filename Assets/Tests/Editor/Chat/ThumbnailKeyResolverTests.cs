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
}
