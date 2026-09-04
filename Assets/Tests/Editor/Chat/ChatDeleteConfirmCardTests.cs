using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Exercises ConfirmCardFitter against a REAL DeleteChatConfirmPanel card — built
/// to Main.unity's numbers with the real SF Pro Text Regular asset, and measured
/// through the same inactive → active sequence the popup lives through.
///
/// This card is the same chassis as ReplyModeConfirmPopup but a DIFFERENT defect,
/// which is why it needs its own file (2026-09-04):
///
///   • The «Авто» popup's bug was a wrapped TITLE drawing over a body that never
///     moved. Here the title is the fixed «Удалить чат?» — 304u in a 760u column,
///     one line forever — so it is not even wired, and the fit runs title-less.
///   • This card's bug was in the AUTHORED geometry: the Body box ran from -150
///     to -290 while the bottom-anchored buttons start at -280. Grow-by-overflow
///     preserves authored gaps by design, so ConfirmCardFitter alone would have
///     reproduced that overlap at every size. The box was corrected to 86 first.
///   • This is the only confirm body in the app assembled from data the app does
///     not control, so it is the only one that genuinely runs long. The line
///     counts below come from real chat names, not from invented copy.
///
/// ClearanceHolds_AtEveryLineCount is the test that fails if the 140 ever comes
/// back, with or without the fitter.
/// </summary>
public class ChatDeleteConfirmCardTests
{
    private const string FontPath = "Assets/TextMesh Pro/Fonts/SFProText-Regular SDF.asset";

    // Main.unity: Screen_Messanger/ChatsPanel/DeleteChatConfirmPanel/Content.
    private const float CardWidth = 820f;
    private const float CardHeight = 460f;
    private const float TextWidth = 760f;   // a fixed sizeDelta.x here, not a stretch inset
    private const float TitleTop = 60f;
    private const float TitleHeight = 70f;
    private const float BodyTop = 150f;
    private const float BodyHeight = 86f;

    // Both buttons are 110u tall at y = 70, so they own the card's bottom 180u.
    // Everything above that belongs to the text block.
    private const float ButtonBlock = 180f;

    /// <summary>The clearance the corrected geometry buys, and that growth must preserve.</summary>
    private const float Clearance = 44f;

    // 32pt SF Pro Text line = 39.36u (lineHeight 275.516 / pointSize 224 = 1.22998 em).
    private const float BodyLine = 39.36f;

    // Real chat names, measured in the 760u column. Each is the SHORTEST realistic
    // name that reaches its line count, so they double as the wrap thresholds.
    private const string OneLineName = "Мама";
    private const string TwoLineName = "Айгерим Нурлановна";
    private const string ThreeLineName = "Доставка цветов Астана — заказы, оплата и вопросы клиентов 2026";
    private const string FourLineName =
        "Автозапчасти Алматы: опт и розница, приём заказов и доставка по Казахстану, гарантия и возвраты";

    private GameObject _canvasGo;
    private GameObject _popup;
    private RectTransform _card;
    private TextMeshProUGUI _title, _body;
    private ConfirmCardFitter.Baseline _baseline;

    [TearDown]
    public void TearDown()
    {
        if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
        _baseline = default;
    }

    // --- the authored card, before anything is fitted ---------------------

    /// <summary>
    /// The premise the whole fix rests on, stated as arithmetic: the authored box
    /// must END ABOVE the buttons. This is exactly what was false before —
    /// 150 + 140 = 290 against a button edge at 280 — and no amount of fitting
    /// could have repaired it, because growth preserves the authored gap.
    /// </summary>
    [Test]
    public void AuthoredBodyBox_EndsAboveTheButtons()
    {
        float bodyBottom = BodyTop + BodyHeight;
        float buttonTop = CardHeight - ButtonBlock;

        Assert.Less(bodyBottom, buttonTop,
            "The body box must not reach into the buttons' band. It did: the card shipped with " +
            "a 140u box at -150, ending 10u below the buttons' top edge.");
        Assert.AreEqual(Clearance, buttonTop - bodyBottom, 0.001f,
            "44u — the same clearance ReplyModeConfirmPopup was authored with.");
    }

