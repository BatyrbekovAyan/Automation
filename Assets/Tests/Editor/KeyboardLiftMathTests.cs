using NUnit.Framework;

/// <summary>
/// Pure-math coverage for the focused-field keyboard lift. The runtime
/// behaviour (platform keyboard readers, SmoothDamp, reparenting) is
/// device-only — these pin the decisions that can be reasoned about.
/// </summary>
public class KeyboardLiftMathTests
{
    private const float NoCeiling = float.MaxValue;

    // ---- RequiredLift ----------------------------------------------------

    [Test]
    public void KeyboardDown_NoLift()
    {
        // A bottom-most field must not twitch upward on dismiss.
        var lift = KeyboardLiftMath.RequiredLift(
            fieldBottomAtRestY: 40f, keyboardTopY: 0f, clearance: 48f, maxLift: NoCeiling);
        Assert.AreEqual(0f, lift);
    }

    [Test]
    public void FieldAlreadyAboveKeyboard_NoLift()
    {
        var lift = KeyboardLiftMath.RequiredLift(
            fieldBottomAtRestY: 1200f, keyboardTopY: 800f, clearance: 48f, maxLift: NoCeiling);
        Assert.AreEqual(0f, lift);
    }

    [Test]
    public void FieldExactlyAtClearance_NoLift()
    {
        var lift = KeyboardLiftMath.RequiredLift(
            fieldBottomAtRestY: 848f, keyboardTopY: 800f, clearance: 48f, maxLift: NoCeiling);
        Assert.AreEqual(0f, lift);
    }

    [Test]
    public void FieldAtKeyboardTop_LiftsByClearanceOnly()
    {
        var lift = KeyboardLiftMath.RequiredLift(
            fieldBottomAtRestY: 800f, keyboardTopY: 800f, clearance: 48f, maxLift: NoCeiling);
        Assert.AreEqual(48f, lift);
    }

    [Test]
    public void FieldBehindKeyboard_LiftsClear()
    {
        // Bottom card at y=100 with an 800-tall keyboard: 800 + 48 - 100.
        var lift = KeyboardLiftMath.RequiredLift(
            fieldBottomAtRestY: 100f, keyboardTopY: 800f, clearance: 48f, maxLift: NoCeiling);
        Assert.AreEqual(748f, lift);
    }

    [Test]
    public void CeilingClampsLift()
    {
        // Without the clamp the card would be pushed over the header/tab bar.
        var lift = KeyboardLiftMath.RequiredLift(
            fieldBottomAtRestY: 100f, keyboardTopY: 800f, clearance: 48f, maxLift: 300f);
        Assert.AreEqual(300f, lift);
    }

    [Test]
    public void NegativeCeiling_ClampsToZero()
    {
        var lift = KeyboardLiftMath.RequiredLift(
            fieldBottomAtRestY: 100f, keyboardTopY: 800f, clearance: 48f, maxLift: -50f);
        Assert.AreEqual(0f, lift);
    }

    [Test]
    public void LiftedField_EndsAtOrAboveClearance()
    {
        // The invariant that actually matters: after lifting, the field's
        // bottom clears the keyboard by at least `clearance`.
        const float keyboardTop = 780f;
        const float clearance = 48f;
        foreach (var bottom in new[] { 0f, 60f, 199f, 500f, 779f })
        {
            var lift = KeyboardLiftMath.RequiredLift(bottom, keyboardTop, clearance, NoCeiling);
            Assert.GreaterOrEqual(bottom + lift, keyboardTop + clearance,
                $"field bottom {bottom} still covered after lift {lift}");
        }
    }

    // ---- ScrollDeltaNormalized -------------------------------------------

    [Test]
    public void Scroll_KeyboardDown_NoScroll()
    {
        var delta = KeyboardLiftMath.ScrollDeltaNormalized(
            slotBottomY: 100f, keyboardTopY: 0f, clearance: 48f, scrollableRange: 500f);
        Assert.AreEqual(0f, delta);
    }

