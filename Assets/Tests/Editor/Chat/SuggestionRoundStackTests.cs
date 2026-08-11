using System.Collections.Generic;
using NUnit.Framework;

// EditMode coverage for SuggestionRoundStack — the pure history behind the suggestions
// back button (flow decision 2026-08-11: each pick moves a round FORWARD in the chosen
// direction; back restores the previous round's cards instantly, with NO LLM call).
// Pure C#: the controller owns the lifecycle (push on pick, pop on back, clear on any
// fresh round); this seam pins LIFO order, the null-render no-op, and the depth cap.
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
        Assert.IsFalse(stack.TryPop(out _, out _));
    }

    [Test]
    public void PushNullResult_IsNoOp()
    {
        // A pick can land while nothing is rendered (skeleton) — there is no round to return to.
        var stack = new SuggestionRoundStack();
        stack.Push(null, "направление");
        Assert.IsFalse(stack.CanGoBack);
    }

    [Test]
    public void PushThenPop_RestoresSameResultAndSteer()
    {
        var stack = new SuggestionRoundStack();
        var round1 = Set("раунд 1");
        stack.Push(round1, null);   // round 1 has no steer — it was the fresh set
        Assert.IsTrue(stack.CanGoBack);
        Assert.IsTrue(stack.TryPop(out var result, out var steer));
        Assert.AreSame(round1, result);
        Assert.IsNull(steer);
        Assert.IsFalse(stack.CanGoBack);
    }

    [Test]
    public void Pop_IsLifo_DeeperRoundsComeBackFirst()
    {
        var stack = new SuggestionRoundStack();
        stack.Push(Set("раунд 1"), null);
        stack.Push(Set("раунд 2"), "направление А");
        Assert.IsTrue(stack.TryPop(out var second, out var secondSteer));
        Assert.AreEqual("раунд 2", second.items[0].text);
        Assert.AreEqual("направление А", secondSteer);
        Assert.IsTrue(stack.TryPop(out var first, out var firstSteer));
        Assert.AreEqual("раунд 1", first.items[0].text);
        Assert.IsNull(firstSteer);
    }

    [Test]
    public void DepthCap_DropsTheOldestRound()
    {
        var stack = new SuggestionRoundStack();
        for (int i = 1; i <= SuggestionRoundStack.MaxDepth + 1; i++)
            stack.Push(Set("раунд " + i), "s" + i);
        Assert.AreEqual(SuggestionRoundStack.MaxDepth, stack.Count);
        // Pop everything — the deepest restorable round is 2 (round 1 was dropped).
        SuggestionResult last = null;
        while (stack.TryPop(out var r, out _)) last = r;
        Assert.AreEqual("раунд 2", last.items[0].text);
    }

    [Test]
    public void Clear_DropsEverything()
    {
        var stack = new SuggestionRoundStack();
        stack.Push(Set("раунд 1"), null);
        stack.Push(Set("раунд 2"), "x");
        stack.Clear();
        Assert.IsFalse(stack.CanGoBack);
        Assert.AreEqual(0, stack.Count);
    }
}
