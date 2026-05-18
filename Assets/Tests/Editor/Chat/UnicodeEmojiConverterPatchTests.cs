using NUnit.Framework;

/// <summary>
/// Tests for the registry-aware behaviour added to UnicodeEmojiConverter.
/// These complement (do not replace) any existing converter tests.
/// </summary>
public class UnicodeEmojiConverterPatchTests
{
    [SetUp]
    public void SetUp() => EmojiSpriteRegistry.Reset();

    [Test]
    public void Convert_KnownEmoji_EmitsSpriteTag_NoMissingFlag()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("😀", out bool hasMissing);

        StringAssert.Contains("<sprite name=\"1f600\">", result);
        Assert.IsFalse(hasMissing);
    }

    [Test]
    public void Convert_UnknownEmoji_LeavesRawUnicode_SetsMissingFlag()
    {
        // Registry empty — 🫪 (1faea) is unknown
        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("🫪", out bool hasMissing);

        StringAssert.DoesNotContain("<sprite", result);
        StringAssert.Contains("🫪", result);
        Assert.IsTrue(hasMissing);
    }

    [Test]
    public void Convert_MixedString_SpriteTagForKnown_RawUnicodeForUnknown()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("😀🫪", out bool hasMissing);

        StringAssert.Contains("<sprite name=\"1f600\">", result);
        StringAssert.Contains("🫪", result);
        Assert.IsTrue(hasMissing);
    }

    [Test]
    public void Convert_AllKnown_MissingFlagFalse()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600", "1f44b" });

        UnicodeEmojiConverter.ConvertRealEmojisToSprites("😀👋", out bool hasMissing);

        Assert.IsFalse(hasMissing);
    }

    [Test]
    public void Convert_EmptyRegistry_AllEmojiLeaveRawUnicode()
    {
        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("😀👋", out bool hasMissing);

        StringAssert.DoesNotContain("<sprite", result);
        Assert.IsTrue(hasMissing);
    }

    [Test]
    public void Convert_PendingEmoji_EmitsSpriteTag_NoMissingFlag()
    {
        // A pending emoji (fetch in flight) should emit the sprite tag so TMP can
        // find it once the background download registers the sprite asset.
        EmojiSpriteRegistry.MarkPending("1faea");

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("🫪", out bool hasMissing);

        StringAssert.Contains("<sprite name=\"1faea\">", result);
        Assert.IsFalse(hasMissing);
    }

    [Test]
    public void Convert_UnknownEmoji_ClearsFailedState()
    {
        // When an emoji is re-encountered after a failed fetch, ClearFailed should
        // reset it so the next RequestEmoji call can queue a retry.
        EmojiSpriteRegistry.MarkFailed("1faea");

        UnicodeEmojiConverter.ConvertRealEmojisToSprites("🫪", out _);

        Assert.IsFalse(EmojiSpriteRegistry.IsFailed("1faea"));
    }
}
