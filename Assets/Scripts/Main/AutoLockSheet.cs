using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Learning B + the percent (spec 2026-10-22 §4.5): the soft lock's sheet, shown by AutoLockGate when the server says the bot is
/// not ready. Top to bottom: the title, the big percent, a bar filled to that percent with a mark — and «Авто» under it — at the
/// percent from which «Авто» opens, the body (what is missing), the two buttons. Built lazily on its own overlay canvas
/// (sortingOrder 3) exactly like BotActivationConfirm — the gate fires from four screens, and the house rule puts a scene sheet
/// inside the screen that raises it. «Понятно» (and Back) close it; «Включить всё равно» runs the caller's `proceed` (the ordinary
/// enable path: its confirm, then CommitMode) exactly once. Buttons are wired ONCE through PopupUI.WireFingerUp; the pending
/// action, the percent, the fill, the mark and the texts are swapped per Show.
///
/// The ConfirmCardFitter grows the title and the body by their overflow and slides only the BODY down by the title's growth. The
/// title is built to be one line, but at 44pt Bold it is wider than its 624u column in SF Pro Medium/Semibold/Bold (633–657u;
/// only Regular, 572u, fits), so it can wrap — and everything this sheet stacks between the title and the body must then follow
/// it, or the second line overprints the percent. <see cref="SeatProgress"/> does that: zero movement when the title fits.
/// </summary>
public static class AutoLockSheet
{
    public const float CardWidth = 720f;
    public const float CardHeight = 664f;   // was 480: the percent, the bar and the mark's label now sit between the title and the body
    public const float TitleTop = 56f, TitleHeight = 60f, TitleSideInset = 48f;
    public const float PercentTop = 132f, PercentHeight = 84f;                  // 72pt Bold, AccentText
    public const float BarTop = 228f, BarHeight = 16f, BarSideInset = 56f;      // 608u wide: the body's text column
    public const float MarkerWidth = 4f, MarkerHeight = 32f;                    // centred on the bar, so it pokes out of the track
    public const float MarkerLabelTop = 256f, MarkerLabelHeight = 34f;          // 26pt, InkSecondary
    public const float BodyTop = 320f, BodyHeight = 180f, BodySideInset = 56f;
    public const float ButtonY = 44f, ButtonHeight = 96f, ButtonWidth = 300f;
    private const float MarkerLabelWidth = 120f;   // «Авто» at 26pt is ~60u wide; the text centres on the mark
    private const int OverlaySortingOrder = 3;   // above screens, below SelectionOverlay (4)

    private static GameObject panel;
    private static Image cardImage, dismissImage, enableImage, barImage, fillImage, markerImage;
    private static TextMeshProUGUI titleTmp, percentTmp, markerLabelTmp, bodyTmp, dismissLabel, enableLabel;
    private static RectTransform fillRt, markerRt, markerLabelRt;
    private static Action pending;
    private static bool hiding;
    private static ConfirmCardFitter.Baseline baseline;

    /// <summary>True while the sheet is up and not already leaving (Back router).</summary>
    public static bool IsShowing => panel != null && panel.activeSelf && !hiding;

    /// <summary>EditMode seam: the built panel (null until the first <see cref="Show"/>).</summary>
    internal static GameObject PanelForTests => panel;

    /// <summary>
    /// Raise the sheet for a bot whose learning is <paramref name="state"/> (the percent to draw, the percent where «Авто» opens,
    /// what the server says is still missing); <paramref name="proceed"/> runs only on «Включить всё равно».
    /// </summary>
    public static void Show(TMP_FontAsset font, LearningState state, bool perChat, Action proceed)
    {
        EnsureBuilt(font);
        pending = proceed;
        hiding = false;
        percentTmp.text = AutoReadinessCopy.Percent(state.Percent);
        bodyTmp.text = AutoReadinessCopy.SheetBody(state.UnlockAt, state.Capped, state.NeedChats, state.NeedDays, perChat);
        SetFill(state.Percent);
        Paint();
        PopupUI.Show(panel);
        // After Show: TMP measures only on an active object (ConfirmCardFitter's rule). Fit and seat in the same frame, so
        // nothing is ever rendered mis-seated.
        ConfirmCardFitter.Fit((RectTransform)cardImage.transform, titleTmp, bodyTmp, ref baseline);
        SeatProgress(state.UnlockAt, titleGrowth: Mathf.Max(0f, titleTmp.rectTransform.sizeDelta.y - TitleHeight));
    }

    /// <summary>«Понятно», a tap outside the card and the system Back: close, enable nothing.</summary>
    public static void Dismiss()
    {
        if (!IsShowing) return;
        pending = null;
        hiding = true;
        PopupUI.Hide(panel);
    }

    /// <summary>«Включить всё равно»: close, then run the ordinary enable path once.</summary>
    public static void EnableAnyway()
    {
        if (!IsShowing) return;
        Action proceed = pending;
        pending = null;
        hiding = true;
        PopupUI.Hide(panel);
        proceed?.Invoke();
    }

