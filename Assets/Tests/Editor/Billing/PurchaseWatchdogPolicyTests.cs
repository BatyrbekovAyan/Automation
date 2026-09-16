using NUnit.Framework;

public class PurchaseWatchdogPolicyTests
{
    [Test]
    public void Gives_up_only_after_the_grace_with_no_callback_and_no_sheet()
    {
        Assert.IsFalse(PurchaseWatchdogPolicy.ShouldGiveUp(false, false, 0f));
        Assert.IsFalse(PurchaseWatchdogPolicy.ShouldGiveUp(false, false, PurchaseWatchdogPolicy.SheetGraceSeconds - 0.01f));
        Assert.IsTrue(PurchaseWatchdogPolicy.ShouldGiveUp(false, false, PurchaseWatchdogPolicy.SheetGraceSeconds));
        Assert.IsTrue(PurchaseWatchdogPolicy.ShouldGiveUp(false, false, 600f));
    }

    /// <summary>A sheet on screen means the store is alive; the user may sit on it indefinitely.</summary>
    [Test]
    public void Never_gives_up_once_system_ui_appeared()
    {
        Assert.IsFalse(PurchaseWatchdogPolicy.ShouldGiveUp(false, true, PurchaseWatchdogPolicy.SheetGraceSeconds));
        Assert.IsFalse(PurchaseWatchdogPolicy.ShouldGiveUp(false, true, 3600f));
    }

    [Test]
    public void Never_gives_up_after_the_callback_arrived()
    {
        Assert.IsFalse(PurchaseWatchdogPolicy.ShouldGiveUp(true, false, 3600f));
        Assert.IsFalse(PurchaseWatchdogPolicy.ShouldGiveUp(true, true, 3600f));
    }

    /// <summary>The grace must clear a slow sandbox login but not feel like a dead button.</summary>
    [Test]
    public void Grace_is_between_five_and_twenty_seconds()
    {
        Assert.GreaterOrEqual(PurchaseWatchdogPolicy.SheetGraceSeconds, 5f);
        Assert.LessOrEqual(PurchaseWatchdogPolicy.SheetGraceSeconds, 20f);
    }

    [Test]
    public void Hung_reason_is_never_mistaken_for_a_cancel()
        => Assert.AreNotEqual(BillingFailure.UserCancelled, PurchaseWatchdogPolicy.HungReason);
}
