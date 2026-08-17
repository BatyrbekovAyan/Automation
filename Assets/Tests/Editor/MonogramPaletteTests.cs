using Automation.BotSettingsUI;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The pure seam behind the B2 card's avatar square: same name → same colour,
/// forever, with no stored state. Everything the card shows is derived here.
/// </summary>
public class MonogramPaletteTests
{
    [Test]
    public void SameName_AlwaysGetsTheSameHue()
    {
        Assert.AreEqual(MonogramPalette.IndexFor("Колодки Bosch"), MonogramPalette.IndexFor("Колодки Bosch"));
        Assert.AreEqual(MonogramPalette.IndexFor("Масло Mobil 1"), MonogramPalette.IndexFor("Масло Mobil 1"));
    }

    [Test]
    public void IndexIsAlwaysInsideThePalette()
    {
        foreach (var name in new[] { "", "   ", "A", "Колодки", "1 200 000", "🔧 ключ", new string('Я', 500) })
        {
            int index = MonogramPalette.IndexFor(name);
            Assert.GreaterOrEqual(index, 0, $"'{name}' produced a negative index");
            Assert.Less(index, MonogramPalette.Hues.Length, $"'{name}' overflowed the palette");
        }
    }

    // A plain character sum would hand every anagram the same colour, which is
    // exactly the case a catalog hits ("Шина 205" vs "205 Шина").
    [Test]
    public void ReorderedNames_DoNotCollide()
    {
        Assert.AreNotEqual(MonogramPalette.IndexFor("Шина 205"), MonogramPalette.IndexFor("205 Шина"));
    }

    [Test]
    public void Letter_IsTheFirstVisibleCharacterUppercased()
    {
        Assert.AreEqual("К", MonogramPalette.LetterFor("колодки"));
        Assert.AreEqual("M", MonogramPalette.LetterFor("  mobil"));
        Assert.AreEqual("5", MonogramPalette.LetterFor("5W-30"));
    }

    // key[0] on an emoji name would render half a surrogate pair.
    [Test]
    public void Letter_KeepsAstralCharactersWhole()
    {
        var letter = MonogramPalette.LetterFor("🔧 ключ");
        Assert.AreEqual("🔧", letter);
        Assert.AreEqual(2, letter.Length, "The surrogate pair was split.");
    }

    [Test]
    public void EmptyName_FallsBackToABullet()
    {
        Assert.AreEqual("•", MonogramPalette.LetterFor(null));
        Assert.AreEqual("•", MonogramPalette.LetterFor(""));
        Assert.AreEqual("•", MonogramPalette.LetterFor("   "));
    }

    [Test]
    public void ColoursAreMixedAgainstTheLiveTheme()
    {
        var lightSurface = Color.white;
        var darkSurface = new Color(0.09f, 0.11f, 0.14f);

        var onLight = MonogramPalette.Background("Колодки", lightSurface);
        var onDark = MonogramPalette.Background("Колодки", darkSurface);
        Assert.AreNotEqual(onLight, onDark, "The square ignores the theme surface.");

        // A tint, not a flood: the square must still read as part of the card.
        Assert.Greater(onLight.r + onLight.g + onLight.b, darkSurface.r + darkSurface.g + darkSurface.b,
            "The light-theme square came out darker than a dark surface.");
    }

    [Test]
    public void InkStaysDistinctFromTheBackground()
    {
        foreach (var name in new[] { "Колодки", "Масло", "Фильтр", "Аккумулятор", "Свечи", "Диски" })
        {
            var background = MonogramPalette.Background(name, Color.white);
            var ink = MonogramPalette.Ink(name, Color.black);
            float delta = Mathf.Abs(background.grayscale - ink.grayscale);
            Assert.Greater(delta, 0.25f, $"'{name}': the letter barely separates from its square.");
        }
    }
}
