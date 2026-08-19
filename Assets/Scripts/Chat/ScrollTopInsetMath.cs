using System;

/// <summary>
/// Pure geometry for keeping the messages scroll usable while the MovingArea rides a bottom
/// inset (native keyboard or a keyboard-slot tenant like the suggestions panel).
/// Kept free of UnityEngine types so the rules are unit-testable without a runtime
/// (same convention as KeyboardLiftMath / ScrollFabMath).
/// </summary>
public static class ScrollTopInsetMath
{
    /// <summary>
    /// The scroll's top offset (RectTransform.offsetMax.y, a negative top inset) while the
    /// MovingArea is risen by <paramref name="appliedInset"/> canvas units. Pulling the top
    /// edge DOWN by exactly the rise pins it to its rest screen position, so the scrollable
    /// range grows to match what the rise hid — without this the top `inset` units of
    /// history clamp above the screen and can never be scrolled into view.
    /// A negative applied inset (SmoothDamp overshoot below rest) never grows the viewport.
    /// </summary>
    public static float TrimmedTopOffset(float restTopOffset, float appliedInset)
        => restTopOffset - Math.Max(0f, appliedInset);

    /// <summary>
    /// Clamps the content's anchoredPosition.y back into the valid scroll range after the
    /// viewport was resized. Messages-list convention: content bottom-anchored and
    /// bottom-pivoted, y = 0 pinned to the viewport bottom (newest), scrolled up = negative.
    /// ScrollRect only corrects an out-of-range position during a drag, so a viewport that
    /// grows back (keyboard/panel closing) while the user sits at the very top would leave
    /// the thread parked past its own end without this.
    /// </summary>
    /// <summary>
    /// Whether the compensator may clamp the content after a viewport resize. It may not while a
    /// finger owns the scroll: during a drag the ScrollRect itself owns content.anchoredPosition and
    /// deliberately allows ELASTIC OVERSCROLL, which is by definition outside the very range
    /// <see cref="ClampContentY"/> enforces — so clamping there does not correct the position, it
    /// tears the rubber band away under the finger.
    /// <para>
    /// This was invisible until the slot inset started moving DURING a gesture (the thread
    /// pull-down, 2026-08-19). Device symptom, short thread scrolled to the top: the stretch
    /// collapses and the messages snap upward the instant the pull-down engages, then stretch again
    /// the moment the slot bottoms out and the inset stops changing. The window is exactly "the
    /// inset is moving", because that is the only time the compensator runs at all.
    /// </para>
    /// <para>
    /// Skipping is safe in both directions. A pull-down only ever GROWS the reachable range (its
    /// ceiling is the height it engaged at, so it can never push the inset past where it already
    /// was), and on release ScrollRect's own elasticity settles the content against the new
    /// viewport. The clamp still runs for every non-drag resize — the keyboard or the panel opening
    /// and closing — which is the case it was written for.
    /// </para>
    /// </summary>
    public static bool ShouldClampContent(bool scrollIsDragging) => !scrollIsDragging;

    public static float ClampContentY(float contentY, float contentHeight, float viewportHeight)
    {
        var maxDown = Math.Max(0f, contentHeight - viewportHeight);
        if (contentY < -maxDown) return -maxDown;
        return contentY > 0f ? 0f : contentY;
    }
}
