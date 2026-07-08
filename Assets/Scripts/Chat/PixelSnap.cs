using UnityEngine;

public static class PixelSnap
{
    public static float SnapPx(float designUnits, float scaleFactor)
    {
        if (scaleFactor <= 0f) return designUnits;
        float px = Mathf.Max(1f, Mathf.Round(designUnits * scaleFactor));
        return px / scaleFactor;
    }

    public static float SnapUnits(float designUnits, Canvas canvas)
    {
        if (canvas == null) return designUnits;
        return SnapPx(designUnits, canvas.rootCanvas.scaleFactor);
    }
}
