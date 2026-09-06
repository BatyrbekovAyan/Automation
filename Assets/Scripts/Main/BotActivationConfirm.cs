using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Confirm dialog for ENABLING a bot from the bots-page «Авто» capsule
/// (C2 card, sketch 006). Copy and asymmetry match the chats-header pill:
/// only enabling confirms (the bot starts messaging real clients), disabling
/// never routes here.
///
/// Built LAZILY at runtime on its own overlay canvas instead of living in the
/// scene: the chats popup (ReplyModeConfirmPopup) sits under Screen_Whatsapp
/// and cannot render while the «Боты» tab is up, and a scene-built twin would
/// mean another Main.unity mutation. Buttons are wired ONCE through
/// PopupUI.WireFingerUp (it appends, never replaces — rewiring per show would
/// stack handlers); the pending action is swapped per call instead
/// (ReplyModeToggleBinder's pattern). Colors are stamped from Theme on every
/// Show so the popup follows the active theme without a ThemedColor binding.
/// </summary>
public static class BotActivationConfirm
{
    private const string TitleText = "Включить авто-режим?";
    private const string BodyText =
        "Бот будет отвечать клиентам сам. Выключить можно в любой момент — этой же кнопкой.";

    // Authored card geometry. Public so ConfirmCardLayoutTests computes this twin's
    // clearance above the buttons from the same numbers BuildCard writes, instead of
    // typing them a second time — a box moved here must move the test's arithmetic too.
    public const float CardWidth = 720f;
    public const float CardHeight = 440f;
    public const float TitleTop = 56f, TitleHeight = 60f, TitleSideInset = 48f;
    public const float BodyTop = 136f, BodyHeight = 140f, BodySideInset = 56f;
    public const float ButtonY = 44f, ButtonHeight = 96f, ButtonWidth = 300f;
    private const int OverlaySortingOrder = 3;   // above screens, below SelectionOverlay (4)

    private static GameObject panel;
    private static Image cardImage;
    private static TextMeshProUGUI titleTmp, bodyTmp, cancelLabel, confirmLabel;
    private static Image cancelImage, confirmImage;
    private static Action pending;
    private static ConfirmCardFitter.Baseline baseline;   // authored card geometry, captured once

    /// <summary>Show the confirm; <paramref name="onConfirm"/> runs only on «Включить».</summary>
    public static void Show(TMP_FontAsset font, Action onConfirm)
    {
        EnsureBuilt(font);
        pending = onConfirm;
        Paint();
        PopupUI.Show(panel);
        Fit();
    }

    /// <summary>
    /// Grow the card if the copy no longer fits its authored boxes — the same
    /// treatment the chats popup gets, applied here so the two twins cannot
    /// drift. A no-op for today's copy (the title is one line and the body
    /// three, both inside their boxes); it exists so a longer string can never
    /// reproduce the chats popup's 2026-09-04 overlap on this screen.
    /// Runs after PopupUI.Show, which is what makes the panel active and the
    /// TMP measurement valid — see ConfirmCardFitter.
    /// </summary>
    private static void Fit()
    {
        if (cardImage == null) return;
        ConfirmCardFitter.Fit((RectTransform)cardImage.transform, titleTmp, bodyTmp, ref baseline);
    }

    private static void EnsureBuilt(TMP_FontAsset font)
    {
        if (panel != null) return;

        var canvasGo = new GameObject("BotActivationConfirmCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
        PopupUI.WireFingerUp(panel, OnCancel);   // tap outside the card = cancel

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

        titleTmp = BuildTmp(card.transform, "Title", TitleText, 44f, FontStyles.Bold, font);
        // Wrapping ON so a longer title breaks onto a second line — which Fit()
        // then makes room for — instead of running off the card's edge. Today's
        // title measures ~554u inside a 624u column, so it still renders as the
        // same single line it always did.
        titleTmp.textWrappingMode = TextWrappingModes.Normal;
        SetTopStretch((RectTransform)titleTmp.transform, top: TitleTop, height: TitleHeight, sideInset: TitleSideInset);

        bodyTmp = BuildTmp(card.transform, "Body", BodyText, 32f, FontStyles.Normal, font);
        bodyTmp.enableWordWrapping = true;
        SetTopStretch((RectTransform)bodyTmp.transform, top: BodyTop, height: BodyHeight, sideInset: BodySideInset);

        (cancelImage, cancelLabel) = BuildButton(card.transform, "CancelButton", "Отмена",
            font, anchoredX: -170f, OnCancel);
        (confirmImage, confirmLabel) = BuildButton(card.transform, "ConfirmButton", "Включить",
            font, anchoredX: 170f, OnConfirm);

        // Snapshot the geometry just authored above — Fit() solves from it.
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

        var tmp = BuildTmp(go.transform, "Label", label, 34f, FontStyles.Bold, font);
        StretchFull((RectTransform)tmp.transform);
        return (go.GetComponent<Image>(), tmp);
    }

    // Theme snapshot at show time — the popup is transient, so a live binding
    // would be overkill; re-stamping per Show keeps both themes correct.
    private static void Paint()
    {
        if (cardImage != null) cardImage.color = Theme.Color(ThemeRole.Surface);
        if (titleTmp != null) titleTmp.color = Theme.Color(ThemeRole.InkPrimary);
        if (bodyTmp != null) bodyTmp.color = Theme.Color(ThemeRole.InkSecondary);
        if (cancelImage != null) cancelImage.color = Theme.Color(ThemeRole.Background);
        if (cancelLabel != null) cancelLabel.color = Theme.Color(ThemeRole.InkSecondary);
        if (confirmImage != null) confirmImage.color = Theme.Color(ThemeRole.AccentFill);
        if (confirmLabel != null) confirmLabel.color = Theme.Color(ThemeRole.AccentOnFill);
    }

    private static void OnConfirm()
    {
        PopupUI.Hide(panel);
        Action confirmed = pending;
        pending = null;
        confirmed?.Invoke();
    }

    private static void OnCancel()
    {
        PopupUI.Hide(panel);
        pending = null;
    }

    /// <summary>True while the confirm is on screen (Back router).</summary>
    public static bool IsShowing => panel != null && panel.activeSelf;

    /// <summary>The system Back — identical to «Отмена».</summary>
    public static void Cancel()
    {
        if (IsShowing) OnCancel();
    }

    // ---- build helpers ---------------------------------------------------

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
