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

    // ── The dot's overhang: a one-line field is not allowed to cut its own pins ──

    /// The chat composer: an 834x74 field — ONE line tall — inside the scroll
    /// frame that hugs it. Nothing here is scrolled or occluded, so both dots
    /// must be whole.
    private TMP_InputField BuildComposer()
    {
        LogAssert.ignoreFailingMessages = true;   // uGUI Selectable list, EditMode

        _card = new GameObject("Input", typeof(RectTransform), typeof(RectMask2D));
        var frameRt = (RectTransform)_card.transform;
        frameRt.sizeDelta = new Vector2(820f, 74f);

        var inputGo = new GameObject("InputField", typeof(RectTransform));
        inputGo.transform.SetParent(_card.transform, false);
        ((RectTransform)inputGo.transform).sizeDelta = new Vector2(834f, 74f);
        var field = inputGo.AddComponent<TMP_InputField>();

        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(inputGo.transform, false);
        var areaRt = (RectTransform)textArea.transform;
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.sizeDelta = new Vector2(-48f, 0f);   // inset 24 per side, NO vertical inset
        field.textViewport = areaRt;

        return field;
    }

    // 44pt composer text, ascender → descender.
    private const float ComposerLineHeight = 50f;

    /// The reported bug: the window was the field's own box, so it cut both
    /// dots roughly in half and scrolling never helped — the box never grows.
    [Test]
    public void ComposerSizedField_WindowHoldsTheWholeDot()
    {
        var window = SelectionHandleClipping.WithHandleOverhang(
            SelectionHandleClipping.VisibleScreenRect(BuildComposer()),
            SelectionHandleView.DotOverhang);

        // The line sits centred in the field, so the dot reaches this far from
        // the window's centre.
        float dotReach = ComposerLineHeight / 2f + SelectionHandleView.DotOverhang;

        Assert.GreaterOrEqual(window.height / 2f, dotReach,
            "A one-line field must not clip the dot its own pins draw past the line.");
    }

    [Test]
    public void HandleOverhang_GrowsVerticallyOnly()
    {
        var padded = SelectionHandleClipping.WithHandleOverhang(Card, 27f);

        Assert.AreEqual(Card.height + 54f, padded.height, 0.01f);
        Assert.AreEqual(Card.width, padded.width, 0.01f, "The dot overhangs the line, not the column.");
        Assert.AreEqual(Card.yMin - 27f, padded.yMin, 0.01f);
        Assert.AreEqual(Card.yMax + 27f, padded.yMax, 0.01f);
    }

    // A field scrolled off its page must still cull its pins — padding an empty
    // window back into existence would float them over unrelated UI.
    [Test]
    public void HandleOverhang_LeavesAnEmptyWindowEmpty()
    {
        Assert.IsTrue(SelectionHandleClipping.IsEmpty(
            SelectionHandleClipping.WithHandleOverhang(Rect.zero, 27f)));
        Assert.IsTrue(SelectionHandleClipping.IsEmpty(
            SelectionHandleClipping.WithHandleOverhang(Rect.MinMaxRect(900f, 800f, 100f, 500f), 27f)));
    }

    // The card still bounds the pins — it just grants them the dot's own slack,
    // so a pin on the last visible row shows a whole dot instead of a sliver.
    [Test]
    public void CardWindow_KeepsItsBoundsPlusTheDotSlack()
    {
        var window = SelectionHandleClipping.WithHandleOverhang(
            SelectionHandleClipping.VisibleScreenRect(BuildCard()), SelectionHandleView.DotOverhang);

        Assert.AreEqual(360f + 2f * SelectionHandleView.DotOverhang, window.height, 0.01f);
    }
}
