using NUnit.Framework;

public class EmojiSpriteRegistryTests
{
    [SetUp]
    public void SetUp() => EmojiSpriteRegistry.Reset();

    [Test]
    public void Build_PopulatesKnownSet()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600", "1f44b" });
        Assert.IsTrue(EmojiSpriteRegistry.IsKnown("1f600"));
        Assert.IsTrue(EmojiSpriteRegistry.IsKnown("1f44b"));
    }

    [Test]
    public void Build_ClearsPreviousState()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });
        EmojiSpriteRegistry.MarkPending("1f600");
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f601" });
        Assert.IsFalse(EmojiSpriteRegistry.IsKnown("1f600"));
        Assert.IsFalse(EmojiSpriteRegistry.IsPending("1f600"));
    }

    [Test]
    public void UnknownName_IsNotKnown()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });
        Assert.IsFalse(EmojiSpriteRegistry.IsKnown("1faea"));
    }

    [Test]
    public void MarkPending_ThenRegister_MovesToKnown()
    {
        EmojiSpriteRegistry.MarkPending("1faea");
        Assert.IsTrue(EmojiSpriteRegistry.IsPending("1faea"));
        Assert.IsFalse(EmojiSpriteRegistry.IsKnown("1faea"));

        EmojiSpriteRegistry.Register("1faea");
        Assert.IsTrue(EmojiSpriteRegistry.IsKnown("1faea"));
        Assert.IsFalse(EmojiSpriteRegistry.IsPending("1faea"));
    }

    [Test]
    public void MarkFailed_ClearsPending()
    {
        EmojiSpriteRegistry.MarkPending("1faea");
        EmojiSpriteRegistry.MarkFailed("1faea");
        Assert.IsFalse(EmojiSpriteRegistry.IsPending("1faea"));
        Assert.IsTrue(EmojiSpriteRegistry.IsFailed("1faea"));
    }

    [Test]
    public void ClearFailed_AllowsRetry()
    {
        EmojiSpriteRegistry.MarkPending("1faea");
        EmojiSpriteRegistry.MarkFailed("1faea");
        EmojiSpriteRegistry.ClearFailed("1faea");
        Assert.IsFalse(EmojiSpriteRegistry.IsFailed("1faea"));
        Assert.IsFalse(EmojiSpriteRegistry.IsPending("1faea"));
    }

    [Test]
    public void Register_ClearsFailedFlag()
    {
        EmojiSpriteRegistry.MarkPending("1faea");
        EmojiSpriteRegistry.MarkFailed("1faea");
        EmojiSpriteRegistry.Register("1faea");
        Assert.IsFalse(EmojiSpriteRegistry.IsFailed("1faea"));
        Assert.IsTrue(EmojiSpriteRegistry.IsKnown("1faea"));
    }
}
