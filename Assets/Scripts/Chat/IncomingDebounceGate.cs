/// <summary>Pure debounce decision for coalescing rapid incoming batches into one «Вместе»
/// suggestions request (BATCH-03). Reset on each incoming (Poke); fires ONCE when the window
/// settles (ShouldFire); Cancel drops a pending window on chat close / bot switch / same-bot
/// chat switch / toggle-off. The clock is injected (seconds) so "3 rapid pokes -> 1 fire after
/// the window" is EditMode-testable — mirrors the pure-seam discipline of OpenChatLivePollGate
/// (stateless), but this gate is STATEFUL (holds the deadline + armed flag across calls).</summary>
public sealed class IncomingDebounceGate
{
    /// <summary>The coalesce window, in seconds. ~2.5s per CONTEXT (single tunable; tune at e2e).
    /// Long enough to catch a burst of fragments typed ~1s apart, short enough to feel live.</summary>
    public const float WindowSeconds = 2.5f;

    private float _deadline;
    private bool _armed;

    /// <summary>Reset the window: fire WindowSeconds after the most recent incoming.</summary>
    public void Poke(float now) { _deadline = now + WindowSeconds; _armed = true; }

    /// <summary>Drop a pending window (chat close / bot switch) — never fires until re-Poked.</summary>
    public void Cancel() => _armed = false;

    /// <summary>True EXACTLY once when the window has settled; disarms so it fires only once.</summary>
    public bool ShouldFire(float now)
    {
        if (!_armed || now < _deadline) return false;
        _armed = false;
        return true;
    }
}
