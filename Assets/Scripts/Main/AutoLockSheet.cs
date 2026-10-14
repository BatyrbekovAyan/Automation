using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Learning B: the soft lock's sheet, shown by AutoLockGate when the server says the bot is not ready. Built lazily on
/// its own overlay canvas (sortingOrder 3) exactly like BotActivationConfirm — the gate fires from four screens, and the
/// house rule puts a scene sheet inside the screen that raises it. «Понятно» (and Back) close it; «Включить всё равно»
/// runs the caller's `proceed` (the ordinary enable path: its confirm, then CommitMode) exactly once. Buttons are wired
/// ONCE through PopupUI.WireFingerUp; the pending action is swapped per Show.
/// </summary>
public static class AutoLockSheet
{
    public const float CardWidth = 720f;
    public const float CardHeight = 480f;   // 40u taller than BotActivationConfirm: the body is four lines at 32pt
    public const float TitleTop = 56f, TitleHeight = 60f, TitleSideInset = 48f;
    public const float BodyTop = 136f, BodyHeight = 180f, BodySideInset = 56f;
    public const float ButtonY = 44f, ButtonHeight = 96f, ButtonWidth = 300f;
    private const int OverlaySortingOrder = 3;   // above screens, below SelectionOverlay (4)

    private static GameObject panel;
    private static Image cardImage, dismissImage, enableImage;
    private static TextMeshProUGUI titleTmp, bodyTmp, dismissLabel, enableLabel;
    private static Action pending;
    private static bool hiding;
    private static ConfirmCardFitter.Baseline baseline;

    /// <summary>True while the sheet is up and not already leaving (Back router).</summary>
    public static bool IsShowing => panel != null && panel.activeSelf && !hiding;

    /// <summary>EditMode seam: the built panel (null until the first <see cref="Show"/>).</summary>
    internal static GameObject PanelForTests => panel;

    /// <summary>Raise the sheet for a bot with <paramref name="matches"/> of the window; <paramref name="proceed"/> runs only on «Включить всё равно».</summary>
    public static void Show(TMP_FontAsset font, int matches, bool perChat, Action proceed)
    {
        EnsureBuilt(font);
        pending = proceed;
        hiding = false;
        bodyTmp.text = AutoReadinessCopy.SheetBody(matches, perChat);
        Paint();
        PopupUI.Show(panel);
        // After Show: TMP measures only on an active object (ConfirmCardFitter's rule).
        ConfirmCardFitter.Fit((RectTransform)cardImage.transform, titleTmp, bodyTmp, ref baseline);
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
        cardImage = dismissImage = enableImage = null;
        titleTmp = bodyTmp = dismissLabel = enableLabel = null;
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

        bodyTmp = BuildTmp(card.transform, "Body", AutoReadinessCopy.SheetBody(0, perChat: false), 32f, FontStyles.Normal, font);
        bodyTmp.enableWordWrapping = true;
        SetTopStretch((RectTransform)bodyTmp.transform, top: BodyTop, height: BodyHeight, sideInset: BodySideInset);

        (dismissImage, dismissLabel) = BuildButton(card.transform, "DismissButton", AutoReadinessCopy.Understood,
            font, anchoredX: -170f, Dismiss);
        (enableImage, enableLabel) = BuildButton(card.transform, "EnableButton", AutoReadinessCopy.EnableAnyway,
            font, anchoredX: 170f, EnableAnyway);

        ConfirmCardFitter.Capture(cardRt, titleTmp, bodyTmp, ref baseline);
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
