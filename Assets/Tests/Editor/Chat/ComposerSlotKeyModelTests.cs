using System;
using NUnit.Framework;

/// <summary>
/// Pins the composer key's state → appearance table (sketch 005 winner E).
///
/// The glyph names the DESTINATION, not the current state — so ⌨ belongs to the states where the
/// PANEL is up and ✦ to the states where it is not. A test written from the state name alone would
/// assert the exact opposite, which is why every row below spells out the destination it expects.
/// Also pinned: Panel/Expanded are one row, Collapsed/Keyboard are the other, «Авто» hides the key
/// from every state, the circle AND PositiveInk ride the ✦ destination together, and a state no
/// cast should have produced resolves to that same ✦ row instead of inventing a keyboard.
/// </summary>
public class ComposerSlotKeyModelTests
{
    // Read from the enum, not hand-listed: SuggestionSlotState is owned by a sibling seam, and a
    // state added there must not slip past the sweeps below while the test names still say EVERY.
    private static readonly SuggestionSlotState[] AllStates =
        (SuggestionSlotState[])Enum.GetValues(typeof(SuggestionSlotState));

    // Not a tenant at all — a value that only a cast can produce (persisted int, a widened enum
    // read back by an old build). The seam must resolve it, never render from it.
    private const SuggestionSlotState NotATenant = (SuggestionSlotState)99;

    private static void AssertIdentical(SlotKeyStyle expected, SlotKeyStyle actual, string why)
    {
        Assert.AreEqual(expected.Visible, actual.Visible, $"{why} (Visible)");
        Assert.AreEqual(expected.Glyph, actual.Glyph, $"{why} (Glyph)");
        Assert.AreEqual(expected.TintCircle, actual.TintCircle, $"{why} (TintCircle)");
        Assert.AreEqual(expected.Ink, actual.Ink, $"{why} (Ink)");
    }

    // ---- One row per state («Вместе») -------------------------------------

    [Test]
    public void Panel_OffersTheKeyboard()
    {
        var style = ComposerSlotKeyModel.For(SuggestionSlotState.Panel, semiAutoOn: true);
        Assert.IsTrue(style.Visible);
        Assert.AreEqual(SlotKeyGlyph.Keyboard, style.Glyph,
            "panel is up → the destination is the keyboard, NOT the panel it already shows");
        Assert.IsFalse(style.TintCircle, "the return trip is quiet — no promoting circle");
        Assert.AreEqual(ThemeRole.InkTertiary, style.Ink);
    }

    [Test]
    public void Expanded_OffersTheKeyboard_SameAsPanel()
    {
        var style = ComposerSlotKeyModel.For(SuggestionSlotState.Expanded, semiAutoOn: true);
        Assert.IsTrue(style.Visible);
        Assert.AreEqual(SlotKeyGlyph.Keyboard, style.Glyph, "taller panel is still a panel that is up");
        Assert.IsFalse(style.TintCircle);
        Assert.AreEqual(ThemeRole.InkTertiary, style.Ink);
    }

    [Test]
    public void Collapsed_OffersTheSuggestions()
    {
        var style = ComposerSlotKeyModel.For(SuggestionSlotState.Collapsed, semiAutoOn: true);
        Assert.IsTrue(style.Visible);
        Assert.AreEqual(SlotKeyGlyph.Sparkle, style.Glyph,
            "nothing is up → the destination is the panel");
        Assert.IsTrue(style.TintCircle, "the ✦ destination is the promoted one");
        Assert.AreEqual(ThemeRole.PositiveInk, style.Ink);
    }

    [Test]
    public void Keyboard_OffersTheSuggestions_SameAsCollapsed()
    {
        var style = ComposerSlotKeyModel.For(SuggestionSlotState.Keyboard, semiAutoOn: true);
        Assert.IsTrue(style.Visible);
        Assert.AreEqual(SlotKeyGlyph.Sparkle, style.Glyph,
            "keyboard up still means the PANEL is not up — the destination is the panel");
        Assert.IsTrue(style.TintCircle);
        Assert.AreEqual(ThemeRole.PositiveInk, style.Ink);
    }

    // ---- The two destinations collapse to two rows ------------------------

    [Test]
    public void PanelAndExpanded_AreOneRow()
        => AssertIdentical(
            ComposerSlotKeyModel.For(SuggestionSlotState.Panel, semiAutoOn: true),
            ComposerSlotKeyModel.For(SuggestionSlotState.Expanded, semiAutoOn: true),
            "both are «panel is up» — the drag detent must not change the key");

