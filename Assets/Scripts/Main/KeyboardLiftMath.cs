using System;

/// <summary>
/// Pure geometry for lifting a focused field clear of the on-screen keyboard.
/// Kept free of UnityEngine types so the decisions are unit-testable without a
/// runtime (same convention as ScrollFabMath / OnboardingPageMath).
///
/// All Y values are canvas units measured from the CANVAS BOTTOM.
/// </summary>
public static class KeyboardLiftMath
{
    /// <summary>
    /// Converts an occluded screen height (device pixels) into canvas units.
    ///
    /// safeAreaBottomPx is a parameter rather than a baked-in
    /// Screen.safeArea.y read because it is only correct for callers whose
    /// canvas is inset to the safe area. On a ScreenSpaceOverlay canvas the
    /// canvas rect IS the full screen — canvas y=0 is the physical screen
    /// bottom, the same origin the keyboard height is measured from — so
    /// subtracting the inset there would under-lift by the home-bar height.
    /// Overlay callers pass 0.
    /// </summary>
    public static float ScreenPxToCanvas(
        float occludedPx,
        float safeAreaBottomPx,
        bool isOverlay,
        float scaleFactor,
        float canvasHeight,
        float screenHeight)
    {
        if (occludedPx <= 0f) return 0f;

        var adjusted = Math.Max(0f, occludedPx - Math.Max(0f, safeAreaBottomPx));
        if (adjusted <= 0f) return 0f;

        if (isOverlay)
        {
            // Guard: a zero/negative scaleFactor would yield Infinity/NaN and
            // fling the field off-screen.
            return scaleFactor > 0f ? adjusted / scaleFactor : 0f;
        }

        if (screenHeight <= 0f || canvasHeight <= 0f) return 0f;
        return adjusted * (canvasHeight / screenHeight);
    }

    /// <summary>
    /// How far to raise a field so its bottom clears the keyboard.
    ///
    /// fieldBottomAtRestY — the field's bottom edge with no lift applied.
    /// keyboardTopY       — top edge of the keyboard (0 when it is down).
    /// clearance          — gap to leave between keyboard top and field bottom.
    /// maxLift            — ceiling, so the raised card cannot be pushed over
    ///                      the header / tab bar. Pass float.MaxValue for none.
    ///
    /// Returns 0 (never negative) when the field is already clear, so a field
    /// that sits above the keyboard is left exactly where it is.
    /// </summary>
    public static float RequiredLift(
        float fieldBottomAtRestY,
        float keyboardTopY,
        float clearance,
        float maxLift)
    {
        // Keyboard down: no lift at all. Without this an already-visible
        // bottom-most field would still rise by `clearance`, which reads on
        // device as a twitch when the keyboard dismisses.
        if (keyboardTopY <= 0f) return 0f;

        var needed = keyboardTopY + clearance - fieldBottomAtRestY;
        if (needed <= 0f) return 0f;

        var ceiling = Math.Max(0f, maxLift);
        return needed < ceiling ? needed : ceiling;
    }
}
