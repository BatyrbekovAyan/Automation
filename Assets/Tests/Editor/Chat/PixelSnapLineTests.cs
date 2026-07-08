using NUnit.Framework;
using UnityEngine;

public class PixelSnapLineTests
{
    [Test]
    public void ToUnityAxis_Height_MapsToVertical()
    {
        Assert.AreEqual(RectTransform.Axis.Vertical,
            PixelSnapLine.ToUnityAxis(PixelSnapLine.SnapAxis.Height));
    }

    [Test]
    public void ToUnityAxis_Width_MapsToHorizontal()
    {
        Assert.AreEqual(RectTransform.Axis.Horizontal,
            PixelSnapLine.ToUnityAxis(PixelSnapLine.SnapAxis.Width));
    }
}