    /// <summary>
    /// Why no title reference is serialized, and why ConfirmCardFitter was relaxed
    /// to accept a null one rather than the scene being re-wired.
    /// </summary>
    [Test]
    public void Title_IsOneLine_AndCanNeverNeedTheFit()
    {
        Build(BodyText(OneLineName));
        Show();

        float preferred = _title.GetPreferredValues(TextWidth, 32767f).y;

        Assert.LessOrEqual(preferred, TitleHeight,
            "«Удалить чат?» is fixed copy that fits its 70u box. If this ever fails, the title " +
            "must be serialized and passed to Fit — not have its box enlarged.");
        Assert.AreEqual(54.12f, preferred, 1.5f, "One 44pt SF Pro line");
    }

    // --- the measurement itself -------------------------------------------

    [Test]
    public void TextColumnIs760_TheWidthEveryMeasurementUses()
    {
        Build(BodyText(OneLineName));
        Show();

        Assert.AreEqual(TextWidth, _body.rectTransform.rect.width, 0.5f,
            "A fixed sizeDelta.x, so the width is valid on the activation frame with no layout pass");
    }

    [TestCase(OneLineName, 1)]
    [TestCase(TwoLineName, 2)]
    [TestCase(ThreeLineName, 3)]
    [TestCase(FourLineName, 4)]
    public void RealChatNames_WrapToTheExpectedLineCount(string chatName, int lines)
    {
        Build(BodyText(chatName));
        Show();

        float preferred = _body.GetPreferredValues(TextWidth, 32767f).y;

        Assert.AreEqual(lines * BodyLine, preferred, 1.5f,
            $"«{chatName}» should wrap the body to {lines} line(s). If this drifts, either the " +
            "copy in ChatDeleteConfirm.BodyText changed or the font did — check before re-baselining.");
    }

    /// <summary>
    /// The titleless branch of the copy — the row had no resolvable name. It is the
    /// shortest string this card can show, so it must never move anything.
    /// </summary>
    [Test]
    public void TitlelessCopy_IsOneLine()
    {
        Build(BodyText(null));
        Show();

        Assert.AreEqual(BodyLine, _body.GetPreferredValues(TextWidth, 32767f).y, 1.5f);
    }

    // --- what the fitter does with it -------------------------------------

    [TestCase(OneLineName)]
    [TestCase(TwoLineName)]
    public void NameThatFitsTheAuthoredBox_LeavesTheCardExactlyAsAuthored(string chatName)
    {
        Build(BodyText(chatName));
        Show();
        Fit();

        Assert.AreEqual(CardHeight, _card.sizeDelta.y, 0.001f,
            "One and two-line bodies are the common case — they must be byte-identical after the fit");
        Assert.AreEqual(BodyHeight, _body.rectTransform.sizeDelta.y, 0.001f);
        Assert.AreEqual(-BodyTop, _body.rectTransform.anchoredPosition.y, 0.001f);
    }

    [Test]
    public void LongGroupName_GrowsTheBoxAndTheCard_InsteadOfOverflowing()
    {
        Build(BodyText(ThreeLineName));
        Show();
        Fit();

        Assert.AreEqual(119f, _body.rectTransform.sizeDelta.y, 0.001f,
            "Three 39.36u lines are 119u once ceiled");
        Assert.AreEqual(493f, _card.sizeDelta.y, 0.001f,
            "The card absorbs the 33u the box gained");
        Assert.AreEqual(-BodyTop, _body.rectTransform.anchoredPosition.y, 0.001f,
            "With no title to grow, the body must not move — only get taller");
    }

