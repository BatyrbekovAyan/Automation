using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Exercises ConfirmCardFitter against a REAL card built to Main.unity's numbers
/// with the real SF Pro Text Semibold asset, so the part ConfirmCardLayoutTests
/// cannot cover — TextMeshPro's own measurement — is pinned too.
///
/// Three things are being proved here, each of which was a genuine risk when the
/// fix was written (2026-09-04):
///
///   • TMP really does report ~2 lines for the per-chat title in the popup's
///     640u column. The pure seam's constants are derived from the font's own
///     metrics; this is the check that the derivation matches the engine.
///   • The measurement is valid on the ACTIVATION frame. The popup is inactive
///     between shows, so the fit runs on a GameObject that has just been
///     switched on and has had no layout pass — the tests reproduce exactly that
///     sequence: the panel is deactivated BEFORE its texts are created (so TMP
///     has genuinely never initialised, the scene's real state), then set text,
///     SetActive, fit. Build-active-then-deactivate would pre-initialise TMP and
///     make the ordering rule unfalsifiable (review, 2026-09-05).
///   • A wrapped title ends up above the body rather than inside it.
///
/// If a Unity/TMP upgrade changes line-height or preferred-height accounting,
/// TitleWrapsToTwoLines_AtThePopupsRealWidth is the test that says so.
/// </summary>
public class ConfirmCardFitterTests
{
    private const string FontPath = "Assets/TextMesh Pro/Fonts/SFProText-Semibold SDF.asset";

    // Main.unity: Screen_Messanger/ReplyModeConfirmPopup/Content and its children.
    private const float CardWidth = 720f;
    private const float CardHeight = 440f;
    private const float SideInset = -80f;   // sizeDelta.x on Title and Body
    private const float TitleTop = 52f;
    private const float TitleHeight = 64f;
    private const float BodyTop = 118f;
    private const float BodyHeight = 130f;

    private const string ShortTitle = "Включить авто-режим?";
    private const string LongTitle = "Включить авто-режим в этом чате?";
    private const string ChatBody =
        "Бот будет сам отвечать этому клиенту. Выключить можно в любой момент — этой же кнопкой.";
    private const string HeaderBody =
        "Бот будет отвечать клиентам сам. Выключить можно в любой момент — этой же кнопкой.";

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

    // --- the measurement itself -------------------------------------------

    [Test]
    public void TitleWrapsToTwoLines_AtThePopupsRealWidth()
    {
        Build(LongTitle, ChatBody);

        Assert.AreEqual(640f, _title.rectTransform.rect.width, 0.5f,
            "A stretch-anchored text derives its width from the card, so it is valid BEFORE activation " +
            "and without a layout pass — the property the activation-frame measurement relies on");

        Show();

        Assert.AreEqual(640f, _title.rectTransform.rect.width, 0.5f,
            "The text column is the card minus its 80u of side inset — the width every measurement uses");

        float preferred = _title.GetPreferredValues(640f, 32767f).y;

        Assert.AreEqual(100.24f, preferred, 1.5f,
            "Two 42pt SF Pro lines are 2 x 50.12u. If this drifts, the constants in " +
            "ConfirmCardLayoutTests were derived from a font or a TMP version that no longer ships.");
        Assert.Greater(preferred, TitleHeight,
            "This overflow of the authored 64u box is the whole bug");
    }

    [Test]
    public void ShortTitleStillFitsItsAuthoredBox()
    {
        Build(ShortTitle, HeaderBody);
        Show();

        float preferred = _title.GetPreferredValues(640f, 32767f).y;

        Assert.LessOrEqual(preferred, TitleHeight,
            "The chats-header title must keep fitting, or the fix would move a popup that was already correct");
    }

    /// <summary>
    /// The per-chat body is the tightest string in the whole dialog: its longest
    /// wrapped line, «клиенту. Выключить можно в любой», measures 638.9u in a
    /// 640u column — 1.1u of margin, 0.17%. It still fits three lines today, so
    /// the card does not move; edit that sentence by one word and it wraps to
    /// four and the card legitimately grows to 473u. This test carries the copy
    /// so that a copy edit fails HERE, naming the string that changed, rather
    /// than in a test named after the title.
    /// </summary>
    [Test]
    public void PerChatBody_StillFitsThreeLines_ByAHair()
    {
        Build(ShortTitle, ChatBody);
        Show();

        float preferred = _body.GetPreferredValues(640f, 32767f).y;

        Assert.LessOrEqual(preferred, BodyHeight,
            "The per-chat body has ~1u of horizontal margin — if it now wraps to a fourth line, " +
            "the copy changed. The card grows correctly either way; update this test, do not widen the box.");
        Assert.AreEqual(121.72f, preferred, 1.5f, "Three 34pt lines are 3 x 40.57u");
    }

    // --- what the fitter does with it -------------------------------------

