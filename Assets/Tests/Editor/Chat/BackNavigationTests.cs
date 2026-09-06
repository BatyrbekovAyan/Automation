using System.Collections.Generic;
using NUnit.Framework;

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
}
