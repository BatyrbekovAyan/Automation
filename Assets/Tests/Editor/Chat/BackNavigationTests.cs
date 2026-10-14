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
}
