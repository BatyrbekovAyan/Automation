using NUnit.Framework;

public class DeletedChatRuleTests
{
    [Test] public void FreshDelete_SameTimestamp_Hides()
    {
        bool hide = DeletedChatRule.ShouldHide(true, 100, 100, false, out long adopt);
        Assert.IsTrue(hide); Assert.AreEqual(-1, adopt);
    }

    [Test] public void NoNewActivity_OlderTimestamp_Hides()
        => Assert.IsTrue(DeletedChatRule.ShouldHide(true, 100, 90, true, out _));

    [Test] public void Revived_NewerTimestamp_Shows()
    {
        bool hide = DeletedChatRule.ShouldHide(true, 100, 150, true, out long adopt);
        Assert.IsFalse(hide); Assert.AreEqual(-1, adopt);
    }

    [Test] public void ExternalDelete_NoWatermark_HidesAndAdopts()
    {
        bool hide = DeletedChatRule.ShouldHide(false, 0, 100, true, out long adopt);
        Assert.IsTrue(hide); Assert.AreEqual(100, adopt);
    }

    [Test] public void NeverDeleted_Shows()
    {
        bool hide = DeletedChatRule.ShouldHide(false, 0, 100, false, out long adopt);
        Assert.IsFalse(hide); Assert.AreEqual(-1, adopt);
    }

    [Test] public void ZeroTimestamp_WithZeroWatermark_Hides()
        => Assert.IsTrue(DeletedChatRule.ShouldHide(true, 0, 0, false, out _));
}