    /// <summary>
    /// The regression guard. Every line count must leave the buttons the same 44u
    /// below the body box that the scene authored — which is only true because the
    /// card is centre-pivoted and the buttons hang off its bottom edge.
    ///
    /// Run this against the pre-fix scene (Body height 140) and it fails at every
    /// case with a clearance of -10, fitted or not. That is the point: it fails on
    /// the geometry, not on the growth.
    /// </summary>
    [TestCase(OneLineName)]
    [TestCase(TwoLineName)]
    [TestCase(ThreeLineName)]
    [TestCase(FourLineName)]
    public void ClearanceHolds_AtEveryLineCount(string chatName)
    {
        Build(BodyText(chatName));
        Show();
        Fit();

        float bodyBottom = -_body.rectTransform.anchoredPosition.y + _body.rectTransform.sizeDelta.y;
        float buttonTop = _card.sizeDelta.y - ButtonBlock;

        Assert.AreEqual(Clearance, buttonTop - bodyBottom, 0.001f,
            $"«{chatName}»: growing the card must buy the room, never take it from the gap above " +
            "the buttons. A negative number here is the 2026-09-04 overlap.");
    }

    /// <summary>
    /// The text is Middle-aligned inside its box, which is what hid the defect for
    /// so long: a short body floats in the middle and only a full box reaches the
    /// edges. So the drawn TEXT has to be checked too, not just the rect.
    /// </summary>
    [TestCase(OneLineName)]
    [TestCase(TwoLineName)]
    [TestCase(ThreeLineName)]
    [TestCase(FourLineName)]
    public void DrawnText_NeverReachesTheButtons(string chatName)
    {
        Build(BodyText(chatName));
        Show();
        Fit();

        RectTransform bodyRt = _body.rectTransform;
        float boxTop = -bodyRt.anchoredPosition.y;
        float boxCentre = boxTop + bodyRt.sizeDelta.y / 2f;
        float textHeight = _body.GetPreferredValues(TextWidth, 32767f).y;

        // Middle vertical alignment: the text is centred in whatever box it has.
        float textTop = boxCentre - textHeight / 2f;
        float textBottom = boxCentre + textHeight / 2f;

        Assert.Less(textBottom, _card.sizeDelta.y - ButtonBlock,
            $"«{chatName}»: the last line of the body drew over the button labels");
        Assert.Greater(textTop, TitleTop + TitleHeight,
            $"«{chatName}»: the first line of the body drew up into the title's box");
    }

    // --- the null-title relaxation ----------------------------------------

    /// <summary>
    /// ConfirmCardFitter used to early-return when the title was null, which would
    /// have made this whole fix a silent no-op. It now reads a null title as zero
    /// authored height AND zero measured height, so the title term cancels and the
    /// card grows by the body's overflow alone — never by more.
    /// </summary>
    [Test]
    public void NullTitle_ContributesExactlyZeroGrowth()
    {
        Build(BodyText(FourLineName));
        Show();
        Fit();

        float bodyGrowth = _body.rectTransform.sizeDelta.y - BodyHeight;

        Assert.Greater(bodyGrowth, 0f, "Sanity: a four-line name does overflow the authored box");
        Assert.AreEqual(bodyGrowth, _card.sizeDelta.y - CardHeight, 0.001f,
            "The card grew by the body's overflow and nothing else — a null title adds no height");
        Assert.AreEqual(-BodyTop, _body.rectTransform.anchoredPosition.y, 0.001f,
            "Only a title's growth pushes the body down, and there is no title here");
    }

    [Test]
    public void CaptureWithNullTitle_RecordsTheAuthoredCard()
    {
        Build(BodyText(FourLineName));

        // ChatDeleteConfirm.Awake captures while the card is still untouched.
        ConfirmCardFitter.Capture(_card, null, _body, ref _baseline);
        Assert.IsTrue(_baseline.Captured,
            "A null title must not stop the capture — the card and body are what the baseline needs");

        Show();
        Fit();

        Assert.AreEqual(532f, _card.sizeDelta.y, 0.001f,
            "An early capture must produce the same solve as capturing lazily on the first fit");
    }

    // --- shared card, repeated shows --------------------------------------

