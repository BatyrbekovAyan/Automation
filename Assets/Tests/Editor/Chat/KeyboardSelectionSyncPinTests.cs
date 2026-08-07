using NUnit.Framework;

public class KeyboardSelectionSyncPinTests
{
    [Test]
    public void UpdateKeyboardStringPosition_StillExistsInThisTmpVersion()
    {
        Assert.IsTrue(KeyboardSelectionSync.TargetExists,
            "TMP_InputField.UpdateKeyboardStringPosition is gone — a Unity/uGUI upgrade broke " +
            "KeyboardSelectionSync. Re-point the seam (see docs/superpowers/specs/2026-08-07-input-text-selection-design.md).");
    }

    [Test]
    public void Push_WithNullField_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => KeyboardSelectionSync.Push(null));
    }
}
