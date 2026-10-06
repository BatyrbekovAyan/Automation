using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-built centered card for the legacy-profile claim (A1), on its own overlay canvas —
/// BotActivationConfirm's pattern (not a scene popup: the claim can be raised from boot, before
/// any screen owns the modal). Two shapes: CODE (body + 6-digit input + «Подтвердить»/«Позже») and
/// REAUTH/OWNED (body + «Понятно»). The card is top-anchored so the keyboard raised by the input
/// never covers it on a 1080×1920 canvas (card bottom at ~820u, keyboard top ≈ 920u).
/// </summary>
public static class ProfileClaimSheet
{
    public const float CardWidth = 840f;
    public const float CardHeight = 640f;
    public const float CardTop = 200f;
    public const float TitleTop = 56f, TitleHeight = 64f, TitleSideInset = 48f;
    public const float BodyTop = 136f, BodyHeight = 200f, BodySideInset = 56f;
    public const float FieldTop = 356f, FieldHeight = 108f, FieldSideInset = 96f;
    public const float ButtonY = 44f, ButtonHeight = 96f, ButtonWidth = 340f;
    private const int OverlaySortingOrder = 3;

    private static GameObject panel;
    private static Image cardImage, fieldImage, primaryImage, secondaryImage;
    private static TextMeshProUGUI titleTmp, bodyTmp, errorTmp, primaryLabel, secondaryLabel, placeholderTmp, fieldTextTmp;
    private static TMP_InputField field;
    private static Action<string> pendingSubmit;
    private static Action pendingDismiss;
    private static Action pendingResend;      // V2: set while the sheet offers «Отправить код ещё раз» instead of the field
    private static Type cachedRoundedType;

