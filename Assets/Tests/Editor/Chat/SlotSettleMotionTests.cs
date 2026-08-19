using NUnit.Framework;

// EditMode coverage for SlotSettleMotion — the duration of the suggestions slot's settle after the
// finger lets go. Pins the property the owner actually reported as missing: the settle must
// CONTINUE the gesture, not restart it. A constant duration cannot, because the panel arrives at the
// release point already moving; matching the cubic-out curve's initial speed (3x its average) to the
// finger's is what removes the stall. The two clamps are pinned as bounds, not as values — they are
// device-tuning knobs, while the velocity match is the rule.
public class SlotSettleMotionTests
{
    [Test]
    public void Duration_MatchesTheCubicsInitialSpeedToTheFinger()
    {
        // 3 * 300 / 3000 = 0.30s, inside the clamp range so the raw rule is observable.
        Assert.AreEqual(0.30f, SlotSettleMotion.Duration(300f, 3000f), 0.0001f);
    }

    [Test]
    public void Duration_FasterReleaseSettlesSooner()
    {
        float slow = SlotSettleMotion.Duration(300f, 3000f);
        float fast = SlotSettleMotion.Duration(300f, 6000f);
        Assert.Less(fast, slow);
    }

    [Test]
    public void Duration_FartherToTravelTakesLonger()
    {
        float near = SlotSettleMotion.Duration(150f, 3000f);
        float far = SlotSettleMotion.Duration(300f, 3000f);
        Assert.Greater(far, near);
    }

    [Test]
    public void Duration_HardFlick_ClampsToTheFloorInsteadOfCutting()
        => Assert.AreEqual(SlotSettleMotion.MinSeconds, SlotSettleMotion.Duration(50f, 40000f), 0.0001f);

    [Test]
    public void Duration_BarelyMoving_ClampsToTheCeilingInsteadOfCrawling()
        => Assert.AreEqual(SlotSettleMotion.MaxSeconds, SlotSettleMotion.Duration(700f, 60f), 0.0001f);

    // A release with no measurable speed is not an instant one — it gets the gentle end.
    [Test]
    public void Duration_NoMeasurableSpeed_TakesTheCeiling()
    {
        Assert.AreEqual(SlotSettleMotion.MaxSeconds, SlotSettleMotion.Duration(300f, 0f), 0.0001f);
        Assert.AreEqual(SlotSettleMotion.MaxSeconds, SlotSettleMotion.Duration(300f, -3000f), 0.0001f);
    }

    // Already there: the tween still has to exist (callers hang completion work on it), but it must
    // not linger.
    [Test]
    public void Duration_NothingLeftToTravel_TakesTheFloor()
    {
        Assert.AreEqual(SlotSettleMotion.MinSeconds, SlotSettleMotion.Duration(0f, 3000f), 0.0001f);
        Assert.AreEqual(SlotSettleMotion.MinSeconds, SlotSettleMotion.Duration(-10f, 3000f), 0.0001f);
    }

    [Test]
    public void Duration_GarbageInput_FallsBackToTheNeutralDefault()
    {
        Assert.AreEqual(SlotSettleMotion.DefaultSeconds, SlotSettleMotion.Duration(float.NaN, 3000f), 0.0001f);
        Assert.AreEqual(SlotSettleMotion.DefaultSeconds, SlotSettleMotion.Duration(300f, float.NaN), 0.0001f);
        Assert.AreEqual(SlotSettleMotion.DefaultSeconds, SlotSettleMotion.Duration(float.PositiveInfinity, 3000f), 0.0001f);
    }

    [Test]
    public void Duration_IsAlwaysInsideTheClampRange()
    {
        for (float d = 0f; d <= 1200f; d += 37f)
            for (float v = 0f; v <= 20000f; v += 613f)
            {
                float t = SlotSettleMotion.Duration(d, v);
                Assert.GreaterOrEqual(t, SlotSettleMotion.MinSeconds, $"d={d} v={v}");
                Assert.LessOrEqual(t, SlotSettleMotion.MaxSeconds, $"d={d} v={v}");
            }
    }
}
