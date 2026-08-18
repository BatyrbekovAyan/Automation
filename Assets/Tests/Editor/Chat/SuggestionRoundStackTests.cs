using System.Collections.Generic;
using NUnit.Framework;

// EditMode coverage for SuggestionRoundStack — the pure history behind the suggestions
// back button (flow decision 2026-08-11; header added by the 2026-08-18 drill redesign:
// each round remembers the display title it was shown under, so ‹ restores cards AND
// header with no LLM call). Pins LIFO order, the null-render no-op, and the depth cap.
public class SuggestionRoundStackTests
{
    private static SuggestionResult Set(string text)
        => new SuggestionResult
        {
            status = SuggestionStatus.Ok,
            requestSeq = 1,
            items = new List<SuggestionItem> { new SuggestionItem { text = text, intentLabel = "Ответ" } }
        };

    [Test]
    public void Empty_CannotGoBack()
    {
        var stack = new SuggestionRoundStack();
        Assert.IsFalse(stack.CanGoBack);
        Assert.IsFalse(stack.TryPop(out _, out _, out _));
    }

    [Test]
    public void PushNullResult_IsNoOp()
    {
        // A pick can land while nothing is rendered (skeleton) — there is no round to return to.
        var stack = new SuggestionRoundStack();
        stack.Push(null, "направление", "ЦЕНА");
        Assert.IsFalse(stack.CanGoBack);
    }

    [Test]
    public void PushThenPop_RestoresResultSteerAndHeader()
    {
        var stack = new SuggestionRoundStack();
        var round1 = Set("раунд 1");
        stack.Push(round1, null, null);   // round 1: fresh set under the default header
        Assert.IsTrue(stack.CanGoBack);
        Assert.IsTrue(stack.TryPop(out var result, out var steer, out var header));
        Assert.AreSame(round1, result);
        Assert.IsNull(steer);
        Assert.IsNull(header);
        Assert.IsFalse(stack.CanGoBack);
    }

    [Test]
    public void Pop_IsLifo_DeeperRoundsComeBackFirst()
    {
        var stack = new SuggestionRoundStack();
        stack.Push(Set("раунд 1"), null, null);
        stack.Push(Set("раунд 2"), "направление А", "Цена");
        Assert.IsTrue(stack.TryPop(out var second, out var secondSteer, out var secondHeader));
        Assert.AreEqual("раунд 2", second.items[0].text);
        Assert.AreEqual("направление А", secondSteer);
        Assert.AreEqual("Цена", secondHeader);
        Assert.IsTrue(stack.TryPop(out var first, out var firstSteer, out var firstHeader));
        Assert.AreEqual("раунд 1", first.items[0].text);
        Assert.IsNull(firstSteer);
        Assert.IsNull(firstHeader);
    }

    [Test]
    public void DepthCap_DropsTheOldestRound()
    {
        var stack = new SuggestionRoundStack();
        for (int i = 1; i <= SuggestionRoundStack.MaxDepth + 1; i++)
            stack.Push(Set("раунд " + i), "s" + i, "h" + i);
        Assert.AreEqual(SuggestionRoundStack.MaxDepth, stack.Count);
        // Pop everything — the deepest restorable round is 2 (round 1 was dropped).
        SuggestionResult last = null;
        while (stack.TryPop(out var r, out _, out _)) last = r;
        Assert.AreEqual("раунд 2", last.items[0].text);
    }

    [Test]
    public void Clear_DropsEverything()
    {
        var stack = new SuggestionRoundStack();
        stack.Push(Set("раунд 1"), null, null);
        stack.Push(Set("раунд 2"), "x", "y");
        stack.Clear();
        Assert.IsFalse(stack.CanGoBack);
        Assert.AreEqual(0, stack.Count);
    }
}
