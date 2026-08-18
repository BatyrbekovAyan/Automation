using Automation.BotSettingsUI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pins the sketch-007 «B2 Ценник» card produced by
/// Tools/BotSettings/Restyle Item Card (B2), on BOTH prefabs — the product and
/// service cards are separate object trees, and a builder that resolved only
/// one of them would leave the app half converted with nothing complaining.
///
/// The two rules worth reading before changing anything here are the price
/// tag's unpinned width and the text column's masked preferred width; together
/// they are what makes a long name and a long price coexist. See
/// ItemCardB2RestyleBuilder's summary for why.
/// </summary>
public class ItemCardB2Tests
{
    private static readonly string[] CardPrefabPaths =
    {
        "Assets/Prefabs/Product.prefab",
        "Assets/Prefabs/Service.prefab",
    };

    private static GameObject Card(string path)
    {
        var card = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(card, $"{path} not found");
        return card;
    }

    private static Object Ref(Object owner, string property) =>
        new SerializedObject(owner).FindProperty(property)?.objectReferenceValue;

    [Test]
    public void Card_IsARowOfThreeCells()
    {
        foreach (var path in CardPrefabPaths)
        {
            var card = Card(path);
            var row = card.GetComponent<HorizontalLayoutGroup>();
            Assert.IsNotNull(row, $"{path}: the card root is not a horizontal row.");
            Assert.IsTrue(row.childControlWidth && row.childControlHeight,
                $"{path}: the row must size its cells, or they keep authored widths (the 2026-08-14 defect).");
            Assert.IsFalse(row.childForceExpandWidth,
                $"{path}: force-expand would stretch the price tag across the leftover space.");
            Assert.AreEqual(36, row.padding.left, $"{path}: card padding drifted.");
            Assert.AreEqual(33f, row.spacing, 0.5f, $"{path}: cell spacing drifted.");

            Assert.IsNull(card.transform.Find("Info/Price"),
                $"{path}: the price is still in the text column — it belongs in the tag.");
        }
    }

    // The mask that makes the whole thing work: a preferred width of 0 at
    // LayoutElement's priority hides the text column's real appetite, so the
    // row's total preferred never exceeds the card and uGUI never enters the
    // shrink path that would squeeze the price tag.
    [Test]
    public void TextColumn_IsTheOnlyFlexibleCell()
    {
        foreach (var path in CardPrefabPaths)
        {
            var card = Card(path);
            var info = card.transform.Find("Info").GetComponent<LayoutElement>();
            Assert.IsNotNull(info, $"{path}: the text column has no LayoutElement.");
            Assert.AreEqual(0f, info.preferredWidth, 0.01f,
                $"{path}: a non-zero preferred width lets a long name squeeze the price tag.");
            Assert.AreEqual(1f, info.flexibleWidth, 0.01f,
                $"{path}: the column must absorb the leftover width.");
            Assert.Greater(info.minWidth, 0f,
                $"{path}: without a minimum, a pathological price would eat the name entirely.");

            var pill = card.transform.Find("Pill").GetComponent<LayoutElement>();
            Assert.AreEqual(0f, pill.flexibleWidth, 0.01f, $"{path}: the tag must not stretch.");
        }
    }

    [Test]
    public void PriceTag_SizesItselfToItsContent()
    {
        foreach (var path in CardPrefabPaths)
        {
            var card = Card(path);
            var pill = card.transform.Find("Pill");
            Assert.IsNotNull(pill, $"{path}: no price tag — run Tools/BotSettings/Restyle Item Card (B2).");

            var element = pill.GetComponent<LayoutElement>();
            Assert.Less(element.preferredWidth, 0f,
                $"{path}: LayoutElement outranks a LayoutGroup, so any preferred width here freezes " +
                "the tag at one size and the content-fitting breaks.");
            Assert.AreEqual(72f, element.preferredHeight, 0.5f, $"{path}: tag height drifted.");

            Assert.IsNotNull(pill.GetComponent<RoundedCornerMaskSync>(),
                $"{path}: the tag resizes under a stencil Mask, so its corner material must be " +
                "re-pushed on resize or the rounding keeps rendering the previous width.");

            var inner = pill.GetComponent<HorizontalLayoutGroup>();
            Assert.IsNotNull(inner, $"{path}: the tag has no group to publish its preferred width.");
            Assert.IsTrue(inner.childControlWidth, $"{path}: the tag must measure its own text.");
            Assert.AreEqual(30, inner.padding.left, $"{path}: tag padding drifted.");
            Assert.AreEqual(30, inner.padding.right, $"{path}: tag padding drifted.");
        }
    }

    // card.Price is written to PlayerPrefs and into three n8n payloads verbatim.
    [Test]
    public void Currency_StaysOutOfTheValue()
    {
        foreach (var path in CardPrefabPaths)
        {
            var card = Card(path);
            var price = card.transform.Find("Pill/Price")?.GetComponent<TextMeshProUGUI>();
            var currency = card.transform.Find("Pill/Currency")?.GetComponent<TextMeshProUGUI>();
            Assert.IsNotNull(price, $"{path}: no Price label inside the tag.");
            Assert.IsNotNull(currency, $"{path}: the ₸ must stay a separate label.");
            Assert.AreEqual("₸", currency.text);
            StringAssert.DoesNotContain("₸", price.text ?? string.Empty,
                $"{path}: the glyph leaked into the value.");
        }
    }

    [Test]
    public void Monogram_OwnsItsOwnColours()
    {
        foreach (var path in CardPrefabPaths)
        {
            var card = Card(path);
            var mono = card.transform.Find("Thumb");
            Assert.IsNotNull(mono, $"{path}: no monogram square.");

            var monogram = mono.GetComponent<ItemCardMonogram>();
            Assert.IsNotNull(monogram, $"{path}: the square has no ItemCardMonogram.");
            Assert.IsNotNull(Ref(monogram, "background"), $"{path}: monogram background not wired.");
            Assert.IsNotNull(Ref(monogram, "letter"), $"{path}: monogram letter not wired.");

            // Two colour owners would flatten the monogram on Theme.Changed.
            Assert.IsNull(mono.GetComponent<ThemedColor>(),
                $"{path}: the monogram square must not carry a ThemedColor.");
            Assert.IsNull(mono.Find("Letter").GetComponent<ThemedColor>(),
                $"{path}: the monogram letter must not carry a ThemedColor.");

            var size = mono.GetComponent<LayoutElement>();
            Assert.AreEqual(120f, size.preferredWidth, 0.5f, $"{path}: monogram size drifted.");
        }
    }

    [Test]
    public void CardView_KeepsEveryReference()
    {
        foreach (var path in CardPrefabPaths)
        {
            var card = Card(path);
            Component view = card.GetComponent<ProductCardView>();
            if (view == null) view = card.GetComponent<ServiceCardView>();
            Assert.IsNotNull(view, $"{path}: the card view component is gone.");

            foreach (var property in new[] { "nameLabel", "priceLabel", "descLabel", "rootButton", "monogram", "pricePill" })
                Assert.IsNotNull(Ref(view, property),
                    $"{path}: '{property}' is not wired — a builder re-created the node instead of moving it.");
        }
    }

    [Test]
    public void Chevron_IsGoneFromTheRow()
    {
        foreach (var path in CardPrefabPaths)
        {
            var chevron = Card(path).transform.Find("Chevron");
            if (chevron != null)
                Assert.IsFalse(chevron.gameObject.activeSelf,
                    $"{path}: B2 has no chevron — the whole card is the target.");
        }
    }
}
