using System.Collections.Generic;
using NUnit.Framework;

public class UnreadSeparatorPlacementTests
{
    private static List<bool> Incoming(params bool[] flags) => new List<bool>(flags);

    [Test]
    public void ZeroUnread_ReturnsZero()
    {
        Assert.AreEqual(0, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true, true, true), 0));
    }

    [Test]
    public void NegativeUnread_ReturnsZero()
    {
        Assert.AreEqual(0, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true, false), -3));
    }

    [Test]
    public void NullList_ReturnsZero()
    {
        Assert.AreEqual(0, UnreadSeparatorPlacement.IndexForUnreadCount(null, 2));
    }

    [Test]
    public void EmptyList_ReturnsZero()
    {
        Assert.AreEqual(0, UnreadSeparatorPlacement.IndexForUnreadCount(new List<bool>(), 3));
    }

    [Test]
    public void AllIncoming_TwoUnread_TwoBubblesBelow()
    {
        // newest-first [in,in,in,in], n=2 → separator above the 2nd-newest incoming
        Assert.AreEqual(2, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true, true, true, true), 2));
    }

    [Test]
    public void SingleIncoming_OneUnread_ReturnsOne()
    {
        Assert.AreEqual(1, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true), 1));
    }

    [Test]
    public void MixedTail_OutgoingNewerThanUnread_CountsIncomingOnly()
    {
        // newest-first [out,out,in,in,out,in], n=2
        // walk: out, out, in(1), in(2==n) at index 3 → 4 bubbles below
        Assert.AreEqual(4, UnreadSeparatorPlacement.IndexForUnreadCount(
            Incoming(false, false, true, true, false, true), 2));
    }

    [Test]
    public void FewerIncomingThanUnread_PlacesAtTop()
    {
        // [in,out,in], n=5 → only 2 incoming → top = count = 3
        Assert.AreEqual(3, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true, false, true), 5));
    }

    [Test]
    public void ExactIncomingCount_PlacesAtTop()
    {
        // [in,out,in], n=2 → in(1), in(2==n) at index 2 → 3 below
        Assert.AreEqual(3, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true, false, true), 2));
    }

    [Test]
    public void NoIncoming_PlacesAtTop()
    {
        Assert.AreEqual(2, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(false, false), 1));
    }
}
