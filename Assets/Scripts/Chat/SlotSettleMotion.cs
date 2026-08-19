using UnityEngine;

/// <summary>
/// How long the suggestions slot takes to settle onto its detent after the finger lets go.
/// <para>
/// A fixed duration is what made a release read as a hesitation: the panel arrives at the release
/// point carrying the finger's speed, and a constant-length tween restarts the motion from whatever
/// speed its own curve dictates — so a fast pull stalled and a slow one snapped. Deriving the
/// duration from the distance still to travel and the speed it was travelling at makes the settle a
/// CONTINUATION of the gesture rather than a new animation that happens to end in the same place.
/// </para>
/// <para>
/// The factor of three is not a fudge. A cubic-out tween covers distance D in duration d with an
/// INITIAL speed of 3D/d — the curve spends its speed early. So handing off at the finger's speed v
/// without a visible step means 3D/d = v, i.e. d = 3D/v. Any other ease needs its own factor; this
/// seam is written for cubic-out and the call site must not quietly swap the curve.
/// </para>
/// <para>
/// Both clamps earn their place. Without the floor a hard flick would finish in a couple of frames
/// and read as a cut; without the ceiling a gentle release would crawl for most of a second. The
/// bounds are device-tuning knobs — the RULE is the velocity match.
/// </para>
/// </summary>
public static class SlotSettleMotion
{
    /// <summary>Fastest a settle may be. Below this a flick reads as a cut rather than a motion.</summary>
    public const float MinSeconds = 0.10f;

    /// <summary>Slowest a settle may be, and the answer whenever the release speed is unusable.</summary>
    public const float MaxSeconds = 0.34f;

    /// <summary>The settle for motion that is not continuing a gesture at all — a panel leaving on
    /// its own (answered run, «Авто»), where there is no speed to match and none should be invented.</summary>
    public const float DefaultSeconds = 0.20f;

    /// <summary>Initial-speed factor of a cubic-out curve: it opens at 3× its average speed.</summary>
    private const float CubicOutInitialSpeedFactor = 3f;

    /// <summary>
    /// Seconds for a cubic-out settle across <paramref name="distanceCanvasPx"/> that leaves the
    /// finger at <paramref name="releaseSpeedCanvasPxPerSec"/> — an unsigned SPEED, not a velocity;
    /// direction is already decided by the detent the caller snapped to.
    /// <para>
    /// A speed of zero is a release with no measurable motion, which gets the gentle end of the
    /// range rather than an instant jump. Nothing left to travel gets the floor: the tween still has
    /// to exist (callers hang completion work on it) but must not linger. Garbage in either input
    /// falls back to the neutral default instead of propagating.
    /// </para>
    /// </summary>
    public static float Duration(float distanceCanvasPx, float releaseSpeedCanvasPxPerSec)
    {
        if (!float.IsFinite(distanceCanvasPx) || !float.IsFinite(releaseSpeedCanvasPxPerSec))
            return DefaultSeconds;
        if (distanceCanvasPx <= 0f) return MinSeconds;
        if (releaseSpeedCanvasPxPerSec <= 0f) return MaxSeconds;

        float matched = CubicOutInitialSpeedFactor * distanceCanvasPx / releaseSpeedCanvasPxPerSec;
        return Mathf.Clamp(matched, MinSeconds, MaxSeconds);
    }
}
