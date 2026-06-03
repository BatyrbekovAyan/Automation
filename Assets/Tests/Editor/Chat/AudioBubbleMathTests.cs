using NUnit.Framework;

public class AudioBubbleMathTests
{
    [Test]
    public void BarHeights_SameSeed_IsDeterministic()
    {
        var a = AudioBubbleMath.BarHeights("msg-123", 32);
        var b = AudioBubbleMath.BarHeights("msg-123", 32);
        Assert.AreEqual(a, b);
    }

    [Test]
    public void BarHeights_DifferentSeed_Differs()
    {
        var a = AudioBubbleMath.BarHeights("msg-123", 32);
        var b = AudioBubbleMath.BarHeights("msg-999", 32);
        Assert.AreNotEqual(a, b);
    }

    [Test]
    public void BarHeights_HasRequestedLength()
    {
        Assert.AreEqual(32, AudioBubbleMath.BarHeights("x", 32).Length);
    }

    [Test]
    public void BarHeights_WithinBounds()
    {
        foreach (var h in AudioBubbleMath.BarHeights("seed", 64))
        {
            Assert.GreaterOrEqual(h, AudioBubbleMath.MinBarFraction);
            Assert.LessOrEqual(h, 1f);
        }
    }

    [Test]
    public void BarHeights_NonPositiveCount_IsEmpty()
    {
        Assert.AreEqual(0, AudioBubbleMath.BarHeights("seed", 0).Length);
    }

    [Test]
    public void FilledBars_Zero_None()
    {
        Assert.AreEqual(0f, AudioBubbleMath.FilledBars(0f, 32));
    }

    [Test]
    public void FilledBars_Full_CapsAtBarCount()
    {
        Assert.AreEqual(32f, AudioBubbleMath.FilledBars(1f, 32));
    }

    [Test]
    public void FilledBars_CompletesBeforeEnd()
    {
        // n+1 mapping: all 32 bars are full well before the audio ends (here at 98%),
        // so the finish event never has to pop the last bar in.
        Assert.AreEqual(32f, AudioBubbleMath.FilledBars(0.98f, 32));
    }

    [Test]
    public void FilledBars_Midpoint_IsContinuous()
    {
        // 0.5 * (32 + 1) = 16.5 → 16 full bars + leading bar half filled.
        Assert.AreEqual(16.5f, AudioBubbleMath.FilledBars(0.5f, 32), 0.001f);
    }

    [Test]
    public void FilledBars_LeadingBarFillsGradually()
    {
        // Across the last bar's range the value climbs continuously (the bar tints in)
        // rather than jumping from empty to full.
        float a = AudioBubbleMath.FilledBars(0.94f, 32);
        float b = AudioBubbleMath.FilledBars(0.96f, 32);
        Assert.GreaterOrEqual(a, 31f);
        Assert.Greater(b, a);
        Assert.Less(b, 32f);
    }

    [Test]
    public void FilledBars_ClampsAboveOne()
    {
        Assert.AreEqual(32f, AudioBubbleMath.FilledBars(1.4f, 32));
    }

    [Test]
    public void FilledBars_ClampsBelowZero()
    {
        Assert.AreEqual(0f, AudioBubbleMath.FilledBars(-0.3f, 32));
    }

    [Test]
    public void NextSpeed_CyclesForward()
    {
        Assert.AreEqual(1.5f, AudioBubbleMath.NextSpeed(1f));
        Assert.AreEqual(2f, AudioBubbleMath.NextSpeed(1.5f));
        Assert.AreEqual(1f, AudioBubbleMath.NextSpeed(2f));
    }

    [Test]
    public void NextSpeed_TolerantOfDrift()
    {
        Assert.AreEqual(2f, AudioBubbleMath.NextSpeed(1.499f));
    }

    [Test]
    public void SecondsFromFraction_Endpoints()
    {
        Assert.AreEqual(0f, AudioBubbleMath.SecondsFromFraction(0f, 30));
        Assert.AreEqual(30f, AudioBubbleMath.SecondsFromFraction(1f, 30));
    }

    [Test]
    public void SecondsFromFraction_Midpoint()
    {
        Assert.AreEqual(15f, AudioBubbleMath.SecondsFromFraction(0.5f, 30));
    }

    [Test]
    public void SecondsFromFraction_ZeroDuration_IsZero()
    {
        Assert.AreEqual(0f, AudioBubbleMath.SecondsFromFraction(0.5f, 0));
    }
}