    [Test]
    public void FittingTwice_DoesNotCompound()
    {
        Build(BodyText(ThreeLineName));
        Show();
        Fit();

        float afterFirst = _card.sizeDelta.y;
        Fit();

        Assert.AreEqual(afterFirst, _card.sizeDelta.y, 0.001f,
            "Every Ask re-fits the same card — the baseline must stay the authored geometry");
    }

    [Test]
    public void DeletingAShortChatAfterALongOne_ReturnsToTheAuthoredSize()
    {
        Build(BodyText(ThreeLineName));
        Show();
        Fit();
        Assert.Greater(_card.sizeDelta.y, CardHeight, "Sanity: the card is grown at this point");

        _body.text = BodyText(OneLineName);
        Fit();

        Assert.AreEqual(CardHeight, _card.sizeDelta.y, 0.001f,
            "The popup is reused for every chat — a grown card must shrink back for the next one");
        Assert.AreEqual(BodyHeight, _body.rectTransform.sizeDelta.y, 0.001f);
    }

    /// <summary>
    /// The fit's precondition, stated as a failing case: measured before the popup
    /// is activated, TMP reports nothing and the card stays exactly as authored —
    /// which is indistinguishable from a body that fits. Hence Ask shows first.
    /// </summary>
    [Test]
    public void FittingBeforeTheShow_LeavesTheCardAlone()
    {
        Build(BodyText(FourLineName));   // deliberately NOT shown

        Fit();

        Assert.AreEqual(CardHeight, _card.sizeDelta.y, 0.001f,
            "TMP cannot measure on a GameObject that has never been active, so the fit fails open. " +
            "This is why ChatDeleteConfirm.Ask calls PopupUI.Show BEFORE Fit.");
    }

    // ---------------------------------------------------------------------

    private static string BodyText(string chatName) => ChatDeleteConfirm.BodyText(chatName);

    private void Fit() => ConfirmCardFitter.Fit(_card, null, _body, ref _baseline);

    /// <summary>Activates the popup the way PopupUI.Show does — the fit's precondition.</summary>
    private void Show() => _popup.SetActive(true);

    private void Build(string body)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Assert.IsNotNull(font, $"Missing {FontPath} — this test measures the real shipping font");

        _canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(RectTransform));
        ((RectTransform)_canvasGo.transform).sizeDelta = new Vector2(1080f, 1920f);

        _popup = new GameObject("DeleteChatConfirmPanel", typeof(RectTransform));
        _popup.transform.SetParent(_canvasGo.transform, false);
        var popupRt = (RectTransform)_popup.transform;
        popupRt.anchorMin = Vector2.zero;
        popupRt.anchorMax = Vector2.one;
        popupRt.sizeDelta = Vector2.zero;

        // Deactivated BEFORE its children exist, not after. The scene saves this panel
        // inactive, so its TextMeshProUGUI components have never run OnEnable and have
        // never parsed their text — which is the state the fit's ordering rule is about.
        // Building active and switching off afterwards would quietly pre-initialise them
        // and make FittingBeforeTheShow_LeavesTheCardAlone unfalsifiable.
        _popup.SetActive(false);

        var cardGo = new GameObject("Content", typeof(RectTransform));
        cardGo.transform.SetParent(_popup.transform, false);
        _card = (RectTransform)cardGo.transform;
        _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(0.5f, 0.5f);
        _card.sizeDelta = new Vector2(CardWidth, CardHeight);

        _title = NewText("Title", font, "Удалить чат?", 44f, FontStyles.Bold, TitleTop, TitleHeight);
        _body = NewText("Body", font, body, 32f, FontStyles.Normal, BodyTop, BodyHeight);
    }

    private TextMeshProUGUI NewText(string name, TMP_FontAsset font, string text, float size,
        FontStyles style, float top, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_card, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(TextWidth, height);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;   // horizontally centred, vertically Middle
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.margin = Vector4.zero;
        tmp.raycastTarget = false;
        return tmp;
    }
}
