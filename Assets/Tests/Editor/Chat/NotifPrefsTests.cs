using System;
using System.Collections.Generic;
using NUnit.Framework;

// Contract: all three notification switches default ON, persist via the
// injected store, and never touch other keys. Seams swapped per SemiAutoStore
// test pattern so real PlayerPrefs are never involved.
public class NotifPrefsTests
{
    private Func<string, int, int> _savedGet;
    private Action<string, int> _savedSet;
    private Dictionary<string, int> _mem;

    [SetUp]
    public void SetUp()
    {
        _savedGet = NotifPrefs.GetInt;
        _savedSet = NotifPrefs.SetIntAndSave;
        _mem = new Dictionary<string, int>();
        NotifPrefs.GetInt = (key, def) => _mem.TryGetValue(key, out var v) ? v : def;
        NotifPrefs.SetIntAndSave = (key, value) => _mem[key] = value;
    }

    [TearDown]
    public void TearDown()
    {
        NotifPrefs.GetInt = _savedGet;
        NotifPrefs.SetIntAndSave = _savedSet;
    }

    [Test]
    public void AllSwitches_DefaultOn()
    {
        Assert.IsTrue(NotifPrefs.SoundEnabled);
        Assert.IsTrue(NotifPrefs.VibrationEnabled);
        Assert.IsTrue(NotifPrefs.UnreadBadgeEnabled);
    }

    [Test]
    public void SetOff_ReadsBackOff()
    {
        NotifPrefs.SoundEnabled = false;
        Assert.IsFalse(NotifPrefs.SoundEnabled);
        Assert.AreEqual(0, _mem[NotifPrefs.SoundKey]);
    }

    [Test]
    public void SetOffThenOn_ReadsBackOn()
    {
        NotifPrefs.VibrationEnabled = false;
        NotifPrefs.VibrationEnabled = true;
        Assert.IsTrue(NotifPrefs.VibrationEnabled);
        Assert.AreEqual(1, _mem[NotifPrefs.VibrationKey]);
    }

    [Test]
    public void Switches_UseDistinctKeys()
    {
        NotifPrefs.SoundEnabled = false;
        Assert.IsTrue(NotifPrefs.VibrationEnabled);
        Assert.IsTrue(NotifPrefs.UnreadBadgeEnabled);
        Assert.AreEqual(1, _mem.Count);
    }
}
