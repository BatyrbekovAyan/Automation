using System.Collections.Generic;
using NUnit.Framework;

/// <summary>Learning B: the one door to enabling «Авто» — straight through on Open, the sheet on Locked.</summary>
public class AutoLockGateTests
{
    [SetUp] public void SetUp() { UsageStore.ResetSeamsForTests(); AutoLockSheet.ResetForTests(); }
    [TearDown] public void TearDown() { UsageStore.ResetSeamsForTests(); AutoLockSheet.ResetForTests(); }

    private static void Snapshot(params AutoReadinessEntry[] rows) =>
        UsageStore.Apply(new UsageSnapshot { success = true, readiness = new List<AutoReadinessEntry>(rows) });

    [Test]
    public void OpenVerdict_ProceedsAtOnce_NoSheet()
    {
        int n = 0;
        AutoLockGate.TryEnable("Bot0", null, perChat: false, () => n++);           // no snapshot at all
        Snapshot(new AutoReadinessEntry { botKey = "Bot0", matches = 12, total = 15, chats = 3, days = 2, ready = true });
        AutoLockGate.TryEnable("Bot0", null, perChat: false, () => n++);           // ready
        AutoLockGate.TryEnable("Bot9", null, perChat: false, () => n++);           // not listed
        AutoLockGate.TryEnable(null, null, perChat: false, () => n++);             // no key (a chat with no bot context)
        Assert.AreEqual(4, n);
        Assert.IsFalse(AutoLockSheet.IsShowing);
    }

    [Test]
    public void LockedVerdict_ShowsTheSheet_AndProceedsOnlyOnEnableAnyway()
    {
        int n = 0;
        Snapshot(new AutoReadinessEntry { botKey = "Bot0", matches = 7, total = 12, chats = 3, days = 2, ready = false });
        AutoLockGate.TryEnable("Bot0", null, perChat: false, () => n++);
        Assert.IsTrue(AutoLockSheet.IsShowing);
        Assert.AreEqual(0, n, "the lock defers the enable");
        AutoLockSheet.EnableAnyway();
        Assert.AreEqual(1, n);
    }

    [Test]
    public void LockedVerdict_Understood_EnablesNothing()
    {
        int n = 0;
        Snapshot(new AutoReadinessEntry { botKey = "Bot0", matches = 0, total = 0, chats = 0, days = 0, ready = false });
        AutoLockGate.TryEnable("Bot0", null, perChat: true, () => n++);
        AutoLockSheet.Dismiss();
        Assert.AreEqual(0, n);
        Assert.IsFalse(AutoLockSheet.IsShowing);
    }
}
