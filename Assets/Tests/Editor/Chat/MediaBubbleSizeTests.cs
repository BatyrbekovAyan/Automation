using NUnit.Framework;
using UnityEngine;

public class MediaBubbleSizeTests
{
    private const float Tolerance = 1f; // px tolerance (rounding-safe)

    [Test]
    public void Landscape16x9_FillsBoxWidth()
    {
        Vector2 size = MediaBubbleSize.Resolve(1.78f);
        Assert.AreEqual(810f, size.x, Tolerance);
        Assert.AreEqual(455f, size.y, Tolerance);
    }

    [Test]
    public void Square_FillsBoxWidthAndIsSquare()
    {
        Vector2 size = MediaBubbleSize.Resolve(1.0f);
        Assert.AreEqual(810f, size.x, Tolerance);
        Assert.AreEqual(810f, size.y, Tolerance);
    }

    [Test]
    public void Portrait3x4_ExactlyAtHeightCap()
    {
        Vector2 size = MediaBubbleSize.Resolve(0.75f);
        Assert.AreEqual(810f, size.x, Tolerance);
        Assert.AreEqual(1080f, size.y, Tolerance);
    }

    [Test]
    public void Portrait9x16_IsHeightBoundAndNarrower()
    {
        Vector2 size = MediaBubbleSize.Resolve(0.5625f);
        Assert.AreEqual(607.5f, size.x, Tolerance);
        Assert.AreEqual(1080f, size.y, Tolerance);
    }

    [Test]
    public void Panorama_ClampedToMaxAspect()
    {
        Vector2 size = MediaBubbleSize.Resolve(3.0f); // wider than 16:9 -> clamp 1.78
        Assert.AreEqual(810f, size.x, Tolerance);
        Assert.AreEqual(455f, size.y, Tolerance);
    }

    [Test]
    public void VeryTall_ClampedToMinAspect()
    {
        Vector2 size = MediaBubbleSize.Resolve(0.3f); // taller than 9:16 -> clamp 0.56
        Assert.AreEqual(604.8f, size.x, Tolerance);
        Assert.AreEqual(1080f, size.y, Tolerance);
    }

    [Test]
    public void NonPositiveOrNonFinite_TreatedAsSquare()
    {
        Assert.AreEqual(810f, MediaBubbleSize.Resolve(0f).x, Tolerance);
        Assert.AreEqual(810f, MediaBubbleSize.Resolve(-2f).x, Tolerance);
        Assert.AreEqual(810f, MediaBubbleSize.Resolve(float.NaN).y, Tolerance);
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