    /// <summary>EditMode seam: destroys the overlay and forgets every static, like a fresh play-mode enter.</summary>
    internal static void ResetForTests()
    {
        if (panel != null) UnityEngine.Object.DestroyImmediate(panel.transform.root.gameObject);
        panel = null;
        cardImage = dismissImage = enableImage = barImage = fillImage = markerImage = null;
        titleTmp = percentTmp = markerLabelTmp = bodyTmp = dismissLabel = enableLabel = null;
        fillRt = markerRt = markerLabelRt = null;
        pending = null;
        hiding = false;
        baseline = default;
    }

    private static void EnsureBuilt(TMP_FontAsset font)
    {
        if (panel != null) return;

        var canvasGo = new GameObject("AutoLockSheetCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0f;

        panel = NewChild(canvasGo.transform, "Panel", typeof(Image));
        StretchFull((RectTransform)panel.transform);
        var backdrop = panel.GetComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, PopupUI.DefaultBackdropAlpha);
        backdrop.raycastTarget = true;
        PopupUI.WireFingerUp(panel, Dismiss);   // tap outside the card = «Понятно»

        BuildCard(font);
        panel.SetActive(false);
    }

    private static void BuildCard(TMP_FontAsset font)
    {
        var card = NewChild(panel.transform, "Content", typeof(Image));
        var cardRt = (RectTransform)card.transform;
        cardRt.sizeDelta = new Vector2(CardWidth, CardHeight);
        cardImage = card.GetComponent<Image>();
        cardImage.raycastTarget = true;
        AddRounded(card, 40f);
        PopupUI.AbsorbEvents(cardImage);

        titleTmp = BuildTmp(card.transform, "Title", AutoReadinessCopy.SheetTitle, 44f, FontStyles.Bold, font);
        titleTmp.textWrappingMode = TextWrappingModes.Normal;
        SetTopStretch((RectTransform)titleTmp.transform, top: TitleTop, height: TitleHeight, sideInset: TitleSideInset);

        BuildProgress(card.transform, font);

        bodyTmp = BuildTmp(card.transform, "Body", string.Empty, 32f, FontStyles.Normal, font);   // the text is set per Show
        bodyTmp.enableWordWrapping = true;
        SetTopStretch((RectTransform)bodyTmp.transform, top: BodyTop, height: BodyHeight, sideInset: BodySideInset);

        (dismissImage, dismissLabel) = BuildButton(card.transform, "DismissButton", AutoReadinessCopy.Understood,
            font, anchoredX: -170f, Dismiss);
        (enableImage, enableLabel) = BuildButton(card.transform, "EnableButton", AutoReadinessCopy.EnableAnyway,
            font, anchoredX: 170f, EnableAnyway);

        ConfirmCardFitter.Capture(cardRt, titleTmp, bodyTmp, ref baseline);
    }

    // The percent, the bar with its fill, the mark at the unlock threshold and the mark's label — everything between the title
    // and the body. The mark and its label are the card's children (not the bar's): SeatProgress seats the whole stack per Show.
    private static void BuildProgress(Transform card, TMP_FontAsset font)
    {
        percentTmp = BuildTmp(card, "Percent", string.Empty, 72f, FontStyles.Bold, font);   // the text is set per Show
        SetTopStretch((RectTransform)percentTmp.transform, top: PercentTop, height: PercentHeight, sideInset: BarSideInset);

        GameObject bar = NewChild(card, "Bar", typeof(Image));
        SetTopStretch((RectTransform)bar.transform, top: BarTop, height: BarHeight, sideInset: BarSideInset);
        barImage = Decorate(bar, BarHeight / 2f);

        GameObject fill = NewChild(bar.transform, "Fill", typeof(Image));
        fillRt = (RectTransform)fill.transform;
        fillImage = Decorate(fill, BarHeight / 2f);

        GameObject marker = NewChild(card, "Marker", typeof(Image));
        markerRt = (RectTransform)marker.transform;
        markerRt.anchorMin = markerRt.anchorMax = new Vector2(0f, 1f);
        markerRt.pivot = new Vector2(0.5f, 0.5f);
        markerRt.sizeDelta = new Vector2(MarkerWidth, MarkerHeight);
        markerImage = Decorate(marker, 0f);

        markerLabelTmp = BuildTmp(card, "MarkerLabel", AutoReadinessCopy.MarkerLabel, 26f, FontStyles.Normal, font);
        markerLabelRt = (RectTransform)markerLabelTmp.transform;
        markerLabelRt.anchorMin = markerLabelRt.anchorMax = new Vector2(0f, 1f);
        markerLabelRt.pivot = new Vector2(0.5f, 1f);
        markerLabelRt.sizeDelta = new Vector2(MarkerLabelWidth, MarkerLabelHeight);
    }

