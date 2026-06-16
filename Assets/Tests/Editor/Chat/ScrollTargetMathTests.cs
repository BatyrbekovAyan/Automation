using NUnit.Framework;

public class ScrollTargetMathTests
{
    [Test]
    public void ShortContent_ReturnsZero()
        => Assert.AreEqual(0f, ScrollTargetMath.CenteredNormalizedPosition(500f, 800f, 1f));

    [Test]
    public void CentersTarget()
        // target = 1000 - 800*0.4 = 680; 1 - 680/2000 = 0.66
        => Assert.AreEqual(0.66f, ScrollTargetMath.CenteredNormalizedPosition(1000f, 800f, 2000f), 0.001f);

    [Test]
    public void ClampsAboveTop_ReturnsOne()
        => Assert.AreEqual(1f, ScrollTargetMath.CenteredNormalizedPosition(100f, 800f, 2000f), 0.001f);

    [Test]
    public void ClampsBelowBottom_ReturnsZero()
        => Assert.AreEqual(0f, ScrollTargetMath.CenteredNormalizedPosition(99999f, 800f, 2000f), 0.001f);
}
