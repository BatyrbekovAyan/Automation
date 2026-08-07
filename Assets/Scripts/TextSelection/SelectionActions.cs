/// Pure cut/copy/paste string math. Inputs are STRING indices in either
/// order (anchor/focus); outputs are the full new text + caret so the
/// caller can apply them through the focused field's own .text write-through
/// path and then KeyboardSelectionSync.Push.
public readonly struct SelectionEdit
{
    public readonly string NewText;
    public readonly int NewCaret;
    public SelectionEdit(string newText, int newCaret) { NewText = newText; NewCaret = newCaret; }
}

public static class SelectionActions
{
    public static string CopyText(string text, int anchor, int focus)
    {
        var (start, end) = Normalize(text, anchor, focus);
        return text.Substring(start, end - start);
    }

    public static SelectionEdit Cut(string text, int anchor, int focus)
    {
        var (start, end) = Normalize(text, anchor, focus);
        return new SelectionEdit(text.Remove(start, end - start), start);
    }

    public static SelectionEdit Paste(string text, int anchor, int focus, string clip, int characterLimit)
    {
        var (start, end) = Normalize(text, anchor, focus);
        clip = clip ?? "";
        var removed = text.Remove(start, end - start);
        if (characterLimit > 0)
        {
            int room = characterLimit - removed.Length;
            if (room <= 0) clip = "";
            else if (clip.Length > room)
                clip = clip.Substring(0, WordBoundary.ClampToCharBoundary(clip, room));
        }
        return new SelectionEdit(removed.Insert(start, clip), start + clip.Length);
    }

    static (int start, int end) Normalize(string text, int anchor, int focus)
    {
        text = text ?? "";
        int a = WordBoundary.ClampToCharBoundary(text, anchor);
        int f = WordBoundary.ClampToCharBoundary(text, focus);
        return a <= f ? (a, f) : (f, a);
    }
}
