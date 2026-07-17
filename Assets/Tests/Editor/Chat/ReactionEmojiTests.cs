using NUnit.Framework;

/// <summary>
/// D2 root cause A (emoji-FORM mismatch): tapi echoes a reaction in the BASE
/// (variation-selector-free) form — the heart as U+2764 "❤" — while the app's quick-bar and
/// <see cref="ReactionEmojiCatalog"/> store the FULLY-QUALIFIED form U+2764 U+FE0F "❤️".
/// <see cref="ReactionEmoji"/> is the single canonical seam that makes the two forms
/// compare-equal (<see cref="ReactionEmoji.CompareKey"/> / <see cref="ReactionEmoji.SameEmoji"/>)
/// and requalifies the base echo to the sprite-renderable display form
/// (<see cref="ReactionEmoji.Canonical"/>). Pure — no scene, no MonoBehaviour.
/// </summary>
public class ReactionEmojiTests
{
    private const string BaseHeart = "❤";              // ❤  (no variation selector)
    private const string QualifiedHeart = "❤️";   // ❤️ (VS16-qualified)

    // Heart-on-fire — the ONE Telegram-catalog entry whose FE0F sits MID-sequence
    // (U+2764 U+FE0F U+200D U+1F525, selector before the joiner), which a trailing-only strip
    // missed (08-REVIEW WR-04). Composed from the annotated heart constants + an explicit ZWJ
    // so the invisible difference between the two forms stays visible in review.
    private static readonly string Zwj = ((char)0x200D).ToString();
    private static readonly string BaseHeartOnFire = BaseHeart + Zwj + "🔥";           // U+2764 U+200D U+1F525
    private static readonly string QualifiedHeartOnFire = QualifiedHeart + Zwj + "🔥"; // U+2764 U+FE0F U+200D U+1F525

    [Test]
    public void SameEmoji_BaseAndQualifiedHeart_AreEqual()
    {
        Assert.IsTrue(ReactionEmoji.SameEmoji(BaseHeart, QualifiedHeart));
        Assert.IsTrue(ReactionEmoji.SameEmoji(QualifiedHeart, BaseHeart));
    }

    [Test]
    public void SameEmoji_DifferentEmoji_AreNotEqual()
    {
        Assert.IsFalse(ReactionEmoji.SameEmoji("👍", QualifiedHeart));
    }

    [Test]
    public void CompareKey_DropsTrailingVariationSelector()
    {
        Assert.AreEqual(BaseHeart, ReactionEmoji.CompareKey(QualifiedHeart));
        Assert.AreEqual(BaseHeart, ReactionEmoji.CompareKey(BaseHeart));   // idempotent
    }

    [Test]
    public void Canonical_BaseHeart_RequalifiesToDisplayForm()
    {
        // Turns tapi's base ❤ into the sprite-renderable ❤️ (the TMP reaction sprite name
        // includes -fe0f; a stripped base form would render a literal text heart).
        Assert.AreEqual(QualifiedHeart, ReactionEmoji.Canonical(BaseHeart));
        Assert.AreEqual(QualifiedHeart, ReactionEmoji.Canonical(QualifiedHeart));   // idempotent
    }

    [Test]
    public void SameEmoji_BaseAndQualifiedHeartOnFire_AreEqual()
    {
        // tapi echoes ZWJ reactions in the FE0F-less base form too; the mid-sequence selector
        // must strip or every D2 symptom (double count, stale pill, text glyph) recurs here.
        Assert.IsTrue(ReactionEmoji.SameEmoji(BaseHeartOnFire, QualifiedHeartOnFire));
        Assert.IsTrue(ReactionEmoji.SameEmoji(QualifiedHeartOnFire, BaseHeartOnFire));
    }

    [Test]
    public void CompareKey_DropsMidSequenceVariationSelector()
    {
        Assert.AreEqual(BaseHeartOnFire, ReactionEmoji.CompareKey(QualifiedHeartOnFire));
        Assert.AreEqual(BaseHeartOnFire, ReactionEmoji.CompareKey(BaseHeartOnFire));   // idempotent
    }

    [Test]
    public void Canonical_BaseHeartOnFire_RequalifiesToDisplayForm()
    {
        // BuildRequalify keys by CompareKey, so the full strip also fixes requalification: the
        // base echo comes back as the sprite-renderable qualified catalog form (the TMP sprite
        // name includes -fe0f), never as a literal text glyph.
        Assert.AreEqual(QualifiedHeartOnFire, ReactionEmoji.Canonical(BaseHeartOnFire));
        Assert.AreEqual(QualifiedHeartOnFire, ReactionEmoji.Canonical(QualifiedHeartOnFire));   // idempotent
    }

    [Test]
    public void Canonical_EmojiWithNoQualifiedForm_ReturnedUnchanged()
    {
        Assert.AreEqual("🔥", ReactionEmoji.Canonical("🔥"));   // no FE0F form in the set
        Assert.AreEqual("👍", ReactionEmoji.Canonical("👍"));
    }

    [Test]
    public void CanonicalAndCompareKey_NullAndEmpty_PassThrough()
    {
        Assert.IsNull(ReactionEmoji.Canonical(null));
        Assert.AreEqual("", ReactionEmoji.Canonical(""));
        Assert.IsNull(ReactionEmoji.CompareKey(null));
        Assert.AreEqual("", ReactionEmoji.CompareKey(""));
        Assert.IsTrue(ReactionEmoji.SameEmoji(null, null));
    }
}
