using NUnit.Framework;

public class SelectionActionsTests
{
    [Test] public void Copy_returns_selected_substring_regardless_of_direction()
    {
        Assert.AreEqual("beta", SelectionActions.CopyText("alpha beta gamma", 6, 10));
        Assert.AreEqual("beta", SelectionActions.CopyText("alpha beta gamma", 10, 6));
    }

    [Test] public void Cut_removes_selection_and_places_caret_at_start()
    {
        var e = SelectionActions.Cut("alpha beta gamma", 6, 10);
        Assert.AreEqual("alpha  gamma", e.NewText);
        Assert.AreEqual(6, e.NewCaret);
    }

    [Test] public void Paste_replaces_selection()
    {
        var e = SelectionActions.Paste("alpha beta gamma", 6, 10, "ZZ", 0);
        Assert.AreEqual("alpha ZZ gamma", e.NewText);
        Assert.AreEqual(8, e.NewCaret);
    }

    [Test] public void Paste_with_collapsed_selection_inserts_at_caret()
    {
        var e = SelectionActions.Paste("ab", 1, 1, "XY", 0);
        Assert.AreEqual("aXYb", e.NewText);
        Assert.AreEqual(3, e.NewCaret);
    }

    [Test] public void Paste_respects_character_limit_by_truncating_clip()
    {
        var e = SelectionActions.Paste("12345", 5, 5, "abcdef", 8);
        Assert.AreEqual("12345abc", e.NewText);
        Assert.AreEqual(8, e.NewCaret);
    }

    [Test] public void Paste_truncation_never_splits_a_surrogate_pair()
    {
        var e = SelectionActions.Paste("", 0, 0, "a\U0001F602", 2); // room for 2 units; 😂 needs both at index 1..3
        Assert.AreEqual("a", e.NewText);
    }

    [Test] public void Paste_null_clipboard_is_empty()
    {
        var e = SelectionActions.Paste("ab", 0, 1, null, 0);
        Assert.AreEqual("b", e.NewText);
        Assert.AreEqual(0, e.NewCaret);
    }

    [Test] public void Indices_are_clamped_into_range()
    {
        var e = SelectionActions.Cut("abc", -4, 99);
        Assert.AreEqual("", e.NewText);
        Assert.AreEqual(0, e.NewCaret);
    }
}
