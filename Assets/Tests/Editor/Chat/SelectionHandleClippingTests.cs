using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Selection pins render on their own overlay canvas, so nothing clips them to
/// the field they belong to — SelectionOverlay drives a RectMask2D from this
/// rect instead. These pin the rect math: nested masks must both apply, and a
/// field scrolled off its page must produce an empty window so the mask culls
/// the pins rather than flipping inside out.
/// </summary>
public class SelectionHandleClippingTests
{
    private static readonly Rect Card = Rect.MinMaxRect(100f, 500f, 900f, 800f);

    // Card inside the page: the pins may only draw where both allow.
    [Test]
    public void IntersectNarrowsToTheOverlap()
    {
        var page = Rect.MinMaxRect(0f, 400f, 1000f, 700f);
        var overlap = SelectionHandleClipping.Intersect(Card, page);

        Assert.AreEqual(100f, overlap.xMin);
        Assert.AreEqual(900f, overlap.xMax);
        Assert.AreEqual(500f, overlap.yMin);
        Assert.AreEqual(700f, overlap.yMax);
        Assert.IsFalse(SelectionHandleClipping.IsEmpty(overlap));
    }

    [Test]
    public void ContainedRectIsUnchanged()
    {
        var everything = Rect.MinMaxRect(0f, 0f, 2000f, 2000f);
        var overlap = SelectionHandleClipping.Intersect(Card, everything);

        Assert.AreEqual(Card.xMin, overlap.xMin);
        Assert.AreEqual(Card.yMax, overlap.yMax);
    }

    // Card scrolled entirely off the page — no overlap at all.
    [Test]
    public void DisjointMasksProduceAnEmptyWindow()
    {
        var offscreen = Rect.MinMaxRect(0f, 0f, 1000f, 100f);
        var overlap = SelectionHandleClipping.Intersect(Card, offscreen);

        Assert.IsTrue(SelectionHandleClipping.IsEmpty(overlap));
    }

    // Inside-out is the shape a disjoint intersection actually produces, and
    // it must read as empty rather than as a huge rect.
    [Test]
    public void InsideOutRectCountsAsEmpty()
    {
        Assert.IsTrue(SelectionHandleClipping.IsEmpty(Rect.MinMaxRect(900f, 800f, 100f, 500f)));
    }

    [Test]
    public void EdgeTouchingMasksAreEmpty()
    {
        var below = Rect.MinMaxRect(100f, 200f, 900f, 500f);   // shares only y = 500
        Assert.IsTrue(SelectionHandleClipping.IsEmpty(SelectionHandleClipping.Intersect(Card, below)));
    }

    [Test]
    public void ZeroRectIsEmpty()
    {
        Assert.IsTrue(SelectionHandleClipping.IsEmpty(Rect.zero));
    }

    // ── VisibleScreenRect: measured from the FIELD, not from TMP's Text Area ──

    private GameObject _card;

    [TearDown]
    public void TearDown()
    {
        if (_card != null) Object.DestroyImmediate(_card);
        _card = null;
    }

    /// Card (masked, 980x360) > Input (980x800, the scroll content) >
    /// Text Area (masked, inset 40/32) — the real Bot Settings card shape.
    private TMP_InputField BuildCard()
    {
        LogAssert.ignoreFailingMessages = true;   // uGUI Selectable list, EditMode

        _card = new GameObject("Card", typeof(RectTransform), typeof(RectMask2D));
        var cardRt = (RectTransform)_card.transform;
        cardRt.sizeDelta = new Vector2(980f, 360f);

        var inputGo = new GameObject("Input", typeof(RectTransform));
        inputGo.transform.SetParent(_card.transform, false);
        var inputRt = (RectTransform)inputGo.transform;
        inputRt.sizeDelta = new Vector2(980f, 800f);
        var field = inputGo.AddComponent<TMP_InputField>();

        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(inputGo.transform, false);
        var areaRt = (RectTransform)textArea.transform;
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.sizeDelta = new Vector2(-80f, -64f);   // inset 40 per side, 32 top/bottom
        field.textViewport = areaRt;

        return field;
    }

    // The pins belong to the field: clipping them at TMP's inset Text Area cut
    // the dots well inside the visible card.
    [Test]
    public void ClipsAtTheFieldWidth_NotTheInsetTextArea()
    {
        var clip = SelectionHandleClipping.VisibleScreenRect(BuildCard());

        Assert.AreEqual(980f, clip.width, 0.01f,
            "Text Area's 40px-per-side inset must not narrow the pin clip.");
    }

    // The card's own mask still applies — that is what keeps a pin from
    // hanging below the card while its line is at the last visible row.
    [Test]
    public void ClipsToTheCardHeight_NotTheTallScrollContent()
    {
        var clip = SelectionHandleClipping.VisibleScreenRect(BuildCard());

        Assert.AreEqual(360f, clip.height, 0.01f,
            "The card's RectMask2D bounds the pins vertically, not the 800px content.");
    }

    [Test]
    public void NullField_YieldsAnEmptyWindow()
    {
        Assert.IsTrue(SelectionHandleClipping.IsEmpty(SelectionHandleClipping.VisibleScreenRect(null)));
    }
}
