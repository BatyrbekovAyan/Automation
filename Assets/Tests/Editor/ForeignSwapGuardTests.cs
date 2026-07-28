using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Pins the focused-field foreign-swap detection: a wholesale replacement of
/// the focused field's text with exactly another field's content is bleed;
/// typing, clearing, and unrelated edits never are.
/// </summary>
public class ForeignSwapGuardTests
{
    private static readonly List<string> Others = new List<string>
    {
        "+7 707 123 45 67",
        "Пн–Сб 09:00–19:00",
        "",
    };

    [Test]
    public void OtherFieldsText_LandingWholesale_IsForeign()
    {
        Assert.IsTrue(ForeignSwapGuard.IsForeignSwap(
            "+7 707 123 45 67", "info@", Others));
        Assert.IsTrue(ForeignSwapGuard.IsForeignSwap(
            "Пн–Сб 09:00–19:00", "", Others));
    }

    [Test]
    public void SingleKeystroke_NeverForeign_EvenWhenTextsBecomeEqual()
    {
        Assert.IsFalse(ForeignSwapGuard.IsForeignSwap(
            "+7 707 123 45 67", "+7 707 123 45 6", Others));
    }

    [Test]
    public void ClearingTheField_IsAlwaysLegitimate()
    {
        Assert.IsFalse(ForeignSwapGuard.IsForeignSwap(
            "", "г. Алматы, ул. Толе би 285", Others));
    }

    [Test]
    public void UnrelatedWholesaleChange_IsNotForeign()
    {
        Assert.IsFalse(ForeignSwapGuard.IsForeignSwap(
            "совершенно другой текст", "", Others));
    }

    [Test]
    public void EmptyOtherField_NeverMatches()
    {
        // "" is in Others, but an empty newText is clearing, and a non-empty
        // newText can't equal "" — the empty sibling must never trigger.
        Assert.IsFalse(ForeignSwapGuard.IsForeignSwap("x", "", Others));
    }

    [Test]
    public void CapitalizedOrPaddedReplay_StillForeign()
    {
        // iOS may auto-capitalize or pad the replayed content on insertion.
        Assert.IsTrue(ForeignSwapGuard.IsForeignSwap(
            "ПН–СБ 09:00–19:00 ", "x", Others));
    }

    [Test]
    public void NoChange_NotForeign()
    {
        Assert.IsFalse(ForeignSwapGuard.IsForeignSwap(
            "+7 707 123 45 67", "+7 707 123 45 67", Others));
    }

    [Test]
    public void NullSafe()
    {
        Assert.IsFalse(ForeignSwapGuard.IsForeignSwap(null, null, Others));
        Assert.IsFalse(ForeignSwapGuard.IsForeignSwap("a", "b", null));
    }
}
