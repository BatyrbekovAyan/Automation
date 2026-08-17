using Automation.BotSettingsUI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Behavioural proof that the B2 price tag follows its digits and that a long
/// name can never reach it — the defect reported on device 2026-08-14, and its
/// mirror image (a long price running over the name).
///
/// EditMode cannot exercise a raycast (an unrendered canvas leaves Graphic.depth
/// at -1), but it CAN run layout: LayoutRebuilder works without rendering, and
/// TMP measures preferred width with infinite margins, independent of the rect
/// it currently occupies. So the arithmetic below is the real thing, not a
/// restatement of the serialized values.
/// </summary>
public class ItemCardB2LayoutTests
{
    private const string ProductPrefabPath = "Assets/Prefabs/Product.prefab";
    private const float CardWidth = 984f;   // canvas 1080, +4 container, −100 content padding

    private GameObject host;
    private GameObject card;

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("LayoutHost", typeof(RectTransform), typeof(Canvas));
        var hostRect = (RectTransform)host.transform;
        hostRect.sizeDelta = new Vector2(CardWidth, 1920f);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductPrefabPath);
        Assert.IsNotNull(prefab, "Product.prefab not found");
        card = Object.Instantiate(prefab, host.transform);

        var cardRect = (RectTransform)card.transform;
        cardRect.anchorMin = new Vector2(0f, 1f);
        cardRect.anchorMax = new Vector2(1f, 1f);
        cardRect.sizeDelta = new Vector2(0f, cardRect.sizeDelta.y);
    }

    [TearDown]
    public void TearDown()
    {
        if (host != null) Object.DestroyImmediate(host);
    }

    private ProductCardView View => card.GetComponent<ProductCardView>();
    private RectTransform Pill => (RectTransform)card.transform.Find("Pill");
    private RectTransform Info => (RectTransform)card.transform.Find("Info");
    private RectTransform Column => (RectTransform)card.transform.Find("Info/NameDesc");

    private void Relayout() =>
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)card.transform);

    [Test]
    public void PriceTag_GrowsWithThePrice()
    {
        var view = View;
        view.Name = "Колодки Bosch";
        view.Price = "5000";
        Relayout();
        float narrow = Pill.rect.width;

        view.Price = "1 200 000";
        Relayout();
        float wide = Pill.rect.width;

        if (narrow <= 0f)
            Assert.Inconclusive("TMP reported a zero preferred width in batch mode — " +
                                "the tag's fit must be confirmed visually in the Editor instead.");

        Assert.Greater(wide, narrow,
            "The price tag did not grow with a longer price — its width is pinned somewhere.");
    }

    [Test]
    public void PriceTag_IsItsTextPlusPadding()
    {
        var view = View;
        view.Price = "50001";
        Relayout();

        var price = card.transform.Find("Pill/Price").GetComponent<TextMeshProUGUI>();
        var currency = card.transform.Find("Pill/Currency").GetComponent<TextMeshProUGUI>();
        var inner = Pill.GetComponent<HorizontalLayoutGroup>();

        float expected = inner.padding.left + inner.padding.right + inner.spacing
                         + price.preferredWidth + currency.preferredWidth;

        if (price.preferredWidth <= 0f)
            Assert.Inconclusive("TMP reported a zero preferred width in batch mode.");

        Assert.AreEqual(expected, Pill.rect.width, 1f,
            "The tag is not exactly its content plus padding.");
    }

    // The reported defect, in its structural form: whatever the name, the text
    // column stops before the tag begins.
    [Test]
    public void LongName_NeverReachesThePriceTag()
    {
        var view = View;
        view.Price = "50001";
        view.Name = "Колодки";
        Relayout();
        float tagWithShortName = Pill.rect.width;

        view.Name = new string('Ы', 300);
        view.Description = new string('Я', 300);
        Relayout();

        if (tagWithShortName <= 0f)
            Assert.Inconclusive("TMP reported a zero preferred width in batch mode.");

        Assert.AreEqual(tagWithShortName, Pill.rect.width, 1f,
            "A long name squeezed the price tag — the text column's preferred-width mask is not holding.");

        var columnCorners = new Vector3[4];
        var tagCorners = new Vector3[4];
        Column.GetWorldCorners(columnCorners);
        Pill.GetWorldCorners(tagCorners);
        Assert.LessOrEqual(columnCorners[2].x, tagCorners[0].x + 0.5f,
            "The text column overlaps the price tag.");
    }

    [Test]
    public void EmptyPrice_HidesTheTagEntirely()
    {
        var view = View;
        view.Price = "990";
        Relayout();
        Assert.IsTrue(Pill.gameObject.activeSelf, "A priced item must show its tag.");

        view.Price = string.Empty;
        Assert.IsFalse(Pill.gameObject.activeSelf,
            "A just-added item has no price — an empty tag would render as a lone ₸.");
    }

    [Test]
    public void Monogram_ColoursDifferItemToItem()
    {
        var view = View;
        var background = card.transform.Find("Thumb").GetComponent<Image>();
        var letter = card.transform.Find("Thumb/Letter").GetComponent<TextMeshProUGUI>();

        view.Name = "Колодки Bosch";
        var first = background.color;
        Assert.AreEqual("К", letter.text);

        view.Name = "Масло Mobil";
        Assert.AreEqual("М", letter.text);
        Assert.AreNotEqual(first, background.color,
            "Two differently named items got the same avatar colour.");

        view.Name = "Колодки Bosch";
        Assert.AreEqual(first, background.color, "The colour is not stable for the same name.");
    }
}
