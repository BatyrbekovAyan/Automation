using System.Collections.Generic;
using NUnit.Framework;

public class ScrollFabMathTests
{
    private static List<float> Tops(params float[] ys) => new List<float>(ys);

    [Test]
    public void AllBelowFold_CountsAll()
    {
        Assert.AreEqual(3, ScrollFabMath.CountBelowFold(Tops(-10f, -20f, -30f), 0f));
    }

    [Test]
    public void AllVisible_CountsZero()
    {
        Assert.AreEqual(0, ScrollFabMath.CountBelowFold(Tops(10f, 20f, 30f), 0f));
    }

    [Test]
    public void Partial_CountsOnlyBelow()
    {
        Assert.AreEqual(2, ScrollFabMath.CountBelowFold(Tops(-5f, 5f, -15f, 25f), 0f));
    }

    [Test]
    public void BoundaryExactlyAtFold_NotCounted()
    {
        // top exactly at the viewport bottom is "at" the fold, not below it
        Assert.AreEqual(1, ScrollFabMath.CountBelowFold(Tops(0f, -1f), 0f));
    }

    [Test]
    public void PositiveViewportBottom_ComparesCorrectly()
    {
        Assert.AreEqual(2, ScrollFabMath.CountBelowFold(Tops(140f, 160f, 100f), 150f));
    }

    [Test]
    public void EmptyList_CountsZero()
    {
        Assert.AreEqual(0, ScrollFabMath.CountBelowFold(new List<float>(), 0f));
    }

    [Test]
    public void NullList_CountsZero()
    {
        Assert.AreEqual(0, ScrollFabMath.CountBelowFold(null, 0f));
    }
}
