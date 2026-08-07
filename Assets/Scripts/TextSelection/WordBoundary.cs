/// Pure string math for iOS-style word selection. All indices are STRING
/// indices (UTF-16 code units). Guarantee: no returned boundary ever splits
/// a surrogate pair. v1 emoji rule: a maximal run of {surrogates, ZWJ,
/// FE0F} is one cluster (adjacent emoji select together — pinned by test).
public static class WordBoundary
{
    public static int ClampToCharBoundary(string text, int index)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (index < 0) index = 0;
        if (index > text.Length) index = text.Length;
        if (index > 0 && index < text.Length && char.IsLowSurrogate(text[index]))
            index--;
        return index;
    }

    public static (int start, int end) WordRangeAt(string text, int index)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);
        index = ClampToCharBoundary(text, index);
        if (index >= text.Length) index = text.Length - 1;
        if (char.IsLowSurrogate(text[index]) && index > 0) index--;

        char c = text[index];
        if (IsEmojiPart(c)) return RunAt(text, index, IsEmojiPart);
        if (char.IsWhiteSpace(c)) return (index, index);
        if (IsWordChar(c)) return RunAt(text, index, IsWordChar);
        return RunAt(text, index, IsPunct);
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '\'';
    static bool IsEmojiPart(char c) => char.IsSurrogate(c) || c == '\u200D' || c == '\uFE0F'; // ZWJ, variation selector
    static bool IsPunct(char c) => !IsWordChar(c) && !char.IsWhiteSpace(c) && !IsEmojiPart(c);

    static (int, int) RunAt(string text, int index, System.Func<char, bool> inRun)
    {
        int start = index;
        while (start > 0 && inRun(text[start - 1])) start--;
        int end = index;
        while (end < text.Length && inRun(text[end])) end++;
        return (start, end);
    }
}
