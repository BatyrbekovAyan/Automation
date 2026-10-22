using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Learning B: the one door to enabling «Авто» — straight through on Open, the sheet on Locked. The verdict is `StateFor`'s:
/// a locked bot is a listed row WITH a `progress` that is not ready (a row without one is an older server — no data, Open).
/// A snapshot with a `progress` feeds the phone's learning memory (PlayerPrefs), hence the test-only bot key and the cleanup.
/// </summary>
public class AutoLockGateTests
{
    private const string Bot = "AutoLockGateTestBot";

    [SetUp] public void SetUp() { UsageStore.ResetSeamsForTests(); AutoLockSheet.ResetForTests(); LearnProgressStore.Clear(Bot); }
    [TearDown] public void TearDown() { UsageStore.ResetSeamsForTests(); AutoLockSheet.ResetForTests(); LearnProgressStore.Clear(Bot); }

    private static void Snapshot(params AutoReadinessEntry[] rows) =>
        UsageStore.Apply(new UsageSnapshot { success = true, readiness = new List<AutoReadinessEntry>(rows) });

    [Test]
    public void OpenVerdict_ProceedsAtOnce_NoSheet()
    {
        int n = 0;
        AutoLockGate.TryEnable(Bot, null, perChat: false, () => n++);              // no snapshot at all
        Snapshot(new AutoReadinessEntry { botKey = Bot, matches = 12, total = 15, chats = 3, days = 2, ready = true, progress = 75, unlockAt = 70 });
        AutoLockGate.TryEnable(Bot, null, perChat: false, () => n++);              // ready
        AutoLockGate.TryEnable("Bot9", null, perChat: false, () => n++);           // not listed
        AutoLockGate.TryEnable(null, null, perChat: false, () => n++);             // no key (a chat with no bot context)
        Assert.AreEqual(4, n);
        Assert.IsFalse(AutoLockSheet.IsShowing);
    }

    [Test]
    public void ARowWithoutProgress_AnOlderServer_ProceedsAtOnce_EvenWhenNotReady()
    {
        int n = 0;
        Snapshot(new AutoReadinessEntry { botKey = Bot, matches = 7, total = 12, chats = 3, days = 2, ready = false });   // no `progress`
        AutoLockGate.TryEnable(Bot, null, perChat: false, () => n++);
        Assert.AreEqual(1, n, "no progress = no data = Open — the lock never comes from a number the new rule did not compute");
        Assert.IsFalse(AutoLockSheet.IsShowing);
    }

    [Test]
    public void ABotWhoseAutoOpenedOnce_IsNeverLockedAgain()
    {
        int n = 0;
        Snapshot(new AutoReadinessEntry { botKey = Bot, ready = true, progress = 80, unlockAt = 70 });                    // the unlock is remembered …
        Snapshot(new AutoReadinessEntry { botKey = Bot, ready = false, progress = 40, unlockAt = 70 });                   // … when the window gets worse
        AutoLockGate.TryEnable(Bot, null, perChat: false, () => n++);
        Assert.AreEqual(1, n);
        Assert.IsFalse(AutoLockSheet.IsShowing);
    }

    [Test]
    public void LockedVerdict_ShowsTheSheet_AndProceedsOnlyOnEnableAnyway()
    {
        int n = 0;
        Snapshot(new AutoReadinessEntry { botKey = Bot, matches = 7, total = 12, chats = 3, days = 2, ready = false, progress = 45, unlockAt = 70 });
        AutoLockGate.TryEnable(Bot, null, perChat: false, () => n++);
        Assert.IsTrue(AutoLockSheet.IsShowing);
        Assert.AreEqual(0, n, "the lock defers the enable");
        // The sheet is fed the learning STATE: the percent and the server's threshold — not the window's «N из 20».
        Assert.AreEqual("45\u00A0%", AutoLockSheetTestsAccess.TextOf("Percent"));
        Assert.AreEqual(AutoReadinessCopy.SheetBody(70, false, 0, 0, false), AutoLockSheetTestsAccess.BodyText());
        AutoLockSheet.EnableAnyway();
        Assert.AreEqual(1, n);
    }

