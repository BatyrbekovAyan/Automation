using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pins the Android Back rule (2026-09-06): exactly one surface reacts to a press — the
/// top-most open one — and a swallowing cover consumes the press without closing anything
/// beneath it. AndroidBackRouter supplies the live list; this is the walk it hands it to.
/// </summary>
public class BackNavigationTests
{
    private static BackTarget Target(string name, bool open, List<string> closed, bool swallow = false)
        => new BackTarget(name, () => open, () => closed.Add(name), swallow);

    [Test]
    public void FirstOpenSurface_TakesThePress_AndNothingBelowIsAsked()
    {
        var closed = new List<string>();
        var targets = new List<BackTarget>
        {
            Target("photo-viewer", open: false, closed),
            Target("popup", open: true, closed),
            Target("chat-thread", open: true, closed),   // also open, but underneath
        };

        Assert.AreEqual("popup", BackNavigation.Dispatch(targets));
        CollectionAssert.AreEqual(new[] { "popup" }, closed, "only the top-most open surface closes");
    }

    [Test]
    public void NothingOpen_ReturnsNull_AndClosesNothing()
    {
        var closed = new List<string>();
        var targets = new List<BackTarget> { Target("a", false, closed), Target("b", false, closed) };

        Assert.IsNull(BackNavigation.Dispatch(targets), "null = the caller backgrounds the app");
        Assert.IsEmpty(closed);
    }

    [Test]
    public void SwallowingCover_ConsumesThePress_WithoutClosing()
    {
        var closed = new List<string>();
        var targets = new List<BackTarget>
        {
            Target("loading", open: true, closed, swallow: true),
            Target("chat-thread", open: true, closed),
        };

        Assert.AreEqual("loading", BackNavigation.Dispatch(targets));
        Assert.IsEmpty(closed, "a cover swallows the press; the thread beneath must not close");
    }

    [Test]
    public void ATornDownProbe_IsTreatedAsClosed()
    {
        var closed = new List<string>();
        var targets = new List<BackTarget>
        {
            new BackTarget("gone", () => throw new System.NullReferenceException(), () => closed.Add("gone")),
            Target("chat-thread", open: true, closed),
        };

        Assert.AreEqual("chat-thread", BackNavigation.Dispatch(targets));
        CollectionAssert.AreEqual(new[] { "chat-thread" }, closed);
    }

    [Test]
    public void NullEntries_AreSkipped()
    {
        var closed = new List<string>();
        var targets = new List<BackTarget> { null, new BackTarget("no-probe", null, null), Target("x", true, closed) };

        Assert.AreEqual("x", BackNavigation.Dispatch(targets));
        Assert.IsNull(BackNavigation.Dispatch(null));
    }

