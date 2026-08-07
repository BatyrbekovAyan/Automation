/// iOS-parity visibility rules for the floating edit menu.
[System.Flags]
public enum SelectionMenuItems
{
    None = 0,
    Cut = 1,
    Copy = 2,
    Paste = 4,
    SelectAll = 8,
}

public static class SelectionMenuPolicy
{
    public static SelectionMenuItems Visible(
        bool hasSelection, bool clipboardHasText, int textLength, bool allSelected, bool readOnly)
    {
        var items = SelectionMenuItems.None;
        if (hasSelection && !readOnly) items |= SelectionMenuItems.Cut;
        if (hasSelection) items |= SelectionMenuItems.Copy;
        if (clipboardHasText && !readOnly) items |= SelectionMenuItems.Paste;
        if (textLength > 0 && !allSelected) items |= SelectionMenuItems.SelectAll;
        return items;
    }
}
