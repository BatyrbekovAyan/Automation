using UnityEngine;

/// <summary>
/// Pure sizing math for image/video message bubbles. WhatsApp-style: fit the media's
/// (clamped) aspect ratio into one MaxWidth × MaxHeight bounding box, preserving
/// proportion. Aspect = width / height (>1 landscape, <1 portrait). Media wider than
/// MaxAspect or taller than MinAspect is sized to the clamp edge; the caller
/// (ApplyTextureAspectFill) center-crops it to that same clamped ratio.
/// </summary>
public static class MediaBubbleSize
{
    public const float MaxWidth  = 810f;   // box width  (~0.75 × 1080 ref canvas)
    public const float MaxHeight = 1080f;  // box height (portrait cap)
    public const float MinAspect = 0.56f;  // 9:16 — taller is center-cropped
    public const float MaxAspect = 1.78f;  // 16:9 — wider is center-cropped

    public static Vector2 Resolve(float aspect)
    {
        if (!float.IsFinite(aspect) || aspect <= 0f) aspect = 1f;
        aspect = Mathf.Clamp(aspect, MinAspect, MaxAspect);

        float width  = MaxWidth;
        float height = width / aspect;

        if (height > MaxHeight)         // portrait taller than the box → height-bound
        {
            height = MaxHeight;
            width  = height * aspect;   // narrower, taller bubble
        }

        return new Vector2(width, height);
    }

    /// <summary>
    /// Converts a raw frame aspect (width / height of the decoded/stored frame) into the
    /// aspect as displayed, accounting for a quarter-turn rotation flag. A phone portrait
    /// clip is stored as a landscape frame plus rotation = 90/270; its displayed aspect is
    /// the inverse. 0 and 180 leave the aspect unchanged.
    /// </summary>
    public static float OrientedAspect(float rawAspect, float rotationDegrees)
    {
        if (!float.IsFinite(rawAspect) || rawAspect <= 0f) return 1f;
        bool quarterTurned = (rotationDegrees == 90f || rotationDegrees == 270f);
        return quarterTurned ? 1f / rawAspect : rawAspect;
    }
}