    [Test]
    public void LockedVerdict_WhenTheServerCapsThePercent_TheSheetNamesWhatIsMissing()
    {
        Snapshot(new AutoReadinessEntry
        {
            botKey = Bot, matches = 12, total = 20, chats = 1, days = 1, ready = false,
            progress = 69, unlockAt = 70, capped = true, needChats = 2, needDays = 1,
        });
        AutoLockGate.TryEnable(Bot, null, perChat: false, () => { });
        Assert.IsTrue(AutoLockSheet.IsShowing);
        Assert.AreEqual("69\u00A0%", AutoLockSheetTestsAccess.TextOf("Percent"));
        Assert.AreEqual(AutoReadinessCopy.SheetBody(70, true, 2, 1, false), AutoLockSheetTestsAccess.BodyText());
        StringAssert.Contains("в 2 других чатах и в 1 другой день", AutoLockSheetTestsAccess.BodyText());
    }

    [Test]
    public void LockedVerdict_TheSheetShowsTheBetterOfThePhonesMemoryAndTheServer()
    {
        // The window got worse after a better one: the percent never falls (spec §0.6), and the sheet reads StateFor, not the raw row.
        Snapshot(new AutoReadinessEntry { botKey = Bot, ready = false, progress = 60, unlockAt = 70 });
        Snapshot(new AutoReadinessEntry { botKey = Bot, ready = false, progress = 45, unlockAt = 70 });
        AutoLockGate.TryEnable(Bot, null, perChat: false, () => { });
        Assert.IsTrue(AutoLockSheet.IsShowing);
        Assert.AreEqual("60\u00A0%", AutoLockSheetTestsAccess.TextOf("Percent"));
        var fill = AutoLockSheet.PanelForTests.transform.Find("Content/Bar/Fill");
        Assert.IsNotNull(fill, "Content/Bar/Fill");
        Assert.AreEqual(0.60f, ((UnityEngine.RectTransform)fill).anchorMax.x, 1e-4f);
    }

    [Test]
    public void LockedVerdict_Understood_EnablesNothing()
    {
        int n = 0;
        Snapshot(new AutoReadinessEntry { botKey = Bot, matches = 0, total = 0, chats = 0, days = 0, ready = false, progress = 0, unlockAt = 70 });
        AutoLockGate.TryEnable(Bot, null, perChat: true, () => n++);
        AutoLockSheet.Dismiss();
        Assert.AreEqual(0, n);
        Assert.IsFalse(AutoLockSheet.IsShowing);
    }

    [Test]
    public void PerChatEnable_OnABotAlreadyInAuto_SkipsTheLock_ButMeetsItOnASemiBot()
    {
        int n = 0;
        Snapshot(new AutoReadinessEntry { botKey = Bot, matches = 2, total = 4, chats = 1, days = 1, ready = false, progress = 45, unlockAt = 70 });
        // The bot is in «Авто»: resuming one paused chat is a move INSIDE an enabled mode — the lock never applies (spec §0).
        AutoLockGate.TryEnableChat(Bot, ReplyModeToggleBinder.ReplyMode.Auto, null, () => n++);
        Assert.AreEqual(1, n);
        Assert.IsFalse(AutoLockSheet.IsShowing);
        // The bot is in «Вместе»: turning «Авто» on for one chat IS an enable — the sheet, with the per-chat body.
        AutoLockGate.TryEnableChat(Bot, ReplyModeToggleBinder.ReplyMode.Semi, null, () => n++);
        Assert.AreEqual(1, n);
        Assert.IsTrue(AutoLockSheet.IsShowing);
        var body = AutoLockSheetTestsAccess.BodyText();
        StringAssert.Contains("в этом чате", body);
        AutoLockSheet.EnableAnyway();
        Assert.AreEqual(2, n);
    }
}

/// <summary>Reads the live sheet's labels through the EditMode seam (the panel is built lazily on the first Show).</summary>
internal static class AutoLockSheetTestsAccess
{
    public static string BodyText() => TextOf("Body");

    public static string TextOf(string name)
    {
        var panel = AutoLockSheet.PanelForTests;
        Assert.IsNotNull(panel, "the sheet was shown");
        foreach (var tmp in panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            if (tmp.name == name) return tmp.text;
        Assert.Fail($"{name} label not found");
        return null;
    }
}
