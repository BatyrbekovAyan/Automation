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

    // ---- Keycap emoji (#️⃣ *️⃣ 0️⃣–9️⃣) -----------------------------------------
    // These are an ASCII base (# * or 0-9) + optional FE0F + U+20E3 (combining
    // enclosing keycap). The base is a normal text char, so detection must be
    // contextual: only a base actually followed by U+20E3 is an emoji.

    [Test]
    public void Convert_HashKeycap_EmitsSpriteTag_NoMissingFlag()
    {
        // '#' = U+0023, FE0F, U+20E3 → Twemoji name "0023-fe0f-20e3"
        EmojiSpriteRegistry.BuildFromNames(new[] { "0023-fe0f-20e3" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("#️⃣", out bool hasMissing);

        StringAssert.Contains("<sprite name=\"0023-fe0f-20e3\">", result);
        Assert.IsFalse(hasMissing);
    }

    [Test]
    public void Convert_StarKeycap_EmitsSpriteTag()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "002a-fe0f-20e3" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("*️⃣", out _);

        StringAssert.Contains("<sprite name=\"002a-fe0f-20e3\">", result);
    }

    [Test]
    public void Convert_DigitKeycap_EmitsSpriteTag()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "0031-fe0f-20e3" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("1️⃣", out _);

        StringAssert.Contains("<sprite name=\"0031-fe0f-20e3\">", result);
    }

    [Test]
    public void Convert_KeycapWithoutVariationSelector_EmitsCanonicalSpriteTag()
    {
        // Sender omitted FE0F (minimally-qualified): "#" + U+20E3. The converter
        // must still resolve the fully-qualified registered name.
        EmojiSpriteRegistry.BuildFromNames(new[] { "0023-fe0f-20e3" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("#⃣", out _);

        StringAssert.Contains("<sprite name=\"0023-fe0f-20e3\">", result);
    }

    [Test]
    public void Convert_BareHashStarDigits_NotTreatedAsKeycap()
    {
        // Regression guard: a bare '#', '*' or digit in ordinary text must NEVER be
        // converted to a sprite — the keycap detection is contextual on U+20E3.
        EmojiSpriteRegistry.BuildFromNames(new[] { "0023-fe0f-20e3", "0031-fe0f-20e3", "002a-fe0f-20e3" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("call #5 or *9 at 30", out bool hasMissing);

        StringAssert.DoesNotContain("<sprite", result);
        StringAssert.Contains("#5", result);
        StringAssert.Contains("*9", result);
        StringAssert.Contains("30", result);
        Assert.IsFalse(hasMissing);
    }

    // ---- Lone/unpaired surrogate hardening (WR-01 / D2-view / 08-REVIEW CR-01) ----------
    // A malformed reaction-emoji payload from tapi can carry a lone (unpaired) surrogate.
    // The unguarded char.ConvertToUtf32 walk threw ArgumentException on it, which aborted
    // the OnMessageReactionsChanged multicast AND killed the SyncLatestMessages coroutine
    // mid-loop. The converter must be throw-safe: emit the stray surrogate raw and advance.
    // The converter prepends a zero-width space (U+200B), so assert Contains, not equality.

    [Test]
    public void Convert_LoneHighSurrogate_DoesNotThrow_Passthrough()
    {
        string result = null;
        Assert.DoesNotThrow(() =>
            result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("\uD83D", MissingEmojiMode.Hide));

        StringAssert.Contains("\uD83D", result);
    }

    [Test]
    public void Convert_LoneLowSurrogateMidString_DoesNotThrow()
    {
        // A lone LOW surrogate wedged between two BMP letters.
        string result = null;
        Assert.DoesNotThrow(() =>
            result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("a\uDC00b", MissingEmojiMode.Hide));

        StringAssert.Contains("a", result);
        StringAssert.Contains("b", result);
    }

    [Test]
    public void Convert_LoneLowSurrogateAtStart_DoesNotThrow()
    {
        // A lone low surrogate at index 0 — the walk must not throw entering the loop.
        Assert.DoesNotThrow(() =>
            UnicodeEmojiConverter.ConvertRealEmojisToSprites("\uDC00", MissingEmojiMode.Hide));
    }

    [Test]
    public void Convert_ValidSurrogatePairEmoji_StillConverts_AfterGuard()
    {
        // Regression guard: the lone-surrogate guard must NOT skip a VALID surrogate pair
        // (😀 = U+1F600 = high + low). The grinning face still resolves to its sprite tag.
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("😀", MissingEmojiMode.Hide);

        StringAssert.Contains("sprite", result);
    }
}
