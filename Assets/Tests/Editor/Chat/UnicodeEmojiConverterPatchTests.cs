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
    public void Convert_PendingEmoji_KeepsRaw_SetsMissingFlag()
    {
        // A pending emoji (fetch in flight) must NOT emit a sprite tag — TMP renders
        // a tag with no matching sprite as literal "<sprite name=...>" text. The raw
        // Unicode is kept and the missing flag tells the caller to re-convert when
        // OnEmojiReady fires.
        EmojiSpriteRegistry.MarkPending("1faea");

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("🫪", out bool hasMissing);

        StringAssert.DoesNotContain("<sprite", result);
        StringAssert.Contains("🫪", result);
        Assert.IsTrue(hasMissing);
    }

    [Test]
    public void ConvertHide_KnownEmoji_StillEmitsSpriteTag()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites(
            "😀", MissingEmojiMode.Hide, out bool hasMissing);

        StringAssert.Contains("<sprite name=\"1f600\">", result);
        Assert.IsFalse(hasMissing);
    }

    [Test]
    public void ConvertHide_UnknownEmoji_OmittedEntirely_SetsMissingFlag()
    {
        // Display surfaces use Hide mode: an emoji with no sprite is dropped from
        // the output entirely — no raw Unicode tofu, no literal tag text.
        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites(
            "привет 🫪", MissingEmojiMode.Hide, out bool hasMissing);

        StringAssert.DoesNotContain("<sprite", result);
        StringAssert.DoesNotContain("🫪", result);
        StringAssert.Contains("привет", result);
        Assert.IsTrue(hasMissing);
    }

    [Test]
    public void ConvertHide_PendingEmoji_OmittedEntirely_SetsMissingFlag()
    {
        EmojiSpriteRegistry.MarkPending("1faea");

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites(
            "🫪", MissingEmojiMode.Hide, out bool hasMissing);

        StringAssert.DoesNotContain("<sprite", result);
        StringAssert.DoesNotContain("🫪", result);
        Assert.IsTrue(hasMissing);
    }

    [Test]
    public void ConvertHide_MixedString_TagForKnown_NothingForUnknown()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites(
            "😀🫪", MissingEmojiMode.Hide, out bool hasMissing);

        StringAssert.Contains("<sprite name=\"1f600\">", result);
        StringAssert.DoesNotContain("🫪", result);
        Assert.IsTrue(hasMissing);
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
