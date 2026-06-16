using UnityEngine;

/// Pure math for landing a target bubble ~40% down the viewport when jumping to a
/// quoted original. verticalNormalizedPosition: 1 = top, 0 = bottom.
public static class ScrollTargetMath
{
    public static float CenteredNormalizedPosition(float distanceFromTop, float viewportHeight, float scrollableHeight)
    {
        if (scrollableHeight <= 1f) return 0f;
        float target = Mathf.Clamp(distanceFromTop - viewportHeight * 0.4f, 0f, scrollableHeight);
        return 1f - target / scrollableHeight;
    }
}
