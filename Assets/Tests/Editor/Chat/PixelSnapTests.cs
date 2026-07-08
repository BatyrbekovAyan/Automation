using NUnit.Framework;
using UnityEngine;

public class PixelSnapTests
{
    [Test]
    public void SnapPx_UnitScale_ReturnsSameUnit()
    {
        Assert.AreEqual(1f, PixelSnap.SnapPx(1f, 1f), 1e-4f);
    }

    [Test]
    public void SnapPx_HighDensity_SnapsToWholePixelCount()
    {
        // 1 unit * 3 = 3px -> 3/3 = 1 unit (represents exactly 3 physical px)
        Assert.AreEqual(1f, PixelSnap.SnapPx(1f, 3f), 1e-4f);
    }

    [Test]
    public void SnapPx_FractionalScale_RoundsToNearestPixel()
    {
        // 2 * 1.33 = 2.66 -> round 3 -> 3 / 1.33
        Assert.AreEqual(3f / 1.33f, PixelSnap.SnapPx(2f, 1.33f), 1e-4f);
    }

    [Test]
    public void SnapPx_ResultIsAlwaysWholePhysicalPixels()
    {
        foreach (var sf in new[] { 1f, 1.33f, 2f, 2.625f, 3f, 3.5f })
        foreach (var u in new[] { 1f, 2f, 3f })
        {
            float units = PixelSnap.SnapPx(u, sf);
            float px = units * sf;
            Assert.AreEqual(Mathf.Round(px), px, 1e-3f, $"sf={sf} u={u} -> {px}px not whole");
            Assert.GreaterOrEqual(px, 1f - 1e-3f, $"sf={sf} u={u} under 1px");
        }
    }

    [Test]
    public void SnapPx_SubPixelDesign_ClampsToOnePixel()
    {
        // 0.4 * 1 = 0.4 -> round 0 -> max(1,0) = 1 -> 1px
        Assert.AreEqual(1f, PixelSnap.SnapPx(0.4f, 1f), 1e-4f);
    }

    [Test]
    public void SnapPx_InvalidScale_ReturnsDesignUnchanged()
    {
        Assert.AreEqual(1f, PixelSnap.SnapPx(1f, 0f), 1e-4f);
        Assert.AreEqual(2f, PixelSnap.SnapPx(2f, -3f), 1e-4f);
    }
}
