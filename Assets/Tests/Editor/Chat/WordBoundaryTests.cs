using NUnit.Framework;

public class WordBoundaryTests
{
    static (int, int) R(string t, int i) => WordBoundary.WordRangeAt(t, i);

    [Test] public void Latin_word_selected_from_middle() =>
        Assert.AreEqual((6, 10), R("alpha beta gamma", 8));   // "beta"

    [Test] public void Cyrillic_word_selected() =>
        Assert.AreEqual((0, 6), R("Привет мир", 2));

    [Test] public void Digits_and_underscore_are_word_chars() =>
        Assert.AreEqual((0, 8), R("abc_1234 x", 4));

    [Test] public void Whitespace_returns_caret_placement() =>
        Assert.AreEqual((5, 5), R("alpha beta", 5));

    [Test] public void Punctuation_selects_the_punctuation_run() =>
        Assert.AreEqual((3, 5), R("ab !? cd", 3));

    [Test] public void Apostrophe_stays_inside_word() =>
        Assert.AreEqual((0, 5), R("don't stop", 2));

    [Test] public void Surrogate_pair_never_split()
    {
        var (s, e) = R("hi \U0001F602 yo", 3); // 😂 occupies string indices 3..5
        Assert.AreEqual((3, 5), (s, e));
    }

    [Test] public void Adjacent_emoji_select_as_one_run_v1()
    {
        // Documented v1 behavior: a run of emoji/ZWJ/FE0F selects together.
        var (s, e) = R("x \U0001F602\U0001F44D y", 2);
        Assert.AreEqual((2, 6), (s, e));
    }

    [Test] public void Index_at_text_end_selects_last_word() =>
        Assert.AreEqual((6, 10), R("alpha beta", 10));

    [Test] public void Empty_text_returns_zero_caret() =>
        Assert.AreEqual((0, 0), R("", 0));

    [Test] public void Clamp_moves_off_low_surrogate() =>
        Assert.AreEqual(3, WordBoundary.ClampToCharBoundary("hi \U0001F602", 4));

    [Test] public void Clamp_bounds_negative_and_overflow()
    {
        Assert.AreEqual(0, WordBoundary.ClampToCharBoundary("abc", -5));
        Assert.AreEqual(3, WordBoundary.ClampToCharBoundary("abc", 99));
    }
}
