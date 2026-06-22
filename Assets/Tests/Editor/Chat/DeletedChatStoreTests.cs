using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class DeletedChatStoreTests
{
    private string _root;

    [SetUp] public void SetUp()
    {
        _root = Path.Combine(Application.temporaryCachePath, "delstore_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    [TearDown] public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test] public void RoundTrips()
    {
        var map = new Dictionary<string, long> { { "a@c.us", 100 }, { "b@c.us", 200 } };
        DeletedChatStore.Save(_root, map);
        var loaded = DeletedChatStore.Load(_root);
        Assert.AreEqual(2, loaded.Count);
        Assert.AreEqual(100, loaded["a@c.us"]);
        Assert.AreEqual(200, loaded["b@c.us"]);
    }

    [Test] public void MissingFileIsEmpty()
        => Assert.AreEqual(0, DeletedChatStore.Load(_root).Count);

    [Test] public void CorruptFileIsEmpty()
    {
        File.WriteAllText(Path.Combine(_root, "deleted_chats.json"), "not json");
        Assert.AreEqual(0, DeletedChatStore.Load(_root).Count);
    }

    [Test] public void NullRootIsSafe()
    {
        Assert.AreEqual(0, DeletedChatStore.Load(null).Count);
        Assert.DoesNotThrow(() => DeletedChatStore.Save(null, new Dictionary<string, long> { { "a", 1 } }));
    }
}