    /// <summary>
    /// T9: the «Подключите ботов снова» sheet (a runtime overlay, sortingOrder 3) is a closeable surface, so it joins the
    /// router in its z-order — above the scene popups and the paywall it can sit over — and Back does exactly what its own
    /// «Позже» does: <c>ReconnectBotsSheet.Dismiss</c>, which closes the sheet and connects nothing. Source guard (the
    /// router reads the live scene); the dismiss behaviour itself is pinned on the real sheet by ReconnectBotsSheetTests.
    /// </summary>
    [Test]
    public void TheReconnectSheet_IsOnTheRouter_AndBackDoesWhatItsLaterDoes()
    {
        string router = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/AndroidBackRouter.cs"));
        const string entry = "new BackTarget(\"reconnect-bots-sheet\", () => ReconnectBotsSheet.IsOpen, ReconnectBotsSheet.Dismiss)";
        int sheet = router.IndexOf(entry, System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(sheet, 0, "the sheet must be a Back target, closed by its own «Позже» action");
        foreach (string below in new[] { "new BackTarget(\"popup\"", "new BackTarget(\"paywall\"", "new BackTarget(\"bot-settings\"" })
            Assert.Less(sheet, router.IndexOf(below, System.StringComparison.Ordinal), $"the overlay sheet sits above {below}");
        Assert.Greater(sheet, router.IndexOf("new BackTarget(\"loading\"", System.StringComparison.Ordinal),
            "a loading cover still swallows Back first");

        string sheetSrc = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/ReconnectBotsSheet.cs"));
        StringAssert.Contains("PopupUI.WireFingerUp(button, Dismiss)", sheetSrc, "«Позже» is Dismiss — the action Back repeats");
    }

    /// <summary>
    /// Learning B: the «Бот ещё учится» sheet (a runtime overlay, sortingOrder 3) joins the router in its z-order — above the
    /// scene popups and the paywall — and Back does what its own «Понятно» does: <c>AutoLockSheet.Dismiss</c>, which closes
    /// the sheet and enables nothing. Source guard; the dismiss behaviour itself is pinned on the real sheet by AutoLockSheetTests.
    /// </summary>
    [Test]
    public void TheAutoLockSheet_IsOnTheRouter_AndBackIsUnderstood()
    {
        string router = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/AndroidBackRouter.cs"));
        const string entry = "new BackTarget(\"auto-lock-sheet\", () => AutoLockSheet.IsShowing, AutoLockSheet.Dismiss)";
        int sheet = router.IndexOf(entry, System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(sheet, 0, "the learning sheet must be a Back target, closed by its own «Понятно» action");
        foreach (string below in new[] { "new BackTarget(\"popup\"", "new BackTarget(\"paywall\"", "new BackTarget(\"bot-settings\"" })
            Assert.Less(sheet, router.IndexOf(below, System.StringComparison.Ordinal), $"the overlay sheet sits above {below}");
        Assert.Greater(sheet, router.IndexOf("new BackTarget(\"loading\"", System.StringComparison.Ordinal),
            "a loading cover still swallows Back first");
    }

    /// <summary>
    /// «Личные и рабочие чаты» (spec §5.5, §5.6): the open chat's two sheets — «Сделать чат рабочим?» and «Сделать личным»
    /// (runtime overlays, sortingOrder 3) — join the router above the scene popups and the chat thread they sit over, and Back
    /// does what their quiet action does: «Оставить личным» / «Отмена» — the sheet closes and the chat stays where it is.
    /// The source order is pinned, and the REAL router list (AndroidBackRouter.Targets) is walked over the real sheets.
    /// </summary>
    [Test]
    public void Back_ClosesBothSheets()
    {
        string router = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/AndroidBackRouter.cs"));
        const string entry = "new BackTarget(\"personal-chat-sheets\", () => PersonalChatSheets.IsShowing, () => PersonalChatSheets.TryCloseTop())";
        int sheets = router.IndexOf(entry, System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(sheets, 0, "the two sheets must be a Back target, closed by their own quiet action");
        foreach (string below in new[] { "new BackTarget(\"popup\"", "new BackTarget(\"paywall\"", "new BackTarget(\"chat-thread\"", "new BackTarget(\"bot-settings\"" })
            Assert.Less(sheets, router.IndexOf(below, System.StringComparison.Ordinal), $"the overlay sheets sit above {below}");
        Assert.Greater(sheets, router.IndexOf("new BackTarget(\"loading\"", System.StringComparison.Ordinal),
            "a loading cover still swallows Back first");

        PersonalChatSheets.ResetForTests();
        int moved = 0;
        void Count(AudienceKind kind, string bot, string profile, string chat) => moved++;
        PersonalChatSheets.Committed += Count;
        try
        {
            var chat = new ChatViewModel("77000000501@c.us", "Сауле", null, "Есть розы на завтра?",
                System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            PersonalChatSheets.AskMakeBusiness("Bot0", "back-profile", chat, autoOn: true, null, null);
            Assert.AreEqual("personal-chat-sheets", BackNavigation.Dispatch(RouterTargets()), "Back over «Сделать чат рабочим?»");
            Assert.IsFalse(PersonalChatSheets.IsShowing, "Back = «Оставить личным»: the sheet closes");

            PersonalChatSheets.AskMakePersonal("Bot0", "back-profile", chat, "77000000501 · новый", null, null);
            Assert.AreEqual("personal-chat-sheets", BackNavigation.Dispatch(RouterTargets()), "Back over «Сделать личным»");
            Assert.IsFalse(PersonalChatSheets.IsShowing, "Back = «Отмена»: the sheet closes");

            Assert.AreEqual(0, moved, "Back never moves a chat");
            Assert.AreNotEqual("personal-chat-sheets", BackNavigation.Dispatch(RouterTargets()), "nothing of the sheets is left to close");
        }
        finally
        {
            PersonalChatSheets.Committed -= Count;
            PersonalChatSheets.ResetForTests();
        }
    }

    /// <summary>
    /// «Личные и рабочие чаты» (spec §5.3, §5.4): the «Личные» folder joins the router below the open chat (a chat opened from
    /// the folder slides in over it) and the full-screen flows, above nothing of its own screen. Back does what the folder's
    /// own left button does in each mode: the folder closes; «Изменить» is cancelled (the folder stays); «Кто из них
    /// клиенты?» is «Отмечу позже» — the flag is cleared and the screen closes. The source order is pinned, and the REAL router
    /// list is walked over a real folder (PersonalChatsFolderFixture).
    /// </summary>
    [Test]
    public void Back_ClosesFolder_EditIsCancel_OnboardingIsLater()
    {
        string router = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/AndroidBackRouter.cs"));
        const string entry = "new BackTarget(\"personal-chats-folder\", () => PersonalChatsFolderView.IsShowing, () => PersonalChatsFolderView.Instance?.TryBack())";
        int folder = router.IndexOf(entry, System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(folder, 0, "the folder must be a Back target, closed by its own left button");
        foreach (string above in new[] { "new BackTarget(\"chat-thread\"", "new BackTarget(\"auth-page\"", "new BackTarget(\"bot-settings\"",
                     "new BackTarget(\"add-bot\"", "new BackTarget(\"loading\"", "new BackTarget(\"personal-chat-sheets\"" })
            Assert.Greater(folder, router.IndexOf(above, System.StringComparison.Ordinal), $"{above} stands above the folder");
        foreach (string below in new[] { "new BackTarget(\"profile-subpage\"", "new BackTarget(\"dashboard-list\"" })
            Assert.Less(folder, router.IndexOf(below, System.StringComparison.Ordinal), $"the folder comes before {below}");

        const string bot = "BackNavigationFolderBot";
        var hosts = new List<GameObject>();
        Manager previousManager = Manager.Instance;
        ChatManager previousChats = ChatManager.Instance;
        PersonalChatsFolderView.ResetForTests();
        try
        {
            FolderHarness h = PersonalChatsFolderFixture.Build(bot, "back-folder-profile", hosts);

            h.Folder.Open(FolderMode.Browse);
            Assert.AreEqual("personal-chats-folder", BackNavigation.Dispatch(RouterTargets()), "Back over the folder");
            Assert.IsFalse(h.Folder.IsOpen, "the folder closes");
            Assert.AreNotEqual("personal-chats-folder", BackNavigation.Dispatch(RouterTargets()), "nothing of it is left to close");

            h.Folder.Open(FolderMode.Edit);
            Assert.AreEqual("personal-chats-folder", BackNavigation.Dispatch(RouterTargets()), "Back over «Изменить»");
            Assert.IsTrue(h.Folder.IsOpen, "«Отмена»: the folder stays…");
            Assert.AreEqual(FolderMode.Browse, h.Folder.Mode, "…back in its own mode");
            h.Folder.Close();

            PersonalChatsOnboarding.Request(bot, telegram: false);
            h.Folder.Open(FolderMode.Onboarding);
            Assert.AreEqual("personal-chats-folder", BackNavigation.Dispatch(RouterTargets()), "Back over «Кто из них клиенты?»");
            Assert.IsFalse(h.Folder.IsOpen, "«Отмечу позже»: the screen closes…");
            Assert.IsFalse(PersonalChatsOnboarding.IsRequested(bot, telegram: false), "…and is not owed any more");
        }
        finally
        {
            PersonalChatsOnboarding.Clear(bot, telegram: false);
            Manager.Instance = previousManager;
            ChatManager.Instance = previousChats;
            foreach (GameObject host in hosts)
                if (host != null) Object.DestroyImmediate(host);
            PersonalChatsFolderView.ResetForTests();
        }
    }

    // The router's live list, as one Back press reads it (its other surfaces are all closed in EditMode).
    private static List<BackTarget> RouterTargets()
    {
        System.Reflection.MethodInfo targets = typeof(AndroidBackRouter).GetMethod("Targets",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(targets, "AndroidBackRouter.Targets moved — update BackNavigationTests");
        return (List<BackTarget>)targets.Invoke(null, null);
    }
}
