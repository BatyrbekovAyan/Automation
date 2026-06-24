using System;
using System.Collections.Generic;
using NUnit.Framework;

public class SemiAutoStoreTests
{
    private Dictionary<string, int> _mem;
    private Func<string, int> _savedGet;
    private Action<string, int> _savedSet;

    // Substitute the injectable seam with an in-memory dictionary so tests never touch PlayerPrefs.
    [SetUp]
    public void SetUp()
    {
        _savedGet = SemiAutoStore.GetInt;
        _savedSet = SemiAutoStore.SetIntAndSave;
        _mem = new Dictionary<string, int>();
        SemiAutoStore.GetInt = k => _mem.TryGetValue(k, out var v) ? v : 0;
        SemiAutoStore.SetIntAndSave = (k, v) => _mem[k] = v;
    }

    [TearDown]
    public void TearDown()
    {
        SemiAutoStore.GetInt = _savedGet;
        SemiAutoStore.SetIntAndSave = _savedSet;
    }

    [Test]
    public void Key_FollowsLockedScheme()
        => Assert.AreEqual("Bot0_semiAuto_c1@c.us", SemiAutoStore.Key("Bot0", "c1@c.us"));

    [Test]
    public void IsOn_DefaultForNeverSetKey_IsFalse()
        => Assert.IsFalse(SemiAutoStore.IsOn("Bot0", "c1@c.us"));

    [Test]
    public void SetTrueThenFalse_RoundTrips()
    {
        SemiAutoStore.Set("Bot0", "c1@c.us", true);
        Assert.IsTrue(SemiAutoStore.IsOn("Bot0", "c1@c.us"));

        SemiAutoStore.Set("Bot0", "c1@c.us", false);
        Assert.IsFalse(SemiAutoStore.IsOn("Bot0", "c1@c.us"));
    }

    [Test]
    public void BotSwitch_IsIsolated()
    {
        SemiAutoStore.Set("Bot0", "c1@c.us", true);
        Assert.IsFalse(SemiAutoStore.IsOn("Bot1", "c1@c.us")); // different bot = independent key
    }

    [Test]
    public void ChatSwitch_IsIsolated()
    {
        SemiAutoStore.Set("Bot0", "c1@c.us", true);
        Assert.IsFalse(SemiAutoStore.IsOn("Bot0", "c2@c.us")); // different chat = independent key
    }
}
