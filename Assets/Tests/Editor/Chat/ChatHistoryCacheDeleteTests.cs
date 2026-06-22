using System.IO;
using NUnit.Framework;
using UnityEngine;

public class ChatHistoryCacheDeleteTests
{
    private string _root;

    [SetUp] public void SetUp()
    {
        _root = Path.Combine(Application.temporaryCachePath, "delhist_" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(_root, "messages"));
    }

    [TearDown] public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test] public void DeletesTheChatFile()
    {
        string path = Path.Combine(_root, "messages", "a@c.us.json");
        File.WriteAllText(path, "{}");
        Assert.IsTrue(File.Exists(path));
        ChatHistoryCache.DeleteHistory(_root, "a@c.us");
        Assert.IsFalse(File.Exists(path));
    }

    [Test] public void NoThrowWhenFileAbsent()
        => Assert.DoesNotThrow(() => ChatHistoryCache.DeleteHistory(_root, "missing@c.us"));

    [Test] public void NullArgsAreSafe()
    {
        Assert.DoesNotThrow(() => ChatHistoryCache.DeleteHistory(null, "a@c.us"));
        Assert.DoesNotThrow(() => ChatHistoryCache.DeleteHistory(_root, null));
    }
}
