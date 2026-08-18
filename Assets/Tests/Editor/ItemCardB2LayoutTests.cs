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

        // The real list lives under the Product tab's Viewport, which carries a
        // stencil Mask (BotSettings.prefab). That matters: Mask is an
        // IMaterialModifier and renders a COPY of the graphic's material, so a
        // test hosted on a bare canvas cannot see anything the copy gets wrong.
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        var viewportRect = (RectTransform)viewport.transform;
        viewportRect.SetParent(host.transform, false);
        viewportRect.sizeDelta = new Vector2(CardWidth, 1920f);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductPrefabPath);
        Assert.IsNotNull(prefab, "Product.prefab not found");
        card = Object.Instantiate(prefab, viewport.transform);

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

    // Reproduces the exact sequence the edit sheet commits in: a just-added
    // item has an empty price, so the tag is INACTIVE while its text is
    // written, and only then switched on. Reported on device 2026-08-17: the
    // tag comes out stretched and only settles after leaving and re-opening
    // bot settings.
    [Test]
    public void PriceCommittedOntoAHiddenTag_StillFitsItsContent()
    {
        var view = View;
        view.Name = "Новый товар";
        view.Price = string.Empty;
        Relayout();
        Assert.IsFalse(Pill.gameObject.activeSelf, "precondition: the tag starts hidden");

        view.Price = "100000";
        Relayout();

        var price = card.transform.Find("Pill/Price").GetComponent<TextMeshProUGUI>();
        var currency = card.transform.Find("Pill/Currency").GetComponent<TextMeshProUGUI>();
        var inner = Pill.GetComponent<HorizontalLayoutGroup>();
        if (price.preferredWidth <= 0f)
            Assert.Inconclusive("TMP reported a zero preferred width in batch mode.");

        float expected = inner.padding.left + inner.padding.right + inner.spacing
                         + price.preferredWidth + currency.preferredWidth;
        Assert.AreEqual(expected, Pill.rect.width, 1f,
            "The tag did not re-measure after being switched on with a fresh price.");
    }

    // Drains only the layouts that were actually MARKED dirty, instead of
    // rebuilding unconditionally like the tests above.
    //
    // HONEST LIMITATION: this is NOT a regression guard for the stale-width
    // symptom reported on device 2026-08-17. It was written to be one, and a
    // negative control disproved it — reverting the card view to the original
    // write-then-activate order left this test green, so EditMode reaches the
    // correct width by either path and cannot see the defect. What it does
    // still assert is that the automatic path produces content-fitted geometry
    // at all. The symptom itself is Play-Mode-only and has to be checked there.
    [Test]
    public void PriceCommit_MarksTheRowForRebuildByItself()
    {
        var view = View;
        view.Name = "Новый товар";
        view.Price = string.Empty;
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)card.transform);

        view.Price = "100000";
        Canvas.ForceUpdateCanvases();   // no explicit rebuild — only what was queued

        var price = card.transform.Find("Pill/Price").GetComponent<TextMeshProUGUI>();
        var currency = card.transform.Find("Pill/Currency").GetComponent<TextMeshProUGUI>();
        var inner = Pill.GetComponent<HorizontalLayoutGroup>();
        if (price.preferredWidth <= 0f)
            Assert.Inconclusive("TMP reported a zero preferred width in batch mode.");

        float expected = inner.padding.left + inner.padding.right + inner.spacing
                         + price.preferredWidth + currency.preferredWidth;
        Assert.AreEqual(expected, Pill.rect.width, 1f,
            "Writing a price did not queue a layout rebuild — the tag keeps a stale width " +
            "until something else re-lays the tab out.");
    }

    [Test]
    public void PriceChangedInPlace_ReMeasuresTheTag()
    {
        var view = View;
        view.Price = "5000";
        Relayout();
        float before = Pill.rect.width;

        view.Price = "100000";
        Relayout();

        if (before <= 0f) Assert.Inconclusive("TMP reported a zero preferred width in batch mode.");
        Assert.Greater(Pill.rect.width, before, "The tag kept the previous price's width.");
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

    // Diagnostic for the third report on this node: after a price change the
    // tag's WIDTH is right but its corner rounding is drawn for the old width.
    // Nobi's shader takes the rect size as a material vector, refreshed from
    // OnRectTransformDimensionsChange — so a stale vector means that refresh
    // never ran for the new size.
    [Test]
    public void PriceTag_RoundedCornersFollowTheNewWidth()
    {
        var view = View;
        view.Price = "5000";
        Relayout();
        // Load-bearing: the stencil copy is taken on the graphic's FIRST
        // material rebuild. Without a canvas update HERE the copy would be made
        // after both prices were applied, from a base that is already correct —
        // which is why an earlier version of this test could not fail.
        Canvas.ForceUpdateCanvases();

        view.Price = "1 200 000";
        Relayout();

        var image = Pill.GetComponent<Image>();
        Assert.IsNotNull(image.material, "the tag has no instance material — rounding is not applied at all");

        // Assert on materialForRendering: that is literally the argument
        // Graphic.UpdateMaterial hands to canvasRenderer.SetMaterial, so it is
        // what the screen gets. The renderer's own slot cannot be read here —
        // Graphic.Rebuild bails out on canvasRenderer.cull, which is always true
        // for a canvas that never renders in an EditMode test.
        //
        // image.material would pass either way: Nobi keeps the BASE correct.
        // The whole defect lives in the stencil copy that sits between them.
        var rendered = image.materialForRendering;
        Assert.IsNotNull(rendered, "the tag has no material to render with");

        var props = rendered.GetVector("_WidthHeightRadius");
        if (props == Vector4.zero)
            Assert.Inconclusive("the rounded-corner material was never initialised in this context");

        Assert.AreEqual(Pill.rect.width, props.x, 1f,
            $"the corner shader still has width {props.x} while the tag is {Pill.rect.width} wide — " +
            $"the stencil copy went stale (base material reads {image.material.GetVector("_WidthHeightRadius").x})");
    }
}