    public static bool IsShowing => panel != null && panel.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        panel = null;
        pendingSubmit = null;
        pendingDismiss = null;
        pendingResend = null;
    }

    /// <param name="dismissLabel">The secondary button of the CODE shape: «Позже» at boot (default), «Отменить» right
    /// after a pairing (V2 — leaving that sheet cancels the connection instead of snoozing it).</param>
    public static void Show(TMP_FontAsset font, ProfileClaimPolicy.SheetMode mode, string botName, ChatChannel channel,
                            Action<string> onSubmitCode, Action onDismiss, string dismissLabel = null)
    {
        EnsureBuilt(font);
        pendingSubmit = onSubmitCode;
        pendingDismiss = onDismiss;
        pendingResend = null;
        secondaryLabel.text = string.IsNullOrEmpty(dismissLabel) ? ProfileClaimCopy.Later : dismissLabel;
        bool code = mode == ProfileClaimPolicy.SheetMode.Code;
        titleTmp.text = mode switch
        {
            ProfileClaimPolicy.SheetMode.Code => ProfileClaimCopy.TitleCode,
            ProfileClaimPolicy.SheetMode.Owned => ProfileClaimCopy.TitleOwned,
            _ => ProfileClaimCopy.TitleReauth,
        };
        bodyTmp.text = code ? ProfileClaimCopy.BodyCode(botName, channel) : ProfileClaimCopy.BodyReauth(botName, channel);
        errorTmp.text = "";
        field.gameObject.SetActive(code);
        field.text = "";
        primaryLabel.text = code ? ProfileClaimCopy.Submit : ProfileClaimCopy.Ok;
        secondaryImage.gameObject.SetActive(code);
        ((RectTransform)primaryImage.transform).anchoredPosition = new Vector2(code ? 180f : 0f, ButtonY);
        Paint();
        PopupUI.Show(panel);
    }

    public static void ShowError(string text)
    {
        if (errorTmp != null) errorTmp.text = text ?? "";
    }

    /// <summary>
    /// V2: no usable code (the transport refused it, five wrong answers killed it, the pairing is not visible yet) —
    /// hide the field and turn the primary button into «Отправить код ещё раз».
    /// </summary>
    public static void ShowResend(string error, Action onResend)
    {
        if (panel == null) return;
        pendingResend = onResend;
        field.gameObject.SetActive(false);
        primaryLabel.text = ProfileClaimCopy.Resend;
        errorTmp.text = error ?? "";
    }

    /// <summary>V2: a code is in the chat again — back to the field and «Подтвердить».</summary>
    public static void ShowCodeEntry()
    {
        if (panel == null) return;
        pendingResend = null;
        field.gameObject.SetActive(true);
        field.text = "";
        primaryLabel.text = ProfileClaimCopy.Submit;
        errorTmp.text = "";
    }

    public static void Hide()
    {
        if (panel != null && panel.activeSelf) PopupUI.Hide(panel);
    }

    private static void EnsureBuilt(TMP_FontAsset font)
    {
        if (panel != null) return;

        var canvasGo = new GameObject("ProfileClaimSheetCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
        backdrop.raycastTarget = true;   // the backdrop deliberately does NOT dismiss: «Позже» is explicit

        BuildCard(font);
        panel.SetActive(false);
    }

    private static void BuildCard(TMP_FontAsset font)
    {
        var card = NewChild(panel.transform, "Content", typeof(Image));
        var rt = (RectTransform)card.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -CardTop);
        rt.sizeDelta = new Vector2(CardWidth, CardHeight);
        cardImage = card.GetComponent<Image>();
        cardImage.raycastTarget = true;
        AddRounded(card, 40f);
        PopupUI.AbsorbEvents(cardImage);

        titleTmp = BuildTmp(card.transform, "Title", "", 44f, FontStyles.Bold, font);
        titleTmp.textWrappingMode = TextWrappingModes.Normal;
        SetTopStretch((RectTransform)titleTmp.transform, TitleTop, TitleHeight, TitleSideInset);

        bodyTmp = BuildTmp(card.transform, "Body", "", 32f, FontStyles.Normal, font);
        bodyTmp.enableWordWrapping = true;
        SetTopStretch((RectTransform)bodyTmp.transform, BodyTop, BodyHeight, BodySideInset);

        BuildField(card.transform, font);

        errorTmp = BuildTmp(card.transform, "Error", "", 28f, FontStyles.Normal, font);
        errorTmp.enableWordWrapping = true;
        SetTopStretch((RectTransform)errorTmp.transform, FieldTop + FieldHeight + 12f, 72f, BodySideInset);

        (secondaryImage, secondaryLabel) = BuildButton(card.transform, "SecondaryButton", ProfileClaimCopy.Later, font, -180f, OnDismiss);
        (primaryImage, primaryLabel) = BuildButton(card.transform, "PrimaryButton", ProfileClaimCopy.Submit, font, 180f, OnPrimary);
    }

    private static void BuildField(Transform card, TMP_FontAsset font)
    {
        var go = NewChild(card, "CodeField", typeof(Image), typeof(TMP_InputField));
        SetTopStretch((RectTransform)go.transform, FieldTop, FieldHeight, FieldSideInset);
        fieldImage = go.GetComponent<Image>();
        fieldImage.raycastTarget = true;
        AddRounded(go, 24f);

        var area = NewChild(go.transform, "Text Area", typeof(RectMask2D));
        var areaRt = (RectTransform)area.transform;
        StretchFull(areaRt);
        areaRt.offsetMin = new Vector2(32f, 12f);
        areaRt.offsetMax = new Vector2(-32f, -12f);

        placeholderTmp = BuildTmp(area.transform, "Placeholder", ProfileClaimCopy.Placeholder, 40f, FontStyles.Normal, font);
        StretchFull((RectTransform)placeholderTmp.transform);
        fieldTextTmp = BuildTmp(area.transform, "Text", "", 48f, FontStyles.Bold, font);
        StretchFull((RectTransform)fieldTextTmp.transform);
        fieldTextTmp.characterSpacing = 6f;

        field = go.GetComponent<TMP_InputField>();
        field.textViewport = areaRt;
        field.textComponent = fieldTextTmp;
        field.placeholder = placeholderTmp;
        field.characterLimit = 6;
        field.contentType = TMP_InputField.ContentType.Standard;
        field.inputType = TMP_InputField.InputType.Standard;
        field.keyboardType = TouchScreenKeyboardType.NumberPad;
        field.lineType = TMP_InputField.LineType.MultiLineSubmit;   // house rule: every field MultiLineSubmit (keyboard-dip fix)
        field.targetGraphic = fieldImage;
        field.transition = Selectable.Transition.None;
    }

    private static (Image, TextMeshProUGUI) BuildButton(Transform card, string name, string label, TMP_FontAsset font, float anchoredX, Action action)
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
        var tmp = BuildTmp(go.transform, "Label", label, 34f, FontStyles.Bold, font);
        StretchFull((RectTransform)tmp.transform);
        return (go.GetComponent<Image>(), tmp);
    }

    private static void Paint()
    {
        cardImage.color = Theme.Color(ThemeRole.Surface);
        titleTmp.color = Theme.Color(ThemeRole.InkPrimary);
        bodyTmp.color = Theme.Color(ThemeRole.InkSecondary);
        errorTmp.color = Theme.Color(ThemeRole.Destructive);
        fieldImage.color = Theme.Color(ThemeRole.Background);
        fieldTextTmp.color = Theme.Color(ThemeRole.InkPrimary);
        placeholderTmp.color = Theme.Color(ThemeRole.InkTertiary);
        secondaryImage.color = Theme.Color(ThemeRole.Background);
        secondaryLabel.color = Theme.Color(ThemeRole.InkSecondary);
        primaryImage.color = Theme.Color(ThemeRole.AccentFill);
        primaryLabel.color = Theme.Color(ThemeRole.AccentOnFill);
    }

    private static void OnPrimary()
    {
        if (pendingResend != null)
        {
            errorTmp.text = "";
            pendingResend.Invoke();             // the flow answers with ShowCodeEntry or ShowResend again
            return;
        }
        if (field != null && field.gameObject.activeSelf)
        {
            string code = WappiProxyRequest.DigitsOnly(field.text);
            if (code.Length != 6) { ShowError(ProfileClaimCopy.ErrorWrongCode); return; }
            pendingSubmit?.Invoke(code);      // the flow hides the sheet on success, shows an error otherwise
            return;
        }
        OnDismiss();
    }

    private static void OnDismiss()
    {
        Hide();
        Action d = pendingDismiss;
        pendingDismiss = null;
        pendingSubmit = null;
        pendingResend = null;
        d?.Invoke();
    }

    // ---- build helpers (BotActivationConfirm's set) ----

    private static GameObject NewChild(Transform parent, string name, params Type[] components)
    {
        var go = new GameObject(name, components);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void SetTopStretch(RectTransform rt, float top, float height, float sideInset)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(-sideInset * 2f, height);
    }

    private static TextMeshProUGUI BuildTmp(Transform parent, string name, string text, float fontSize, FontStyles style, TMP_FontAsset font)
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

    /// <summary>Nobi lives in its own UPM assembly — resolve by scanning, never Type.GetType (memory: roundedcorners assembly).</summary>
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
        }
        if (cachedRoundedType == null) return;
        var comp = go.AddComponent(cachedRoundedType);
        cachedRoundedType.GetField("radius")?.SetValue(comp, radius);
        cachedRoundedType.GetMethod("Validate")?.Invoke(comp, null);
        cachedRoundedType.GetMethod("Refresh")?.Invoke(comp, null);
    }
}
