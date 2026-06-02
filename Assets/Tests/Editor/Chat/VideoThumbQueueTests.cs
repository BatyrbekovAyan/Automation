using System.Collections.Generic;
using NUnit.Framework;

public class VideoThumbQueueTests
{
    [Test]
    public void TryEnqueue_NewId_ReturnsTrue()
    {
        var q = new VideoThumbQueue(2);
        Assert.IsTrue(q.TryEnqueue("a"));
    }

    [Test]
    public void TryEnqueue_DuplicateId_ReturnsFalse()
    {
        var q = new VideoThumbQueue(2);
        q.TryEnqueue("a");
        Assert.IsFalse(q.TryEnqueue("a"));
    }

    [Test]
    public void TryEnqueue_NullOrEmpty_ReturnsFalse()
    {
        var q = new VideoThumbQueue(2);
        Assert.IsFalse(q.TryEnqueue(null));
        Assert.IsFalse(q.TryEnqueue(""));
    }

    [Test]
    public void Dispatch_RespectsMaxConcurrent()
    {
        var q = new VideoThumbQueue(2);
        q.TryEnqueue("a"); q.TryEnqueue("b"); q.TryEnqueue("c");
        List<string> first = q.Dispatch();
        Assert.AreEqual(2, first.Count);
        Assert.AreEqual(2, q.InFlightCount);
        Assert.AreEqual(1, q.PendingCount);
        List<string> second = q.Dispatch();   // cap reached
        Assert.AreEqual(0, second.Count);
    }

    [Test]
    public void Complete_FreesSlot_AllowsNextDispatch()
    {
        var q = new VideoThumbQueue(2);
        q.TryEnqueue("a"); q.TryEnqueue("b"); q.TryEnqueue("c");
        List<string> first = q.Dispatch();     // a, b
        q.Complete(first[0]);                  // free one slot
        List<string> next = q.Dispatch();      // c
        Assert.AreEqual(1, next.Count);
        Assert.AreEqual("c", next[0]);
    }

    [Test]
    public void Clear_ResetsState_AndAllowsReEnqueue()
    {
        var q = new VideoThumbQueue(2);
        q.TryEnqueue("a");
        q.Dispatch();
        q.Clear();
        Assert.AreEqual(0, q.InFlightCount);
        Assert.AreEqual(0, q.PendingCount);
        Assert.IsTrue(q.TryEnqueue("a"));      // enqueueable again after Clear
    }
}
