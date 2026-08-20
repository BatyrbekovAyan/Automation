using NUnit.Framework;

public class QuotaMathTests
{
    [Test] public void Under_80_is_ok() => Assert.AreEqual(QuotaState.Ok, QuotaMath.State(239, 300, 0));
    [Test] public void At_80_is_warn() => Assert.AreEqual(QuotaState.Warn, QuotaMath.State(240, 300, 0));
    [Test] public void Over_quota_without_topup_is_over() => Assert.AreEqual(QuotaState.Over, QuotaMath.State(300, 300, 0));
    [Test] public void Topup_extends_quota() {
        Assert.AreEqual(QuotaState.Warn, QuotaMath.State(300, 300, 500));   // 300/800 = 37% но базовая квота выбрана → Warn, не Over
        Assert.AreEqual(QuotaState.Over, QuotaMath.State(800, 300, 500));
        Assert.AreEqual(500, QuotaMath.Remaining(300, 300, 500));
    }
    [Test] public void Zero_quota_is_over_at_zero() => Assert.AreEqual(QuotaState.Over, QuotaMath.State(0, 0, 0));
    [Test] public void Percent_clamps_100() => Assert.AreEqual(100, QuotaMath.Percent(999, 300));
}
