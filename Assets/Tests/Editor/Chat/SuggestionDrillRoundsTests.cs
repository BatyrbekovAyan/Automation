using NUnit.Framework;

// EditMode coverage for the 2026-08-18 drill-rounds redesign seams. Grows across the
// rollout tasks: SuggestionMoves here, ComposeHeaderTitle (panel) and
// ResolvePickStatsMove (controller) appended by their own tasks.
public class SuggestionDrillRoundsTests
{
    [Test]
    public void IsMove_AcceptsAllSixMoves()
    {
        Assert.AreEqual(6, SuggestionMoves.All.Length);
        foreach (string move in SuggestionMoves.All)
            Assert.IsTrue(SuggestionMoves.IsMove(move), move);
    }

    [Test]
    public void IsMove_RejectsNullEmptyFreeFormAndWrongCase()
    {
        Assert.IsFalse(SuggestionMoves.IsMove(null));
        Assert.IsFalse(SuggestionMoves.IsMove(""));
        Assert.IsFalse(SuggestionMoves.IsMove("Цена"));      // free-form title, not a move
        Assert.IsFalse(SuggestionMoves.IsMove("ответ"));     // case-sensitive: server enum is exact
    }

    // --- ComposeHeaderTitle (panel header, pure) ---

    [Test]
    public void ComposeHeaderTitle_NullOrBlank_IsTheDefaultOverline()
    {
        Assert.AreEqual(SuggestionsPanel.DefaultHeaderTitle, SuggestionsPanel.ComposeHeaderTitle(null));
        Assert.AreEqual(SuggestionsPanel.DefaultHeaderTitle, SuggestionsPanel.ComposeHeaderTitle("   "));
    }

    [Test]
    public void ComposeHeaderTitle_UppercasesCyrillicAndTrims()
    {
        Assert.AreEqual("ЦЕНА", SuggestionsPanel.ComposeHeaderTitle(" Цена "));
        Assert.AreEqual("СО СКИДКОЙ", SuggestionsPanel.ComposeHeaderTitle("Со скидкой"));
    }

    [Test]
    public void ComposeHeaderTitle_SlicesARoguePayload()
    {
        string composed = SuggestionsPanel.ComposeHeaderTitle(new string('ы', 40));
        Assert.AreEqual(26, composed.Length);
        StringAssert.EndsWith("…", composed);
    }

    // --- ResolvePickStatsMove (preference learning under free-form titles) ---

    [Test]
    public void ResolvePickStats_PrefersTheMoveField()
    {
        var picked = new SuggestionItem { text = "т", intentLabel = "Коротко", move = "Ответ" };
        Assert.AreEqual("Ответ", SuggestionsController.ResolvePickStatsMove(picked));
    }

    [Test]
    public void ResolvePickStats_LegacyServer_FallsBackToAnEnumLabel()
    {
        var picked = new SuggestionItem { text = "т", intentLabel = "К заказу", move = null };
        Assert.AreEqual("К заказу", SuggestionsController.ResolvePickStatsMove(picked));
    }

    [Test]
    public void ResolvePickStats_FreeFormTitleWithoutMove_RecordsNothing()
    {
        var picked = new SuggestionItem { text = "т", intentLabel = "Со скидкой", move = "" };
        Assert.IsNull(SuggestionsController.ResolvePickStatsMove(picked));
        Assert.IsNull(SuggestionsController.ResolvePickStatsMove(null));
    }
}
