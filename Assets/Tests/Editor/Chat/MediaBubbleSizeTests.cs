using NUnit.Framework;
using UnityEngine;

public class MediaBubbleSizeTests
{
    private const float Tolerance = 1f; // px tolerance (rounding-safe)

    [Test]
    public void Landscape16x9_FillsLandscapeWidth()
    {
        Vector2 size = MediaBubbleSize.Resolve(1.78f);
        Assert.AreEqual(810f, size.x, Tolerance);
        Assert.AreEqual(455f, size.y, Tolerance); // 810 / 1.78
    }

    [Test]
    public void Landscape4x3_UsesWiderLandscapeWidth()
    {
        // Any landscape (aspect > 1) gets the wider width, not the portrait one.
        Vector2 size = MediaBubbleSize.Resolve(1.3333f);
        Assert.AreEqual(810f, size.x, Tolerance);
        Assert.AreEqual(608f, size.y, Tolerance); // 810 / 1.3333 = 607.5
    }

    [Test]
    public void Square_UsesNarrowerPortraitWidth()
    {
        // Square is not "wide content", so it uses the narrower portrait width.
        Vector2 size = MediaBubbleSize.Resolve(1.0f);
        Assert.AreEqual(700f, size.x, Tolerance);
        Assert.AreEqual(700f, size.y, Tolerance);
    }

    [Test]
    public void Portrait3x4_WidthBoundBelowCap()
    {
        // 0.75 is shallower than MinAspect (0.70), so it's width-bound and uncropped.
        Vector2 size = MediaBubbleSize.Resolve(0.75f);
        Assert.AreEqual(700f, size.x, Tolerance);
        Assert.AreEqual(933f, size.y, Tolerance); // 700 / 0.75 = 933.3
    }

    [Test]
    public void PortraitAtMinAspect_IsTallestUncropped()
    {
        // The tallest portrait bubble: aspect exactly at the clamp edge.
        Vector2 size = MediaBubbleSize.Resolve(0.70f);
        Assert.AreEqual(700f, size.x, Tolerance);
        Assert.AreEqual(1000f, size.y, Tolerance); // 700 / 0.70 = 1000
    }

    [Test]
    public void Portrait9x16_ClampedToMinAspectAndCropped()
    {
        // 9:16 (0.5625) is taller than MinAspect (0.70), so it clamps to 0.70 and the
        // caller center-crops the frame — bubble is the full 700 x 1000.
        Vector2 size = MediaBubbleSize.Resolve(0.5625f);
        Assert.AreEqual(700f, size.x, Tolerance);
        Assert.AreEqual(1000f, size.y, Tolerance);
    }

    [Test]
    public void Panorama_ClampedToMaxAspect()
    {
        Vector2 size = MediaBubbleSize.Resolve(3.0f); // wider than 16:9 -> clamp 1.78 (landscape)
        Assert.AreEqual(810f, size.x, Tolerance);
        Assert.AreEqual(455f, size.y, Tolerance);
    }

    [Test]
    public void VeryTall_ClampedToMinAspect()
    {
        Vector2 size = MediaBubbleSize.Resolve(0.3f); // taller than 0.70 -> clamp 0.70 (portrait)
        Assert.AreEqual(700f, size.x, Tolerance);
        Assert.AreEqual(1000f, size.y, Tolerance);
    }

    [Test]
    public void NonPositiveOrNonFinite_TreatedAsSquare()
    {
        Assert.AreEqual(700f, MediaBubbleSize.Resolve(0f).x, Tolerance);
        Assert.AreEqual(700f, MediaBubbleSize.Resolve(-2f).x, Tolerance);
        Assert.AreEqual(700f, MediaBubbleSize.Resolve(float.NaN).y, Tolerance);
    }

    [Test]
    public void OrientedAspect_NoRotation_Unchanged()
    {
        Assert.AreEqual(1.78f, MediaBubbleSize.OrientedAspect(1.78f, 0f), 0.001f);
    }

    [Test]
    public void OrientedAspect_Rotated90_Inverts()
    {
        Assert.AreEqual(1f / 1.78f, MediaBubbleSize.OrientedAspect(1.78f, 90f), 0.001f);
    }

    [Test]
    public void OrientedAspect_Rotated270_Inverts()
    {
        Assert.AreEqual(1f / 0.5625f, MediaBubbleSize.OrientedAspect(0.5625f, 270f), 0.001f);
    }

    [Test]
    public void OrientedAspect_Rotated180_Unchanged()
    {
        Assert.AreEqual(1.5f, MediaBubbleSize.OrientedAspect(1.5f, 180f), 0.001f);
    }

    [Test]
    public void OrientedAspect_NonPositiveRaw_ReturnsSquare()
    {
        Assert.AreEqual(1f, MediaBubbleSize.OrientedAspect(0f, 90f), 0.001f);
        Assert.AreEqual(1f, MediaBubbleSize.OrientedAspect(-3f, 0f), 0.001f);
    }
}
