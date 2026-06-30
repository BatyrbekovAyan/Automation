using System;
using System.Collections.Generic;
using NUnit.Framework;

public class SemiAutoStoreTests
{
    private Dictionary<string, int> _mem;
    private Func<string, int> _savedGet;
    private Action<string, int> _savedSet;
    private Func<string, bool> _savedBotDefault;

    // Substitute the injectable seams with in-memory fakes so tests never touch PlayerPrefs or the
    // reply-mode binder. Bot default fixed to Auto (false) unless a test overrides it.
    [SetUp]
    public void SetUp()
    {
        _savedGet = SemiAutoStore.GetInt;
        _savedSet = SemiAutoStore.SetIntAndSave;
        _savedBotDefault = SemiAutoStore.BotDefaultSemi;
        _mem = new Dictionary<string, int>();
        SemiAutoStore.GetInt = k => _mem.TryGetValue(k, out var v) ? v : 0;
        SemiAutoStore.SetIntAndSave = (k, v) => _mem[k] = v;
        SemiAutoStore.BotDefaultSemi = _ => false;   // bot default = Auto unless a test says otherwise
    }

    [TearDown]
    public void TearDown()
    {
        SemiAutoStore.GetInt = _savedGet;
        SemiAutoStore.SetIntAndSave = _savedSet;
        SemiAutoStore.BotDefaultSemi = _savedBotDefault;
    }

    [Test]
    public void Key_FollowsLockedScheme()
        => Assert.AreEqual("Bot0_semiAuto_c1@c.us", SemiAutoStore.Key("Bot0", "c1@c.us"));

    [Test]
    public void IsOn_NeverSet_FollowsBotDefaultAuto()
        => Assert.IsFalse(SemiAutoStore.IsOn("Bot0", "c1@c.us"));   // bot default Auto → off

    [Test]
    public void IsOn_NeverSet_FollowsBotDefaultSemi()
    {
        SemiAutoStore.BotDefaultSemi = _ => true;                   // bot default Semi
        Assert.IsTrue(SemiAutoStore.IsOn("Bot0", "c1@c.us"));       // no override → inherits Semi
    }

    [Test]
    public void ExplicitOff_OverridesBotDefaultSemi()
    {
        SemiAutoStore.BotDefaultSemi = _ => true;                   // bot default Semi
        SemiAutoStore.Set("Bot0", "c1@c.us", false);               // per-chat override OFF
        Assert.IsFalse(SemiAutoStore.IsOn("Bot0", "c1@c.us"));      // override wins
    }

    [Test]
    public void ExplicitOn_OverridesBotDefaultAuto()
    {
        SemiAutoStore.BotDefaultSemi = _ => false;                  // bot default Auto
        SemiAutoStore.Set("Bot0", "c1@c.us", true);                // per-chat override ON
        Assert.IsTrue(SemiAutoStore.IsOn("Bot0", "c1@c.us"));       // override wins
    }

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
