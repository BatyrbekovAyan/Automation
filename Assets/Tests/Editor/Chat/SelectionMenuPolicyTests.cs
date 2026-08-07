using NUnit.Framework;

public class SelectionMenuPolicyTests
{
    [Test] public void Selection_with_clipboard_shows_everything_when_not_all_selected() =>
        Assert.AreEqual(
            SelectionMenuItems.Cut | SelectionMenuItems.Copy | SelectionMenuItems.Paste | SelectionMenuItems.SelectAll,
            SelectionMenuPolicy.Visible(hasSelection: true, clipboardHasText: true, textLength: 10, allSelected: false, readOnly: false));

    [Test] public void All_selected_hides_select_all() =>
        Assert.IsFalse(SelectionMenuPolicy.Visible(true, true, 10, allSelected: true, readOnly: false)
            .HasFlag(SelectionMenuItems.SelectAll));

    [Test] public void Caret_only_with_clipboard_shows_paste_and_select_all() =>
        Assert.AreEqual(SelectionMenuItems.Paste | SelectionMenuItems.SelectAll,
            SelectionMenuPolicy.Visible(false, true, 10, false, false));

    [Test] public void Caret_only_empty_clipboard_empty_text_shows_nothing() =>
        Assert.AreEqual(SelectionMenuItems.None,
            SelectionMenuPolicy.Visible(false, false, 0, false, false));

    [Test] public void ReadOnly_hides_cut_and_paste_keeps_copy() =>
        Assert.AreEqual(SelectionMenuItems.Copy | SelectionMenuItems.SelectAll,
            SelectionMenuPolicy.Visible(true, true, 10, false, readOnly: true));
}
