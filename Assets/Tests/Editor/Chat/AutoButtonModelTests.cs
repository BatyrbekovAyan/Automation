using NUnit.Framework;
using UnityEngine;
using Mode = ReplyModeToggleBinder.ReplyMode;

// Covers AutoButtonModel — the pure seam behind the chats-header «Авто» button —
// plus the two deliberate spec behaviours it pins (chats-topbar-spec.md §1):
//   • DEFAULT FLIP: a bot with no stored value reads SEMI-auto, not Авто. This
//     was consciously flipped from the pre-restyle default; if this test fails
//     someone reverted the read default — do not "fix" the test.
//   • CONFIRM ASYMMETRY: only the enabling direction requires the popup.
public class AutoButtonModelTests
{
    // --- state mapping ---------------------------------------------------

    [Test]
    public void IsAutoOn_MapsAutoToOn_SemiToOff()
    {
        Assert.IsTrue(AutoButtonModel.IsAutoOn(Mode.Auto));
        Assert.IsFalse(AutoButtonModel.IsAutoOn(Mode.Semi));
    }

    [Test]
    public void Toggled_FlipsBothDirections()
    {
        Assert.AreEqual(Mode.Semi, AutoButtonModel.Toggled(Mode.Auto));
        Assert.AreEqual(Mode.Auto, AutoButtonModel.Toggled(Mode.Semi));
    }

    // --- confirm asymmetry ----------------------------------------------

    [Test]
    public void ConfirmRequired_OnlyWhenEnabling()
    {
        Assert.IsTrue(AutoButtonModel.ConfirmRequired(Mode.Semi),
            "Turning auto ON messages real clients — must confirm");
        Assert.IsFalse(AutoButtonModel.ConfirmRequired(Mode.Auto),
            "Turning auto OFF just hands the wheel back — instant, no popup");
    }

    // --- default flip ----------------------------------------------------

    [Test]
    public void DefaultMode_IsSemi()
    {
        Assert.AreEqual(Mode.Semi, AutoButtonModel.DefaultMode);
    }

    [Test]
    public void GetMode_UnsetBot_ReadsSemiDefault()
    {
        const string botName = "__AutoButtonModelTests_NoSuchBot__";
        PlayerPrefs.DeleteKey(botName + "ReplyMode");

        Assert.AreEqual(Mode.Semi, ReplyModeToggleBinder.GetMode(botName),
            "A bot that never saved a reply mode must read semi-auto (the silent default)");
    }

    [Test]
    public void GetMode_StoredValue_Wins()
    {
        const string botName = "__AutoButtonModelTests_StoredAuto__";
        const string key = botName + "ReplyMode";
        try
        {
            PlayerPrefs.SetInt(key, (int)Mode.Auto);
            Assert.AreEqual(Mode.Auto, ReplyModeToggleBinder.GetMode(botName),
                "An explicit Авто choice must survive the default flip");
        }
        finally
        {
            PlayerPrefs.DeleteKey(key);
        }
    }
}
