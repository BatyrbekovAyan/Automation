using System.Collections.Generic;
using NUnit.Framework;

// EditMode coverage for SuggestionSlotHeight — the persisted "how tall is the keyboard slot"
// measurement behind the sketch-003 keyboard-substitute panel. Pure via the injected prefs
// seams: no PlayerPrefs, no Unity objects. Pins: the fallback before any measurement, the
// sanity window that keeps read glitches (0 mid-animation, full-screen strays) out of the
// store, and the no-redundant-write rule.
public class SuggestionSlotHeightTests
{
    private Dictionary<string, float> _store;
    private int _saveCount;

    [SetUp]
    public void SetUp()
    {
        _store = new Dictionary<string, float>();
        _saveCount = 0;
        SuggestionSlotHeight.LoadPref = (key, def) => _store.TryGetValue(key, out float v) ? v : def;
        SuggestionSlotHeight.SavePref = (key, value) => { _store[key] = value; _saveCount++; };
    }

    [TearDown]
    public void TearDown() => SuggestionSlotHeight.ResetForTests();

    // --- Remembered ---------------------------------------------------------

    [Test]
    public void Remembered_WithNoStoredValue_ReturnsFallback()
        => Assert.AreEqual(SuggestionSlotHeight.FallbackCanvasPx, SuggestionSlotHeight.Remembered);

    [Test]
    public void Remembered_AfterRemember_ReturnsTheMeasurement()
    {
        SuggestionSlotHeight.Remember(906f);
        Assert.AreEqual(906f, SuggestionSlotHeight.Remembered);
    }

    [Test]
    public void Remembered_WithGarbageStoredValue_FallsBack()
    {
        _store["SuggestSlotHeightCanvasPx"] = 12f;   // a pre-fix build stored a mid-animation read
        Assert.AreEqual(SuggestionSlotHeight.FallbackCanvasPx, SuggestionSlotHeight.Remembered);
    }

    // --- Remember sanity window --------------------------------------------

    [TestCase(0f)]
    [TestCase(120f)]                 // mid-animation partial height
    [TestCase(2400f)]                // full-screen stray
    [TestCase(-50f)]
    [TestCase(float.NaN)]
    public void Remember_OutsideSanityWindow_IsIgnored(float garbage)
    {
        SuggestionSlotHeight.Remember(garbage);
        Assert.AreEqual(0, _saveCount);
        Assert.AreEqual(SuggestionSlotHeight.FallbackCanvasPx, SuggestionSlotHeight.Remembered);
    }

    [TestCase(SuggestionSlotHeight.MinValidCanvasPx)]
    [TestCase(SuggestionSlotHeight.MaxValidCanvasPx)]
    [TestCase(836f)]
    public void Remember_InsideSanityWindow_Persists(float measured)
    {
        SuggestionSlotHeight.Remember(measured);
        Assert.AreEqual(measured, SuggestionSlotHeight.Remembered);
    }

    [Test]
    public void Remember_SameValueTwice_WritesOnce()
    {
        SuggestionSlotHeight.Remember(906f);
        SuggestionSlotHeight.Remember(906f);
        Assert.AreEqual(1, _saveCount);
    }

    [Test]
    public void Remember_NewValue_OverwritesTheOld()
    {
        SuggestionSlotHeight.Remember(906f);
        SuggestionSlotHeight.Remember(972f);   // taller keyboard (e.g. QuickType row appeared)
        Assert.AreEqual(972f, SuggestionSlotHeight.Remembered);
        Assert.AreEqual(2, _saveCount);
    }
}
