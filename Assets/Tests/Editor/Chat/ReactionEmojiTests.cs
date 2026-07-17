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