    [Test]
    public void ShortTitle_LeavesTheCardExactlyAsAuthored()
    {
        // The pairing that actually occurs on the chats header — the short title
        // with its own body, not the per-chat one.
        Build(ShortTitle, HeaderBody);
        Show();
        Fit();

        Assert.AreEqual(CardHeight, _card.sizeDelta.y, 0.001f);
        Assert.AreEqual(TitleHeight, _title.rectTransform.sizeDelta.y, 0.001f);
        Assert.AreEqual(-BodyTop, _body.rectTransform.anchoredPosition.y, 0.001f);
        Assert.AreEqual(BodyHeight, _body.rectTransform.sizeDelta.y, 0.001f);
    }

    [Test]
    public void WrappedTitle_PushesTheBodyClearOfIt_AndGrowsTheCard()
    {
        Build(LongTitle, ChatBody);
        Show();
        Fit();

        float titleBottom = TitleTop + _title.rectTransform.sizeDelta.y;
        float bodyTop = -_body.rectTransform.anchoredPosition.y;

        Assert.GreaterOrEqual(bodyTop, titleBottom,
            "The reported bug: the title's second line drew over the body's first line");
        Assert.Greater(bodyTop, BodyTop, "The body must have moved down");
        Assert.Greater(_card.sizeDelta.y, CardHeight, "The card must have grown to absorb the move");
    }

    [Test]
    public void WrappedTitle_KeepsTheAuthoredClearanceAboveTheButtons()
    {
        // Buttons are bottom-anchored and own the card's bottom 148u (104u tall at y = 44).
        const float buttonBlock = 148f;
        float authored = (CardHeight - buttonBlock) - (BodyTop + BodyHeight);

        Build(LongTitle, ChatBody);
        Show();
        Fit();

        float grown = (_card.sizeDelta.y - buttonBlock)
                      - (-_body.rectTransform.anchoredPosition.y + _body.rectTransform.sizeDelta.y);

        Assert.AreEqual(authored, grown, 0.001f,
            "Growing the card must buy the room, never take it from the gap above the buttons");
    }

    [Test]
    public void FittingTwice_DoesNotCompound()
    {
        Build(LongTitle, ChatBody);
        Show();
        Fit();

        float cardAfterFirst = _card.sizeDelta.y;
        float bodyAfterFirst = _body.rectTransform.anchoredPosition.y;

        Fit();

        Assert.AreEqual(cardAfterFirst, _card.sizeDelta.y, 0.001f,
            "Every show re-fits — the baseline must be the authored geometry, not the previous result");
        Assert.AreEqual(bodyAfterFirst, _body.rectTransform.anchoredPosition.y, 0.001f);
    }

    [Test]
    public void SameCard_SwitchingBackToShortCopy_ReturnsToTheAuthoredSize()
    {
        // The popup is shared: the per-chat chip and the chats header raise the
        // same object with different copy, in either order.
        Build(LongTitle, ChatBody);
        Show();
        Fit();
        Assert.Greater(_card.sizeDelta.y, CardHeight);

        _title.text = ShortTitle;
        Fit();

        Assert.AreEqual(CardHeight, _card.sizeDelta.y, 0.001f,
            "A grown card must shrink back when the next caller's title fits again");
        Assert.AreEqual(-BodyTop, _body.rectTransform.anchoredPosition.y, 0.001f);
    }

    /// <summary>
    /// The ordering rule stated as a failing case, for THIS popup's stretch-inset
    /// shape: fitted before the show, the never-active texts cannot be measured,
    /// the card stays exactly as authored — and the fitter says so, once per text.
    /// Remove the isActiveAndEnabled precondition in ConfirmCardFitter and the
    /// warning expectation fails, because an uninitialised TMP measures a small
    /// positive number rather than 0 and the failure turns silent.
    /// </summary>
    [Test]
    public void FittingBeforeTheShow_LeavesTheCardAlone_AndWarns()
    {
        Build(LongTitle, ChatBody);   // deliberately NOT shown
        LogAssert.Expect(LogType.Warning, new Regex("ConfirmCardFitter"));   // title
        LogAssert.Expect(LogType.Warning, new Regex("ConfirmCardFitter"));   // body

        Fit();

        Assert.AreEqual(CardHeight, _card.sizeDelta.y, 0.001f,
            "This is why ReplyModeToggleBinder.ShowConfirm calls PopupUI.Show BEFORE FitConfirmCard");
        Assert.AreEqual(-BodyTop, _body.rectTransform.anchoredPosition.y, 0.001f);
    }

    // --- the baseline is the authored card, and only ever that ------------

    [Test]
    public void CaptureBeforeShowing_ReadsTheAuthoredCard()
    {
        Build(LongTitle, ChatBody);

        // The binder captures at wire time, while the popup is still inactive.
        ConfirmCardFitter.Capture(_card, _title, _body, ref _baseline);
        Assert.IsTrue(_baseline.Captured);

        Show();
        Fit();

        Assert.AreEqual(477f, _card.sizeDelta.y, 0.001f,
            "An early capture must produce the same solve as capturing lazily on the first fit");
    }

