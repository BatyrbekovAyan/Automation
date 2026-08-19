using NUnit.Framework;

public class UnreadSeparatorViewTests
{
    [TestCase(1,   "1 НЕПРОЧИТАННОЕ СООБЩЕНИЕ")]
    [TestCase(2,   "2 НЕПРОЧИТАННЫХ СООБЩЕНИЯ")]
    [TestCase(3,   "3 НЕПРОЧИТАННЫХ СООБЩЕНИЯ")]
    [TestCase(4,   "4 НЕПРОЧИТАННЫХ СООБЩЕНИЯ")]
    [TestCase(0,   "0 НЕПРОЧИТАННЫХ СООБЩЕНИЙ")]
    [TestCase(5,   "5 НЕПРОЧИТАННЫХ СООБЩЕНИЙ")]
    // 11..14 take the "many" form even though they end in 1..4.
    [TestCase(11,  "11 НЕПРОЧИТАННЫХ СООБЩЕНИЙ")]
    [TestCase(12,  "12 НЕПРОЧИТАННЫХ СООБЩЕНИЙ")]
    [TestCase(14,  "14 НЕПРОЧИТАННЫХ СООБЩЕНИЙ")]
    [TestCase(21,  "21 НЕПРОЧИТАННОЕ СООБЩЕНИЕ")]
    [TestCase(22,  "22 НЕПРОЧИТАННЫХ СООБЩЕНИЯ")]
    [TestCase(25,  "25 НЕПРОЧИТАННЫХ СООБЩЕНИЙ")]
    [TestCase(99,  "99 НЕПРОЧИТАННЫХ СООБЩЕНИЙ")]
    [TestCase(101, "101 НЕПРОЧИТАННОЕ СООБЩЕНИЕ")]
    [TestCase(111, "111 НЕПРОЧИТАННЫХ СООБЩЕНИЙ")]
    public void FormatLabel_Pluralizes(int count, string expected)
    {
        Assert.AreEqual(expected, UnreadSeparatorView.FormatLabel(count));
    }
}
