using NUnit.Framework;

// EditMode coverage for DragVelocitySampler — the finger-speed reader behind the flick rule
// (SuggestionSlotDetents.SnapWithFlick) for BOTH slot drag entries. Pins the two properties that
// make a flick rule safe on a touch screen: velocity is averaged over a short WINDOW rather than
// taken from a single frame (one dropped or coalesced pointer frame is a spike, and a spike would
// collapse the slot off an ordinary scroll), and every degenerate input — one sample, a frozen
// clock, a non-finite coordinate — reports ZERO rather than a number, because zero is the only
// value that cannot be mistaken for a flick.
public class DragVelocitySamplerTests
{
    [Test]
    public void NoSamples_IsZero()
        => Assert.AreEqual(0f, new DragVelocitySampler().VelocityCanvasPxPerSec);

    [Test]
    public void OneSample_IsZero()
    {
        var s = new DragVelocitySampler();
        s.Sample(100f, 0f);
        Assert.AreEqual(0f, s.VelocityCanvasPxPerSec);
    }

    [Test]
    public void TwoSamples_UpwardTravel_IsPositive()
    {
        var s = new DragVelocitySampler();
        s.Sample(0f, 0f);
        s.Sample(100f, 0.05f);
        Assert.AreEqual(2000f, s.VelocityCanvasPxPerSec, 0.01f);
    }

    [Test]
    public void TwoSamples_DownwardTravel_IsNegative()
    {
        var s = new DragVelocitySampler();
        s.Sample(100f, 0f);
        s.Sample(0f, 0.05f);
        Assert.AreEqual(-2000f, s.VelocityCanvasPxPerSec, 0.01f);
    }

    [Test]
    public void FrozenClock_IsZero()
    {
        var s = new DragVelocitySampler();
        s.Sample(0f, 3f);
        s.Sample(500f, 3f);
        Assert.AreEqual(0f, s.VelocityCanvasPxPerSec);
    }

    // The whole point of the window: a finger that rested for half a second and then flicked must
    // report the FLICK, not the average of the rest and the flick.
    [Test]
    public void SamplesOlderThanWindow_AreExcluded()
    {
        var s = new DragVelocitySampler();
        s.Sample(0f, 0f);
        s.Sample(0f, 0.50f);
        s.Sample(-100f, 0.55f);
        Assert.AreEqual(-2000f, s.VelocityCanvasPxPerSec, 0.01f);
    }

    [Test]
    public void NonFiniteSample_IsDropped_AndEarlierSamplesSurvive()
    {
        var s = new DragVelocitySampler();
        s.Sample(0f, 0f);
        s.Sample(100f, 0.05f);
        s.Sample(float.NaN, 0.06f);
        s.Sample(200f, float.PositiveInfinity);
        Assert.AreEqual(2000f, s.VelocityCanvasPxPerSec, 0.01f);
    }

    [Test]
    public void Reset_ClearsTheWindow()
    {
        var s = new DragVelocitySampler();
        s.Sample(0f, 0f);
        s.Sample(100f, 0.05f);
        s.Reset();
        Assert.AreEqual(0f, s.VelocityCanvasPxPerSec);
    }

    // More samples than the ring holds: the oldest fall out, and the reported velocity stays the
    // recent one rather than wrapping to a stale slot.
    [Test]
    public void MoreSamplesThanCapacity_ReportsTheRecentWindow()
    {
        var s = new DragVelocitySampler();
        for (int i = 0; i < 40; i++) s.Sample(i * 10f, i * 0.01f);
        Assert.Greater(s.VelocityCanvasPxPerSec, 900f);
        Assert.Less(s.VelocityCanvasPxPerSec, 1100f);
    }
}
