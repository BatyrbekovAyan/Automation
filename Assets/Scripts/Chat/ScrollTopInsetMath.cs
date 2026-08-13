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
    public static float ClampContentY(float contentY, float contentHeight, float viewportHeight)
    {
        var maxDown = Math.Max(0f, contentHeight - viewportHeight);
        if (contentY < -maxDown) return -maxDown;
        return contentY > 0f ? 0f : contentY;
    }
}
