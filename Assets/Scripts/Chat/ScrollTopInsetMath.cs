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
    /// ScrollRect corrects an out-of-range position only while NOBODY is touching it (its
    /// settle is gated on !m_Dragging), and then eases into it — so a viewport that grows back
    /// (keyboard/panel closing) while the user sits at the very top would leave the thread
    /// visibly drifting off its own end without this. That easing is also why the clamp must
    /// stand down whenever the ScrollRect owns the position — see ShouldClampContent.
    /// </summary>
    public static float ClampContentY(float contentY, float contentHeight, float viewportHeight)
    {
        var maxDown = Math.Max(0f, contentHeight - viewportHeight);
        if (contentY < -maxDown) return -maxDown;
        return contentY > 0f ? 0f : contentY;
    }

    /// <summary>
    /// Whether the compensator may write content.anchoredPosition after a viewport resize. It may
    /// not while the ScrollRect owns that position — and ownership outlasts the finger.
    /// <para>
    /// While DRAGGING, the ScrollRect deliberately holds the content OUTSIDE the legal range
    /// (elastic overscroll), which is exactly the range <see cref="ClampContentY"/> forces it back
    /// into: clamping there does not correct the position, it tears the rubber band off under the
    /// finger. While SETTLING — inertia, or the elastic ease back from an overscroll — it is easing
    /// into that same range itself, one frame at a time, and a clamp cuts the spring into a pop.
    /// </para>
    /// <para>
    /// Both halves are required, and the second is the one that is easy to miss: the drag flag
    /// clears on pointer-up, one frame BEFORE the ease begins, so a drag-only guard still pops on
    /// release. Skipping costs nothing either way, because the settle is gated on «not dragging» —
    /// the very event that re-opens this guard is the one that hands the correction to ScrollRect.
    /// </para>
    /// <para>
    /// None of this was reachable until the slot inset started moving DURING a gesture (the thread
    /// pull-down, 2026-08-19); before that the compensator only ever ran with nobody touching the
    /// scroll. Device symptom, short thread scrolled to the top: the stretch collapsed and the
    /// messages snapped upward the instant the pull-down engaged, then stretched again once the
    /// slot bottomed out and the inset stopped changing.
    /// </para>
    /// </summary>
    public static bool ShouldClampContent(bool scrollIsDragging, bool scrollIsSettling)
        => !scrollIsDragging && !scrollIsSettling;
}
