using UnityEngine;

/// Pure tap / double-tap / long-press / slop-cancel state machine. No Unity
/// lifecycle: the router feeds pointer events and Tick with explicit
/// timestamps, so EditMode tests drive time directly. After LongPress or
/// DoubleTap, LongPressActive stays true until Release — the router routes
/// finger movement into selection extension during that window.
public sealed class SelectionGestureMachine
{
    public enum Result { None, Tap, DoubleTap, LongPress, Cancel }

    readonly float _longPressSeconds;
    readonly float _doubleTapSeconds;
    readonly float _slopSqr;

    bool _pressed;
    bool _cancelled;
    bool _committed;          // LongPress or DoubleTap already fired for this press
    Vector2 _pressPos;
    float _pressTime;
    Vector2 _lastTapPos;
    float _lastTapTime = float.NegativeInfinity;

    public SelectionGestureMachine(float longPressSeconds = 0.45f, float doubleTapSeconds = 0.3f, float slopPixels = 30f)
    {
        _longPressSeconds = longPressSeconds;
        _doubleTapSeconds = doubleTapSeconds;
        _slopSqr = slopPixels * slopPixels;
    }

    public bool IsPressed => _pressed;
    public bool LongPressActive => _pressed && _committed;

    public Result Press(Vector2 pos, float now)
    {
        _pressed = true;
        _cancelled = false;
        _committed = false;
        _pressPos = pos;
        _pressTime = now;

        if (now - _lastTapTime <= _doubleTapSeconds && (pos - _lastTapPos).sqrMagnitude <= _slopSqr)
        {
            _lastTapTime = float.NegativeInfinity;
            _committed = true;
            return Result.DoubleTap;
        }
        return Result.None;
    }

    public Result Move(Vector2 pos, float now)
    {
        if (!_pressed || _cancelled || _committed) return Result.None;
        if ((pos - _pressPos).sqrMagnitude > _slopSqr)
        {
            _cancelled = true;
            return Result.Cancel;
        }
        return Result.None;
    }

    public Result Tick(float now)
    {
        if (!_pressed || _cancelled || _committed) return Result.None;
        if (now - _pressTime >= _longPressSeconds)
        {
            _committed = true;
            return Result.LongPress;
        }
        return Result.None;
    }

    public Result Release(Vector2 pos, float now)
    {
        bool clean = _pressed && !_cancelled && !_committed;
        _pressed = false;
        if (!clean) return Result.None;
        if (now - _pressTime <= _doubleTapSeconds)
        {
            _lastTapPos = _pressPos;
            _lastTapTime = now;
        }
        return Result.Tap; // an in-slop release always places the caret like a tap
    }
}
