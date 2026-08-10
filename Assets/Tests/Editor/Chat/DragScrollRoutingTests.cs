using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pins the drag-ownership policy behind DragShield: a text card keeps the
/// gesture only while it has something hidden to reveal, otherwise the page
/// scrolls. Regression cover for the Bot Settings defect where a drag inside
/// a scrollable card moved the page instead of the card's own text.
/// </summary>
public class DragScrollRoutingTests
{
    [Test]
    public void HiddenText_WhenContentTallerThanViewport()
    {
        Assert.IsTrue(DragScrollRouting.HasHiddenText(800f, 360f));
    }

    [Test]
    public void NoHiddenText_WhenContentMatchesViewport()
    {
        Assert.IsFalse(DragScrollRouting.HasHiddenText(360f, 360f));
    }

    // Layout rounding routinely leaves content a hair taller with nothing
    // actually hidden; a sub-pixel overflow must not steal page scrolling.
    [Test]
    public void SubPixelOverflow_IsNotHiddenText()
    {
        Assert.IsFalse(DragScrollRouting.HasHiddenText(360.5f, 360f));
        Assert.IsTrue(DragScrollRouting.HasHiddenText(361.5f, 360f));
    }

    [Test]
    public void OverflowingCard_KeepsTheGesture()
    {
        Assert.AreEqual(
            DragScrollRouting.Target.InnerText,
            DragScrollRouting.Resolve(hasInnerScroll: true, hasHiddenText: true, hasPageScroll: true));
    }

    [Test]
    public void CardWithNothingHidden_YieldsToThePage()
    {
        Assert.AreEqual(
            DragScrollRouting.Target.Page,
            DragScrollRouting.Resolve(hasInnerScroll: true, hasHiddenText: false, hasPageScroll: true));
    }

    // No page behind the card: the card keeps the gesture rather than
    // dropping it, so a drag is never silently swallowed.
    [Test]
    public void NoPageScroll_FallsBackToTheCard()
    {
        Assert.AreEqual(
            DragScrollRouting.Target.InnerText,
            DragScrollRouting.Resolve(hasInnerScroll: true, hasHiddenText: false, hasPageScroll: false));
    }

    [Test]
    public void NoScrollAnywhere_ResolvesToNone()
    {
        Assert.AreEqual(
            DragScrollRouting.Target.None,
            DragScrollRouting.Resolve(hasInnerScroll: false, hasHiddenText: false, hasPageScroll: false));
    }

    [Test]
    public void ShieldWithoutOwnScroll_StillFeedsThePage()
    {
        Assert.AreEqual(
            DragScrollRouting.Target.Page,
            DragScrollRouting.Resolve(hasInnerScroll: false, hasHiddenText: false, hasPageScroll: true));
    }

    // ── ResolveTarget: which ScrollRect a drag on a field belongs to ──────
    // A plain input field has no DragShield, so TMP_InputField is the nearest
    // drag handler and the gesture dies there unless the field forwards it.

    private GameObject _root;

    [TearDown]
    public void TearDown()
    {
        if (_root != null) Object.DestroyImmediate(_root);
        _root = null;
    }

    // page(scrollable) > card(sized by args) > field
    private Transform BuildForm(float cardContentHeight, float cardViewportHeight)
    {
        _root = new GameObject("Page", typeof(RectTransform));
        var page = MakeScroll(_root, contentHeight: 4000f, viewportHeight: 1000f);

        var cardGo = new GameObject("Card", typeof(RectTransform));
        cardGo.transform.SetParent(page.content, false);
        MakeScroll(cardGo, cardContentHeight, cardViewportHeight);

        var field = new GameObject("Field", typeof(RectTransform));
        field.transform.SetParent(cardGo.transform, false);
        return field.transform;
    }

    private static ScrollRect MakeScroll(GameObject host, float contentHeight, float viewportHeight)
    {
        var scroll = host.AddComponent<ScrollRect>();
        scroll.vertical = true;

        var viewport = (RectTransform)host.transform;
        viewport.sizeDelta = new Vector2(500f, viewportHeight);

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(host.transform, false);
        var content = (RectTransform)contentGo.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, contentHeight);

        scroll.viewport = viewport;
        scroll.content = content;
        return scroll;
    }

    [Test]
    public void FieldInsideAScrollableCard_ScrollsTheCard()
    {
        var field = BuildForm(cardContentHeight: 900f, cardViewportHeight: 300f);
        Assert.AreEqual("Card", DragScrollRouting.ResolveTarget(field).name);
    }

    // The whole point of the report "dragging over an input doesn't scroll":
    // a card with nothing hidden must pass the gesture up to the page.
    [Test]
    public void FieldInsideANonScrollableCard_ScrollsThePage()
    {
        var field = BuildForm(cardContentHeight: 300f, cardViewportHeight: 300f);
        Assert.AreEqual("Page", DragScrollRouting.ResolveTarget(field).name);
    }

    [Test]
    public void HorizontalOnlyScroll_IsNotAVerticalTarget()
    {
        var field = BuildForm(cardContentHeight: 900f, cardViewportHeight: 300f);
        field.GetComponentInParent<ScrollRect>().vertical = false;

        Assert.AreEqual("Page", DragScrollRouting.ResolveTarget(field).name);
    }

    [Test]
    public void NoScrollAnywhere_ResolvesToNull()
    {
        _root = new GameObject("Lonely", typeof(RectTransform));
        Assert.IsNull(DragScrollRouting.ResolveTarget(_root.transform));
    }

    [Test]
    public void NullTransform_ResolvesToNull()
    {
        Assert.IsNull(DragScrollRouting.ResolveTarget(null));
    }
}
