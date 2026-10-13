using NUnit.Framework;

// The tracker is the one handoff between the panel (what was shown / tapped) and the send path.
// Static state — every test starts from Clear().
public class SendOriginTrackerTests
{
    [SetUp] public void Reset() => SendOriginTracker.Clear();

    [Test]
    public void FreshSet_ReplacesSnapshot_DrillDoesNot()
    {
        SendOriginTracker.SetShown("aaaa", 3, "Первая", fresh: true);
        SendOriginTracker.SetShown("bbbb", 4, "Другая первая", fresh: false);   // a drill round: the fresh #1 stays the shadow answer
        Assert.AreEqual("aaaa", SendOriginTracker.Current.shownKey);
        Assert.AreEqual("Первая", SendOriginTracker.Current.top1Text);
        SendOriginTracker.SetShown("cccc", 2, "Новая", fresh: true);            // a new incoming → new turn
        Assert.AreEqual("cccc", SendOriginTracker.Current.shownKey);
        Assert.IsFalse(SendOriginTracker.Current.HasPick);
    }

    [Test]
    public void PickFromADrillRound_KeepsItsOwnKey_ButTop1FromTheFreshSet()
    {
        SendOriginTracker.SetShown("aaaa", 3, "Первая", fresh: true);
        SendOriginTracker.SetShown("bbbb", 4, "Вариант первой", fresh: false);
        SendOriginTracker.SetPick("bbbb", 4, 2, "Третий вариант", "Ответ");
        var o = SendOriginTracker.ResolveAndClear("Третий вариант");
        Assert.AreEqual(SendOriginKind.Card, o.kind);
        Assert.AreEqual("bbbb", o.requestKey);
        Assert.AreEqual(4, o.setSize);
        Assert.AreEqual(2, o.index);
        Assert.IsFalse(o.top1Match);
        Assert.IsNull(SendOriginTracker.Current, "a send consumes the snapshot");
    }

    [Test]
    public void PickWithoutSnapshot_CreatesOne()
    {
        SendOriginTracker.SetPick("aaaa", 3, 0, "Текст", "Ответ");   // ‹ after a send restored a round and the owner tapped again
        Assert.IsTrue(SendOriginTracker.Current.HasSet);
        Assert.IsTrue(SendOriginTracker.Current.HasPick);
        Assert.AreEqual(SendOriginKind.Card, SendOriginTracker.ResolveAndClear("Текст").kind);
    }

    [Test]
    public void DrillWithoutSnapshot_HasNoTop1()
    {
        SendOriginTracker.SetShown("bbbb", 4, "Вариант первой", fresh: false);   // ‹ or a drill round after a Clear
        Assert.IsTrue(SendOriginTracker.Current.HasSet);
        Assert.IsNull(SendOriginTracker.Current.top1Text, "a non-fresh set never becomes the shadow answer");
        Assert.IsFalse(SendOriginTracker.ResolveAndClear("Вариант первой").top1Match);
    }

    [Test]
    public void HidingThePanel_DropsAShownSet_ButKeepsATappedCard()
    {
        SendOriginTracker.SetShown("aaaa", 3, "x", fresh: true);
        Assert.IsFalse(SendOriginTracker.Current.HasPick, "no tap yet — the controller may Clear on hide");
        SendOriginTracker.SetPick("aaaa", 3, 1, "Текст", "Ответ");
        Assert.IsTrue(SendOriginTracker.Current.HasPick, "a tapped card is already in the composer — the controller must keep it on hide");
    }

    [Test]
    public void IgnoresEmptyKeysAndTexts()
    {
        SendOriginTracker.SetShown(null, 3, "x", fresh: true);
        SendOriginTracker.SetShown("aaaa", 0, "x", fresh: true);
        Assert.IsNull(SendOriginTracker.Current);
        SendOriginTracker.SetShown("aaaa", 3, "x", fresh: true);
        SendOriginTracker.SetPick("aaaa", 3, 1, "", "Ответ");
        Assert.IsFalse(SendOriginTracker.Current.HasPick);
    }

    [Test]
    public void PreferenceCounter_OnlyCardAndEditedWithAValidMove()
    {
        Assert.IsTrue(SuggestionPickStats.CountsAsPreference(new SendOrigin { kind = SendOriginKind.Card, pickedMove = "Ответ" }));
        Assert.IsTrue(SuggestionPickStats.CountsAsPreference(new SendOrigin { kind = SendOriginKind.Edited, pickedMove = "Уточнить" }));
        Assert.IsFalse(SuggestionPickStats.CountsAsPreference(new SendOrigin { kind = SendOriginKind.Typed, pickedMove = "Ответ" }));
        Assert.IsFalse(SuggestionPickStats.CountsAsPreference(new SendOrigin { kind = SendOriginKind.Card, pickedMove = "Цена" }));
        Assert.IsFalse(SuggestionPickStats.CountsAsPreference(null));
    }
}
