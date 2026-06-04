using UnityEngine;

/// <summary>
/// Pure sizing math for image/video message bubbles. WhatsApp-style: landscape media
/// fills a wider max width, portrait media a narrower one (so tall content isn't
/// overwhelming); height follows from the clamped aspect. Aspect = width / height
/// (>1 landscape, <1 portrait). Media wider than MaxAspect or taller than MinAspect is
/// sized to the clamp edge; the caller (ApplyTextureAspectFill) center-crops the extreme.
/// </summary>
public static class MediaBubbleSize
{
    public const float MaxWidthLandscape = 810f;  // aspect > 1 — fills more width (WhatsApp shows landscape wide)
    public const float MaxWidthPortrait  = 700f;  // aspect <= 1 — narrower so tall portraits aren't overwhelming
    public const float MinAspect = 0.70f;  // ~7:10 — taller is center-cropped (WhatsApp trims tall clips)
    public const float MaxAspect = 1.78f;  // 16:9 — wider is center-cropped

    public static Vector2 Resolve(float aspect)
    {
        if (!float.IsFinite(aspect) || aspect <= 0f) aspect = 1f;
        aspect = Mathf.Clamp(aspect, MinAspect, MaxAspect);

        // Landscape fills the wider max width; portrait (and square) the narrower one.
        // Height follows from the clamped aspect, so the clamp also bounds how tall a
        // portrait bubble gets (MaxWidthPortrait / MinAspect = 1000).
        float width = aspect > 1f ? MaxWidthLandscape : MaxWidthPortrait;
        return new Vector2(width, width / aspect);
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
