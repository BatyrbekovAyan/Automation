using NUnit.Framework;

// EditMode coverage for the learning-A send classifier (pure C#, no Unity). The snapshot is what the
// panel showed this turn (fresh set) plus the card the owner tapped (any round); the resolver names
// the send: card (sent as tapped) / edited (tapped, then changed) / typed (set shown, nothing tapped)
// / none (no set). top1Match compares against the FRESH set's card #1 — the shadow «Авто» answer.
public class SendOriginResolverTests
{
    private static PickSnapshot Shown(string top1 = "Здравствуйте! Есть в наличии, 1500 тг.")
        => new PickSnapshot { shownKey = "0123456789abcdef", shownSize = 3, top1Text = top1 };

    private static PickSnapshot Picked(int index, string text, string move = "Ответ")
    {
        var s = Shown();
        s.pickKey = s.shownKey; s.pickSetSize = 3; s.pickIndex = index; s.pickText = text; s.pickMove = move;
        return s;
    }

    [Test]
    public void NoSnapshot_OrNoSet_IsNone()
    {
        var o = SendOriginResolver.Resolve("привет", null);
        Assert.AreEqual(SendOriginKind.None, o.kind);
        Assert.AreEqual("none", o.KindWire);
        Assert.IsNull(o.requestKey);
        Assert.AreEqual(SendOriginKind.None, SendOriginResolver.Resolve("привет", new PickSnapshot()).kind);
    }

    [Test]
    public void SetShown_NothingTapped_IsTyped_WithTop1Judged()
    {
        var o = SendOriginResolver.Resolve("Добрый день, да, есть", Shown());
        Assert.AreEqual(SendOriginKind.Typed, o.kind);
        Assert.AreEqual(-1, o.index);
        Assert.AreEqual("0123456789abcdef", o.requestKey);
        Assert.AreEqual(3, o.setSize);
        Assert.IsFalse(o.top1Match);

        var same = SendOriginResolver.Resolve("здравствуйте! есть в наличии, 1500 тг", Shown());
        Assert.AreEqual(SendOriginKind.Typed, same.kind, "typed verbatim card #1 is still typed (no tap) — but top1 matches");
        Assert.IsTrue(same.top1Match);
    }

    [Test]
    public void TappedAndSentAsIs_IsCard_EditZero()
    {
        var o = SendOriginResolver.Resolve("Уточните, пожалуйста, год выпуска?", Picked(1, "Уточните, пожалуйста, год выпуска?", "Уточнить"));
        Assert.AreEqual(SendOriginKind.Card, o.kind);
        Assert.AreEqual(1, o.index);
        Assert.AreEqual(0, o.editPercent);
        Assert.AreEqual("Уточнить", o.pickedMove);
        Assert.IsFalse(o.top1Match, "card #2 was sent, not card #1");
    }

    [Test]
    public void TappedThenChanged_IsEdited_WithPercent()
    {
        var o = SendOriginResolver.Resolve("Уточните год выпуска и модель?", Picked(1, "Уточните, пожалуйста, год выпуска?", "Уточнить"));
        Assert.AreEqual(SendOriginKind.Edited, o.kind);
        Assert.Greater(o.editPercent, 0);
        Assert.LessOrEqual(o.editPercent, 100);
    }

    [Test]
    public void Card1TappedAndSent_TopMatches()
    {
        var o = SendOriginResolver.Resolve("Здравствуйте! Есть в наличии, 1500 тг.", Picked(0, "Здравствуйте! Есть в наличии, 1500 тг."));
        Assert.AreEqual(SendOriginKind.Card, o.kind);
        Assert.IsTrue(o.top1Match);
    }

    [Test]
    public void Normalize_TrimsLowersCollapsesAndStripsEdgePunctuation()
    {
        Assert.AreEqual("есть в наличии", SendOriginResolver.Normalize("  Есть   в наличии!!! "));
        Assert.AreEqual("", SendOriginResolver.Normalize(null));
        Assert.AreEqual("а 1500 тг", SendOriginResolver.Normalize("…а 1500 тг."));
    }

    [Test]
    public void Similarity_IsOneMinusLevenshteinOverMaxLength()
    {
        Assert.AreEqual(1.0, SendOriginResolver.Similarity("abc", "ABC "), 1e-9);
        Assert.AreEqual(1.0, SendOriginResolver.Similarity("", ""), 1e-9);
        Assert.AreEqual(0.0, SendOriginResolver.Similarity("abc", ""), 1e-9);
        Assert.AreEqual(1.0 - 1.0 / 4.0, SendOriginResolver.Similarity("kitn", "kite"), 1e-9);
        Assert.AreEqual(3, SendOriginResolver.Levenshtein("kitten", "sitting"));
    }
}
