using NUnit.Framework;

public class UnreadSeparatorViewTests
{
    [TestCase(1,  "1 UNREAD MESSAGE")]
    [TestCase(2,  "2 UNREAD MESSAGES")]
    [TestCase(3,  "3 UNREAD MESSAGES")]
    [TestCase(0,  "0 UNREAD MESSAGES")]
    [TestCase(99, "99 UNREAD MESSAGES")]
    public void FormatLabel_Pluralizes(int count, string expected)
    {
        Assert.AreEqual(expected, UnreadSeparatorView.FormatLabel(count));
    }
}