    [Test]
    public void CollapsedAndKeyboard_AreOneRow()
        => AssertIdentical(
            ComposerSlotKeyModel.For(SuggestionSlotState.Collapsed, semiAutoOn: true),
            ComposerSlotKeyModel.For(SuggestionSlotState.Keyboard, semiAutoOn: true),
            "both are «panel is not up» — whether a keyboard fills the slot is irrelevant to the key");

    // ---- Garbage in resolves, it never renders ----------------------------

    [Test]
    public void UnknownState_OffersTheSuggestions_SameAsCollapsed()
        // The panel states are whitelisted, so anything unrecognised lands on the ✦ branch — the
        // same reading SuggestionSlotStateMachine.Normalise gives it (out-of-range == Collapsed).
        // Written the other way round ("not Collapsed and not Keyboard ⇒ panel is up") the key
        // would offer a keyboard for a slot that has none, and every test above would still pass.
        => AssertIdentical(
            ComposerSlotKeyModel.For(SuggestionSlotState.Collapsed, semiAutoOn: true),
            ComposerSlotKeyModel.For(NotATenant, semiAutoOn: true),
            "an unrecognised state must read as «panel is not up», never as «panel is up»");

    [Test]
    public void UnknownState_IsStillHiddenInAutoMode()
        => Assert.IsFalse(ComposerSlotKeyModel.For(NotATenant, semiAutoOn: false).Visible,
            "«Авто» outranks the state — a bad state is no excuse to render the key");

    // ---- «Авто» hides the key everywhere ----------------------------------

    [Test]
    public void AutoMode_HidesTheKeyFromEveryState()
    {
        foreach (var state in AllStates)
        {
            var style = ComposerSlotKeyModel.For(state, semiAutoOn: false);
            Assert.IsFalse(style.Visible, $"«Авто» has no suggestions to reach — no key in {state}");
        }
    }

    [Test]
    public void AutoMode_HiddenStyleIsDeterministic()
    {
        // Don't-care fields are still fixed values, so the struct is comparable and never carries
        // a half-built appearance into a view that forgets to check Visible.
        var hidden = new SlotKeyStyle(false, SlotKeyGlyph.Sparkle, false, ThemeRole.InkTertiary);
        foreach (var state in AllStates)
            AssertIdentical(hidden, ComposerSlotKeyModel.For(state, semiAutoOn: false),
                $"hidden style must be identical from {state}");
    }

    // ---- The spec's invariants, asserted as correlations -------------------

    [Test]
    public void TintCircle_AppearsExactlyWithTheSparkleGlyph()
    {
        // Over the rows where the key exists — the hidden rows' glyph/tint are don't-care and are
        // pinned by AutoMode_HiddenStyleIsDeterministic instead.
        foreach (var state in AllStates)
        {
            var style = ComposerSlotKeyModel.For(state, semiAutoOn: true);
            Assert.AreEqual(style.Glyph == SlotKeyGlyph.Sparkle, style.TintCircle,
                $"the circle promotes ✦ and only ✦ ({state})");
        }
    }

    [Test]
    public void PositiveInk_RidesTheSameSparkleDestination()
    {
        // The promotion is ONE decision, not two: ✦ ⇒ circle AND PositiveInk, ⌨ ⇒ neither. The
        // rows above pin ink for the four states they name; this states it as the rule, so a state
        // added to the enum later cannot arrive half-promoted (tinted circle, tertiary glyph).
        foreach (var state in AllStates)
        {
            var style = ComposerSlotKeyModel.For(state, semiAutoOn: true);
            var expected = style.Glyph == SlotKeyGlyph.Sparkle ? ThemeRole.PositiveInk : ThemeRole.InkTertiary;
            Assert.AreEqual(expected, style.Ink, $"ink must follow the destination ({state})");
        }
    }

    [Test]
    public void TintCircleAlpha_IsAWashNotAFill()
    {
        Assert.Greater(ComposerSlotKeyModel.TintCircleAlpha, 0f,
            "an invisible circle is no affordance");
        // The circle is PositiveInk at this alpha under a PositiveInk glyph at full alpha — same
        // hue, so the alpha gap IS the separation. Anywhere near half and the circle reads as a
        // filled chip with a barely-visible glyph on it, which is the look this const exists to
        // prevent; the bound has to sit low enough to catch that drift, not merely below 1.
        Assert.Less(ComposerSlotKeyModel.TintCircleAlpha, 0.2f,
            "it is a soft wash behind the glyph, never a filled chip");
    }
}
