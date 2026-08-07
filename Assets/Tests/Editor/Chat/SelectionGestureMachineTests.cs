using NUnit.Framework;
using UnityEngine;

public class SelectionGestureMachineTests
{
    SelectionGestureMachine M() => new SelectionGestureMachine(0.45f, 0.3f, 30f);
    static readonly Vector2 P = new Vector2(100, 100);

    [Test] public void Quick_release_is_a_tap()
    {
        var m = M();
        m.Press(P, 0f);
        Assert.AreEqual(SelectionGestureMachine.Result.Tap, m.Release(P, 0.1f));
    }

    [Test] public void Second_tap_within_window_and_slop_is_double_tap()
    {
        var m = M();
        m.Press(P, 0f); m.Release(P, 0.1f);
        Assert.AreEqual(SelectionGestureMachine.Result.DoubleTap, m.Press(P + new Vector2(5, 5), 0.3f));
    }

    [Test] public void Second_tap_after_window_is_not_double_tap()
    {
        var m = M();
        m.Press(P, 0f); m.Release(P, 0.1f);
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Press(P, 0.6f));
    }

    [Test] public void Long_press_fires_at_threshold_while_within_slop()
    {
        var m = M();
        m.Press(P, 0f);
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Tick(0.44f));
        Assert.AreEqual(SelectionGestureMachine.Result.LongPress, m.Tick(0.46f));
        Assert.IsTrue(m.LongPressActive);
    }

    [Test] public void Move_past_slop_before_timer_cancels()
    {
        var m = M();
        m.Press(P, 0f);
        Assert.AreEqual(SelectionGestureMachine.Result.Cancel, m.Move(P + new Vector2(40, 0), 0.2f));
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Tick(1f));
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Release(P, 1.1f));
    }

    [Test] public void Move_after_long_press_does_not_cancel()
    {
        var m = M();
        m.Press(P, 0f);
        m.Tick(0.5f);
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Move(P + new Vector2(200, 0), 0.6f));
        Assert.IsTrue(m.LongPressActive);
    }

    [Test] public void Long_press_release_is_not_a_tap()
    {
        var m = M();
        m.Press(P, 0f);
        m.Tick(0.5f);
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Release(P, 0.6f));
        Assert.IsFalse(m.LongPressActive);
    }

    [Test] public void Double_tap_press_suppresses_long_press_timer()
    {
        var m = M();
        m.Press(P, 0f); m.Release(P, 0.1f);
        m.Press(P, 0.2f); // DoubleTap
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Tick(2f));
    }
}
