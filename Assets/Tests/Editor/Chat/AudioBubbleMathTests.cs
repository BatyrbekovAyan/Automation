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
    public void PlayedBarCount_Zero_None()
    {
        Assert.AreEqual(0, AudioBubbleMath.PlayedBarCount(0f, 32));
    }

    [Test]
    public void PlayedBarCount_Full_All()
    {
        Assert.AreEqual(32, AudioBubbleMath.PlayedBarCount(1f, 32));
    }

    [Test]
    public void PlayedBarCount_Half()
    {
        Assert.AreEqual(16, AudioBubbleMath.PlayedBarCount(0.5f, 32));
    }

    [Test]
    public void PlayedBarCount_ClampsAboveOne()
    {
        Assert.AreEqual(32, AudioBubbleMath.PlayedBarCount(1.4f, 32));
    }

    [Test]
    public void PlayedBarCount_ClampsBelowZero()
    {
        Assert.AreEqual(0, AudioBubbleMath.PlayedBarCount(-0.3f, 32));
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
