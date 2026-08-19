using NUnit.Framework;

public class RuPluralTests
{
    private static string Pages(int n) => RuPlural.Pick(n, "страница", "страницы", "страниц");

    [TestCase(1,   "страница")]
    [TestCase(2,   "страницы")]
    [TestCase(4,   "страницы")]
    [TestCase(5,   "страниц")]
    [TestCase(0,   "страниц")]
    [TestCase(10,  "страниц")]
    // 11..14 take the "many" form even though they end in 1..4 — the rule everyone misses.
    [TestCase(11,  "страниц")]
    [TestCase(12,  "страниц")]
    [TestCase(13,  "страниц")]
    [TestCase(14,  "страниц")]
    [TestCase(15,  "страниц")]
    [TestCase(21,  "страница")]
    [TestCase(22,  "страницы")]
    [TestCase(25,  "страниц")]
    [TestCase(100, "страниц")]
    [TestCase(101, "страница")]
    [TestCase(111, "страниц")]
    [TestCase(121, "страница")]
    public void Pick_AgreesWithCount(int count, string expected)
        => Assert.AreEqual(expected, Pages(count));

    [Test] public void Pick_NegativeUsesMagnitude()
        => Assert.AreEqual("страница", Pages(-1));
}