    [Test]
    public void Capture_IsOneShot_SoAGrownCardCanNeverBecomeTheBaseline()
    {
        Build(LongTitle, ChatBody);
        Show();
        Fit();
        Assert.Greater(_card.sizeDelta.y, CardHeight, "Sanity: the card is grown at this point");

        // A second capture against the grown card must change nothing — this is
        // what stops the card ratcheting larger show after show.
        ConfirmCardFitter.Capture(_card, _title, _body, ref _baseline);

        _title.text = ShortTitle;
        Fit();

        Assert.AreEqual(CardHeight, _card.sizeDelta.y, 0.001f);
    }

    // --- the twin's fit has a precondition that reads as redundant ---------

    /// <summary>
    /// Source guard, in the style of TailOutlineShaderTests: BotActivationConfirm
    /// builds its own card at runtime on an overlay canvas, so EditMode cannot
    /// reach it without leaving a static panel behind for every later test.
    ///
    /// What it protects: that class's BuildTmp turns wrapping OFF for every text
    /// it creates (correctly — it also builds the two button labels), and the
    /// title re-enables it on the next line. That line looks redundant beside the
    /// body's identical one and is exactly what a tidy-up deletes. Without it the
    /// title can never report more than one line, Solve returns the authored
    /// geometry forever, and the twin's Fit becomes a permanent silent no-op —
    /// while a long title's failure mode reverts to running off the card
    /// SIDEWAYS, which is worse than the overlap this whole fix exists to remove.
    /// </summary>
    [Test]
    public void BotsPageTwin_TitleWrappingStaysEnabled()
    {
        string path = Path.Combine(Application.dataPath, "Scripts/Main/BotActivationConfirm.cs");
        Assert.IsTrue(File.Exists(path), $"BotActivationConfirm.cs moved — update this guard. Looked in {path}");

        // Comments stripped first: a commented-out line must not satisfy the guard.
        string source = Regex.Replace(File.ReadAllText(path), @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);

        Assert.IsTrue(
            Regex.IsMatch(source,
                @"^\s*titleTmp\.(enableWordWrapping\s*=\s*true|textWrappingMode\s*=\s*TextWrappingModes\.Normal)",
                RegexOptions.Multiline),
            "BotActivationConfirm's title no longer enables wrapping. BuildTmp disables it for every text " +
            "it creates, so without that line the title can never wrap, ConfirmCardFitter.Fit becomes a " +
            "permanent no-op there, and a long title runs off the side of the card instead.");
    }

    // ---------------------------------------------------------------------

    private void Fit() => ConfirmCardFitter.Fit(_card, _title, _body, ref _baseline);

    /// <summary>Activates the popup the way PopupUI.Show does — the fit's precondition.</summary>
    private void Show() => _popup.SetActive(true);

    private void Build(string title, string body)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Assert.IsNotNull(font, $"Missing {FontPath} — this test measures the real shipping font");

        _canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(RectTransform));
        ((RectTransform)_canvasGo.transform).sizeDelta = new Vector2(1080f, 1920f);

        _popup = new GameObject("ReplyModeConfirmPopup", typeof(RectTransform));
        _popup.transform.SetParent(_canvasGo.transform, false);
        var popupRt = (RectTransform)_popup.transform;
        popupRt.anchorMin = Vector2.zero;
        popupRt.anchorMax = Vector2.one;
        popupRt.sizeDelta = Vector2.zero;

        // Deactivated BEFORE its children exist: the scene saves the popup inactive, so its
        // texts have never run OnEnable or parsed their text — the state the ordering rule is
        // about. Building active and switching off afterwards pre-initialises TMP and makes
        // FittingBeforeTheShow_LeavesTheCardAlone_AndWarns pass for the wrong reason.
        _popup.SetActive(false);

        var cardGo = new GameObject("Content", typeof(RectTransform));
        cardGo.transform.SetParent(_popup.transform, false);
        _card = (RectTransform)cardGo.transform;
        _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(0.5f, 0.5f);
        _card.sizeDelta = new Vector2(CardWidth, CardHeight);

        _title = NewText("Title", font, title, 42f, FontStyles.Bold,
            TextAlignmentOptions.Top, TitleTop, TitleHeight);
        _body = NewText("Body", font, body, 34f, FontStyles.Normal,
            TextAlignmentOptions.Center, BodyTop, BodyHeight);
    }

    private TextMeshProUGUI NewText(string name, TMP_FontAsset font, string text, float size,
        FontStyles style, TextAlignmentOptions align, float top, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_card, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(SideInset, height);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.margin = Vector4.zero;
        tmp.raycastTarget = false;
        return tmp;
    }
}