    [Test]
    public void Scroll_SlotAlreadyClear_NoScroll()
    {
        var delta = KeyboardLiftMath.ScrollDeltaNormalized(
            slotBottomY: 1200f, keyboardTopY: 800f, clearance: 48f, scrollableRange: 500f);
        Assert.AreEqual(0f, delta);
    }

    [Test]
    public void Scroll_NothingToScroll_ReturnsZero()
    {
        // Content shorter than the viewport: no scrolling possible, and the
        // division must not blow up.
        var delta = KeyboardLiftMath.ScrollDeltaNormalized(
            slotBottomY: 100f, keyboardTopY: 800f, clearance: 48f, scrollableRange: 0f);
        Assert.AreEqual(0f, delta);
    }

    [Test]
    public void Scroll_CoveredSlot_ScrollsProportionally()
    {
        // Needs 748 units of travel over a 1496-unit scrollable range = 0.5.
        var delta = KeyboardLiftMath.ScrollDeltaNormalized(
            slotBottomY: 100f, keyboardTopY: 800f, clearance: 48f, scrollableRange: 1496f);
        Assert.AreEqual(0.5f, delta, 0.0001f);
    }

    // ---- ScreenPxToCanvas ------------------------------------------------

    [Test]
    public void KeyboardDown_ConvertsToZero()
    {
        var canvas = KeyboardLiftMath.ScreenPxToCanvas(
            0f, 0f, isOverlay: true, scaleFactor: 2.5f, canvasHeight: 1920f, screenHeight: 2400f);
        Assert.AreEqual(0f, canvas);
    }

    [Test]
    public void Overlay_DividesByScaleFactor()
    {
        // 1080x2400 device, 1080x1920 reference => scaleFactor 1.0 on width.
        var canvas = KeyboardLiftMath.ScreenPxToCanvas(
            1000f, 0f, isOverlay: true, scaleFactor: 1f, canvasHeight: 2400f, screenHeight: 2400f);
        Assert.AreEqual(1000f, canvas);
    }

    [Test]
    public void Overlay_ZeroScaleFactor_ReturnsZeroNotInfinity()
    {
        var canvas = KeyboardLiftMath.ScreenPxToCanvas(
            1000f, 0f, isOverlay: true, scaleFactor: 0f, canvasHeight: 1920f, screenHeight: 2400f);
        Assert.AreEqual(0f, canvas, "a zero scaleFactor must not produce Infinity");
    }

    [Test]
    public void NonOverlay_ScalesByCanvasToScreenRatio()
    {
        var canvas = KeyboardLiftMath.ScreenPxToCanvas(
            1200f, 0f, isOverlay: false, scaleFactor: 1f, canvasHeight: 1920f, screenHeight: 2400f);
        Assert.AreEqual(960f, canvas);
    }

    [Test]
    public void NonOverlay_ZeroScreenHeight_ReturnsZeroNotNaN()
    {
        var canvas = KeyboardLiftMath.ScreenPxToCanvas(
            1200f, 0f, isOverlay: false, scaleFactor: 1f, canvasHeight: 1920f, screenHeight: 0f);
        Assert.AreEqual(0f, canvas);
    }

    [Test]
    public void SafeAreaInset_IsSubtractedWhenSupplied()
    {
        // Overlay callers pass 0; the parameter exists for safe-area-inset
        // canvases, so verify it still behaves when supplied.
        var canvas = KeyboardLiftMath.ScreenPxToCanvas(
            1000f, 100f, isOverlay: true, scaleFactor: 1f, canvasHeight: 1920f, screenHeight: 2400f);
        Assert.AreEqual(900f, canvas);
    }

    [Test]
    public void SafeAreaLargerThanKeyboard_ReturnsZero()
    {
        var canvas = KeyboardLiftMath.ScreenPxToCanvas(
            80f, 100f, isOverlay: true, scaleFactor: 1f, canvasHeight: 1920f, screenHeight: 2400f);
        Assert.AreEqual(0f, canvas);
    }
}