    // FirstStepsCard.SetProgress's pattern: the fill is the bar's left <percent> share, so it follows the bar's width on its own.
    private static void SetFill(int percent)
    {
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(Mathf.Clamp01(percent / 100f), 1f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
    }

    // Seats everything between the title and the body at its authored distance from the card's top, pushed down by however far
    // the title grew (the fitter has moved the body by the same amount, so the authored gaps survive). The mark sits at the
    // unlock percent's share of the bar's width — the bar is BarSideInset in from both card edges — and its label hangs
    // centred under it.
    private static void SeatProgress(int unlockAt, float titleGrowth)
    {
        SetTopOffset(percentTmp.rectTransform, PercentTop + titleGrowth);
        SetTopOffset(barImage.rectTransform, BarTop + titleGrowth);
        float x = BarSideInset + (CardWidth - 2f * BarSideInset) * Mathf.Clamp01(unlockAt / 100f);
        markerRt.anchoredPosition = new Vector2(x, -(BarTop + BarHeight / 2f + titleGrowth));
        markerLabelRt.anchoredPosition = new Vector2(x, -(MarkerLabelTop + titleGrowth));
    }

    private static void SetTopOffset(RectTransform rt, float top) =>
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -top);

    // A purely visual part of the card: it must not take a tap from the card's own absorber. Radius 0 = square ends.
    private static Image Decorate(GameObject go, float radius)
    {
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        if (radius > 0f) AddRounded(go, radius);
        return image;
    }

    private static (Image, TextMeshProUGUI) BuildButton(Transform card, string name, string label,
        TMP_FontAsset font, float anchoredX, Action action)
    {
        var go = NewChild(card, name, typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(anchoredX, ButtonY);
        rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        AddRounded(go, 48f);

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = go.GetComponent<Image>();
        PopupUI.WireFingerUp(button, action);

        // 30pt, not the confirm's 34: «Включить всё равно» must fit a 300u button on one line.
        var tmp = BuildTmp(go.transform, "Label", label, 30f, FontStyles.Bold, font);
        StretchFull((RectTransform)tmp.transform);
        return (go.GetComponent<Image>(), tmp);
    }

    // Theme snapshot at show time (the sheet is transient). «Понятно» is the primary action — keep learning in «Вместе»;
    // the override is the quiet secondary button.
    private static void Paint()
    {
        if (cardImage != null) cardImage.color = Theme.Color(ThemeRole.Surface);
        if (titleTmp != null) titleTmp.color = Theme.Color(ThemeRole.InkPrimary);
        if (percentTmp != null) percentTmp.color = Theme.Color(ThemeRole.AccentText);
        if (barImage != null) barImage.color = Theme.Color(ThemeRole.Border);
        if (fillImage != null) fillImage.color = Theme.Color(ThemeRole.AccentFill);
        if (markerImage != null) markerImage.color = Theme.Color(ThemeRole.InkPrimary);
        if (markerLabelTmp != null) markerLabelTmp.color = Theme.Color(ThemeRole.InkSecondary);
        if (bodyTmp != null) bodyTmp.color = Theme.Color(ThemeRole.InkSecondary);
        if (dismissImage != null) dismissImage.color = Theme.Color(ThemeRole.AccentFill);
        if (dismissLabel != null) dismissLabel.color = Theme.Color(ThemeRole.AccentOnFill);
        if (enableImage != null) enableImage.color = Theme.Color(ThemeRole.Background);
        if (enableLabel != null) enableLabel.color = Theme.Color(ThemeRole.InkSecondary);
    }

    // ---- build helpers (BotActivationConfirm's, verbatim) ------------------

    private static GameObject NewChild(Transform parent, string name, params Type[] components)
    {
        var go = new GameObject(name, components);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetTopStretch(RectTransform rt, float top, float height, float sideInset)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(-sideInset * 2f, height);
    }

    private static TextMeshProUGUI BuildTmp(Transform parent, string name, string text,
        float fontSize, FontStyles style, TMP_FontAsset font)
    {
        var go = NewChild(parent, name, typeof(TextMeshProUGUI));
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.characterSpacing = -2f;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        return tmp;
    }

    // RoundedCorners lives in its own UPM assembly — scan loaded assemblies
    // (project memory: Type.GetType against Assembly-CSharp silently fails).
    private static Type cachedRoundedType;

    private static void AddRounded(GameObject go, float radius)
    {
        if (cachedRoundedType == null)
        {
            const string fullName = "Nobi.UiRoundedCorners.ImageWithRoundedCorners";
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                cachedRoundedType = asm.GetType(fullName);
                if (cachedRoundedType != null) break;
            }
            if (cachedRoundedType == null) return;
        }

        Component rc = go.GetComponent(cachedRoundedType) ?? go.AddComponent(cachedRoundedType);
        cachedRoundedType.GetField("radius")?.SetValue(rc, radius);
        cachedRoundedType.GetField("image")?.SetValue(rc, go.GetComponent<Image>());
    }
}
