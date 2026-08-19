/// <summary>
/// Finger speed for the suggestions slot's two drag entries — the 42u handle
/// (<see cref="SuggestionSlotDragHandle"/>) and the thread pull-down
/// (<see cref="SlotPullDownRecognizer"/>). Keeps a short ring of (position, time) samples and
/// reports the AVERAGE velocity across the newest window, never a single-frame delta: on a touch
/// screen one dropped or coalesced pointer frame turns a steady drag into a spike, and the flick
/// rule (<see cref="SuggestionSlotDetents.SnapWithFlick"/>) would then collapse the slot off an
/// ordinary scroll.
/// <para>
/// Positions are CANVAS units on Unity's POSITIVE-IS-UP pointer axis, so a downward flick reports
/// a NEGATIVE velocity — callers must not negate. The clock is injected (callers pass
/// Time.unscaledTime), so the whole class is EditMode-testable with no Unity lifecycle.
/// </para>
/// <para>
/// Every degenerate input reports exactly 0 rather than a number: fewer than two samples, a clock
/// that did not advance, and non-finite coordinates. Zero is the only value that can never be
/// mistaken for a flick, which is what keeps a broken pointer frame from dismissing the panel.
/// </para>
/// </summary>
public sealed class DragVelocitySampler
{
    /// <summary>How far back the average reaches. Long enough to absorb a dropped frame at 60 fps,
    /// short enough that the answer describes the END of the gesture rather than its middle.</summary>
    public const float WindowSeconds = 0.08f;

    private const int Capacity = 8;

    private readonly float[] _y = new float[Capacity];
    private readonly float[] _t = new float[Capacity];
    private int _count;
    private int _head;   // next write slot

    public void Reset()
    {
        _count = 0;
        _head = 0;
    }

    /// <summary>Record one sample. A non-finite position or time is a broken pointer frame: it is
    /// DROPPED rather than stored, so the samples around it stay usable.</summary>
    public void Sample(float canvasY, float timeSeconds)
    {
        if (!float.IsFinite(canvasY) || !float.IsFinite(timeSeconds)) return;
        _y[_head] = canvasY;
        _t[_head] = timeSeconds;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    /// <summary>Canvas units per second across the newest <see cref="WindowSeconds"/>.</summary>
    public float VelocityCanvasPxPerSec
    {
        get
        {
            if (_count < 2) return 0f;

            int newest = (_head - 1 + Capacity) % Capacity;
            int oldest = newest;
            for (int i = 1; i < _count; i++)
            {
                int idx = (newest - i + Capacity) % Capacity;
                if (_t[newest] - _t[idx] > WindowSeconds) break;
                oldest = idx;
            }

            float dt = _t[newest] - _t[oldest];
            return dt > 0f ? (_y[newest] - _y[oldest]) / dt : 0f;
        }
    }
}
