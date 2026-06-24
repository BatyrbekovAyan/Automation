using System.Collections.Generic;
using NUnit.Framework;

public class MockSuggestionsProviderTests
{
    // The locked RU intent-label set (D-14 / UI-SPEC Copywriting Contract).
    private static readonly HashSet<string> Labels = new HashSet<string>
    {
        "Приветствие", "Цена", "Наличие", "Запись", "Вежливый отказ"
    };

    private MockSuggestionsProvider _provider;

    // runner=null => BuildResult is exercised with no latency coroutine.
    [SetUp]
    public void SetUp() => _provider = new MockSuggestionsProvider(null);

    private static SuggestionRequest Req(long seq = 1, string steer = null)
        => new SuggestionRequest { chatId = "c1@c.us", requestSeq = seq, steerTowardText = steer };

    [Test]
    public void FreshRequest_ReturnsFourRankedOkItems()
    {
        var result = _provider.BuildResult(Req());
        Assert.AreEqual(SuggestionStatus.Ok, result.status);
        Assert.AreEqual(4, result.items.Count);
        Assert.AreEqual("Приветствие", result.items[0].intentLabel); // best-first recommended lead
    }

    [Test]
    public void FreshSet_ContainsOneDeliberatelyLongReply()
    {
        var items = _provider.BuildResult(Req()).items;
        Assert.IsTrue(items.Exists(i => i.text.Length > 120),
            "Expected at least one reply > 120 chars for the PANEL-06 truncation demo.");
    }

    [Test]
    public void EveryItem_HasIntentLabelFromTheRussianSet()
    {
        foreach (var item in _provider.BuildResult(Req()).items)
        {
            Assert.IsFalse(string.IsNullOrEmpty(item.intentLabel));
            Assert.IsTrue(Labels.Contains(item.intentLabel), $"Unexpected intent label: {item.intentLabel}");
        }
    }

    [Test]
    public void SteeredRequest_ReturnsDifferentOrderedSet()
    {
        string unsteeredLead = _provider.BuildResult(Req()).items[0].text;
        string steeredLead = _provider.BuildResult(Req(steer: "Да, товар есть в наличии.")).items[0].text;
        Assert.AreNotEqual(unsteeredLead, steeredLead);
    }

    [Test]
    public void ErrorMode_ReturnsErrorStatusWithNoItems()
    {
        _provider.simulateError = true;
        var result = _provider.BuildResult(Req());
        Assert.AreEqual(SuggestionStatus.Error, result.status);
        Assert.IsTrue(result.items == null || result.items.Count == 0);
    }

    [Test]
    public void EmptyMode_ReturnsEmptyStatusWithEmptyItems()
    {
        _provider.simulateEmpty = true;
        var result = _provider.BuildResult(Req());
        Assert.AreEqual(SuggestionStatus.Empty, result.status);
        Assert.AreEqual(0, result.items.Count);
    }

    [Test]
    public void RequestSeq_IsEchoedBack()
    {
        Assert.AreEqual(42L, _provider.BuildResult(Req(seq: 42)).requestSeq);
    }

    [Test]
    public void OutOfOrderMode_CanEmitAStaleLowerSeq()
    {
        // Explicit forced echo: a late reply correlating to an older request.
        _provider.forcedEchoSeq = 1;
        Assert.AreEqual(1L, _provider.BuildResult(Req(seq: 5)).requestSeq);

        // The simpler one-behind toggle also produces a lower-than-issued seq.
        var fresh = new MockSuggestionsProvider(null) { simulateOutOfOrder = true };
        Assert.AreEqual(4L, fresh.BuildResult(Req(seq: 5)).requestSeq);
    }
}
