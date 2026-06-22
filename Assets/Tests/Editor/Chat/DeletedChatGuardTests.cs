using System.Collections.Generic;
using NUnit.Framework;

public class DeletedChatGuardTests
{
    [Test] public void SuppressesAfterMark()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted("a@c.us");
        Assert.IsTrue(g.ShouldSuppress("a@c.us"));
        Assert.IsFalse(g.ShouldSuppress("b@c.us"));
    }

    [Test] public void ClearStopsSuppression()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted("a@c.us");
        g.Clear("a@c.us");
        Assert.IsFalse(g.ShouldSuppress("a@c.us"));
    }

    [Test] public void ReconcileKeepsIdStillOnServer()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted("a@c.us");
        g.ReconcileWithServer(new HashSet<string> { "a@c.us", "b@c.us" });
        Assert.IsTrue(g.ShouldSuppress("a@c.us"));
    }

    [Test] public void ReconcileDropsIdAbsentFromServer()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted("a@c.us");
        g.ReconcileWithServer(new HashSet<string> { "b@c.us" });
        Assert.IsFalse(g.ShouldSuppress("a@c.us"));
    }

    [Test] public void NullAndEmptyAreSafe()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted(null); g.MarkDeleted("");
        Assert.IsFalse(g.ShouldSuppress(null));
        Assert.IsFalse(g.ShouldSuppress(""));
        g.ReconcileWithServer(null);
    }

    [Test] public void ClearAllForgetsEverything()
    {
        var g = new DeletedChatGuard();
        g.MarkDeleted("a@c.us");
        g.MarkDeleted("b@c.us");
        g.ClearAll();
        Assert.IsFalse(g.ShouldSuppress("a@c.us"));
        Assert.IsFalse(g.ShouldSuppress("b@c.us"));
    }
}
