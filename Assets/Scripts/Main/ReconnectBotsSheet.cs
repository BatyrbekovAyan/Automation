using System;
using System.Collections.Generic;
using DG.Tweening;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F2 (plan 2026-10-11 §5.1–5.2, task T9): «Подключите ботов снова» — the bottom sheet offered once after a purchase when
/// the server has retired channels of this install's bots (trial over, subscription lapsed). One row per channel,
/// «{бот} · WhatsApp» with its own «Подключить»; «Позже» closes it and the cards keep their «Подключить».
///
/// BUILT LAZILY AT RUNTIME on its own overlay canvas (sortingOrder 3), exactly like <see cref="BillingGateSheet"/>: it is
/// raised by the purchase, wherever the paywall happened to be opened from, so no scene screen owns it — and Main.unity is
/// not touched. Fonts are handed in from the paywall's own labels (there is no runtime path to AssetDatabase). Colours are
/// stamped from <see cref="Theme"/> on every <see cref="Show"/>, so nothing here carries a <see cref="ThemedColor"/>.
/// The Android Back router closes it with <see cref="Dismiss"/> — the action of its own «Позже».
/// </summary>
public static class ReconnectBotsSheet
{
    // ── Metrics (1080×1920 canvas reference units; BillingGateSheet's grid) ──

    private const float TopRadius = 60f;
    private const float SideInset = 72f;
    private const float BottomSafePad = 96f;

    private const float GrabberWidth = 108f;
    private const float GrabberHeight = 12f;
    private const float GrabberTop = 24f;

    private const float TitleTop = 76f;
    private const float TitleHeight = 60f;
    private const float TitleSize = 44f;          // H3

    private const float BodyTop = 156f;
    private const float BodySize = 36f;           // Body2
    /// <summary>Three lines at 36 — used when the body cannot be measured yet (a font-less build); the live copy takes three.</summary>
    private const float BodyFallbackHeight = 150f;
    private const float BodyToRows = 32f;

    /// <summary>One channel per row; a long list scrolls instead of pushing the sheet past the screen.</summary>
    public const float RowHeight = 132f;
    public const int MaxVisibleRows = 4;
    private const float RowLabelSize = 38f;
    private const float RowButtonHeight = 88f;
    private const float RowButtonRadius = 44f;
    private const float RowButtonPadding = 36f;
    private const float RowButtonSize = 32f;
    private const float RowGap = 24f;

    private const float RowsToSecondary = 24f;
    private const float SecondaryHeight = 120f;   // house touch floor
    private const float SecondarySize = 40f;

    private const float DragZoneHeight = 156f;
    private const int OverlaySortingOrder = 3;    // above the screens, below SelectionOverlay (4) — BillingGateSheet's slot

    private const float SlideInSeconds = 0.25f;
    private const float SlideOutSeconds = 0.2f;
    private const float ScrimFadeSeconds = 0.2f;

    // ── Live objects ──

    private static GameObject panel;
    private static RectTransform sheetRect, bodyRect, rowsViewport, rowsContent, secondaryRect;
    private static ScrollRect rowsScroll;
    private static CanvasGroup scrimGroup;
    private static Image scrimImage, sheetImage, grabberImage;
    private static TextMeshProUGUI titleTmp, bodyTmp, secondaryLabel;
    private static readonly List<GameObject> rowObjects = new List<GameObject>();

    private static Tween slide, fade;
    private static bool hiding;
    private static float sheetHeight;
    private static Action<RetiredChannelPolicy.Candidate> onConnect;
    private static TMP_FontAsset titleFontInUse, bodyFontInUse;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        panel = null;
        slide = null;
        fade = null;
        hiding = false;
        onConnect = null;
        rowObjects.Clear();
        titleFontInUse = null;
        bodyFontInUse = null;
    }

    /// <summary>True while the sheet is up and not already leaving (Back router).</summary>
    public static bool IsOpen => panel != null && panel.activeSelf && !hiding;

    /// <summary>EditMode seam: the built panel (null until the first <see cref="Show"/>).</summary>
    internal static GameObject PanelForTests => panel;

    /// <summary>EditMode seam: destroys the overlay and forgets every static, like a fresh play-mode enter.</summary>
    internal static void ResetForTests()
    {
        slide?.Kill();
        fade?.Kill();
        if (panel != null) UnityEngine.Object.DestroyImmediate(panel.transform.root.gameObject);
        Bootstrap();
    }

    /// <summary>
    /// Raise the sheet with one row per candidate. <paramref name="connect"/> runs once a row's «Подключить» is tapped AND
    /// the sheet has left the screen, so the bot's settings slide in over a clear screen. No rows → nothing is shown.
    /// </summary>
    public static void Show(IReadOnlyList<RetiredChannelPolicy.Candidate> rows, TMP_FontAsset titleFont, TMP_FontAsset bodyFont,
                            Action<RetiredChannelPolicy.Candidate> connect)
    {
        if (rows == null || rows.Count == 0) return;
        EnsureBuilt();
        if (panel == null) return;
        titleFontInUse = titleFont != null ? titleFont : titleFontInUse;
        bodyFontInUse = bodyFont != null ? bodyFont : bodyFontInUse;

        onConnect = connect;
        slide?.Kill();
        fade?.Kill();
        hiding = false;

        bool wasOpen = panel.activeSelf;
        panel.SetActive(true);              // active BEFORE any measurement — TMP cannot measure a never-active label
        Paint(rows);
        if (wasOpen) { sheetRect.anchoredPosition = Vector2.zero; scrimGroup.alpha = 1f; return; }

        sheetRect.anchoredPosition = new Vector2(0f, -sheetHeight);
        scrimGroup.alpha = 0f;
        slide = sheetRect.DOAnchorPosY(0f, SlideInSeconds).SetEase(Ease.OutCubic).SetLink(panel);
        fade = scrimGroup.DOFade(1f, ScrimFadeSeconds).SetLink(panel);
    }

    /// <summary>«Позже», a scrim tap, a drag-dismiss or the system Back: close, connect nothing.</summary>
    public static void Dismiss()
    {
        onConnect = null;
        Hide(null);
    }

    private static void OnRowConnect(RetiredChannelPolicy.Candidate row)
    {
        Action<RetiredChannelPolicy.Candidate> next = onConnect;   // taken BEFORE the tween: a re-entrant Show cannot swap it
        onConnect = null;
        Hide(next == null ? (Action)null : () => next(row));
    }

    private static void Hide(Action after)
    {
        if (panel == null || !panel.activeSelf)
        {
            after?.Invoke();
            return;
        }
        if (hiding) return;                 // already leaving: let that exit fire what it promised (BillingGateSheet's rule)

        hiding = true;
        slide?.Kill();
        fade?.Kill();
        slide = sheetRect.DOAnchorPosY(-sheetHeight, SlideOutSeconds)
            .SetEase(Ease.InCubic)
            .SetLink(panel)
            .OnComplete(() =>
            {
                hiding = false;
                if (panel != null) panel.SetActive(false);
                after?.Invoke();
            });
        fade = scrimGroup.DOFade(0f, ScrimFadeSeconds).SetLink(panel);
    }

    // ── Paint: copy, rows, measured geometry, theme colours ──

    private static void Paint(IReadOnlyList<RetiredChannelPolicy.Candidate> rows)
    {
        ApplyFont(titleTmp, titleFontInUse);
        ApplyFont(bodyTmp, bodyFontInUse);
        ApplyFont(secondaryLabel, bodyFontInUse);
        titleTmp.text = RetiredChannelCopy.SheetTitle;
        bodyTmp.text = RetiredChannelCopy.SheetBody;
        secondaryLabel.text = RetiredChannelCopy.SheetLater;

        float width = 1080f - 2f * SideInset;
        float measured = bodyTmp.GetPreferredValues(RetiredChannelCopy.SheetBody, width, 0f).y;
        float bodyHeight = measured > 0f ? Mathf.Ceil(measured) : BodyFallbackHeight;
        bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, bodyHeight);

        RebuildRows(rows);
        int visible = Mathf.Min(rows.Count, MaxVisibleRows);
        float rowsTop = BodyTop + bodyHeight + BodyToRows;
        float rowsHeight = visible * RowHeight;
        PlaceTop(rowsViewport, rowsTop, rowsHeight, SideInset);
        rowsContent.sizeDelta = new Vector2(0f, rows.Count * RowHeight);
        rowsContent.anchoredPosition = Vector2.zero;
        rowsScroll.vertical = rows.Count > MaxVisibleRows;

        float secondaryTop = rowsTop + rowsHeight + RowsToSecondary;
        PlaceTop(secondaryRect, secondaryTop, SecondaryHeight, SideInset);
        sheetHeight = secondaryTop + SecondaryHeight + BottomSafePad;
        sheetRect.sizeDelta = new Vector2(0f, sheetHeight);

        PaintColours();
    }

    private static void PaintColours()
    {
        Color veil = Theme.Color(ThemeRole.Scrim);
        scrimImage.color = new Color(veil.r, veil.g, veil.b, PopupUI.DefaultBackdropAlpha);
        sheetImage.color = Theme.Color(ThemeRole.Surface);
        grabberImage.color = Theme.Color(ThemeRole.Border);
        titleTmp.color = Theme.Color(ThemeRole.InkPrimary);
        bodyTmp.color = Theme.Color(ThemeRole.InkSecondary);
        secondaryLabel.color = Theme.Color(ThemeRole.InkSecondary);
    }

    private static void RebuildRows(IReadOnlyList<RetiredChannelPolicy.Candidate> rows)
    {
        foreach (GameObject old in rowObjects)
        {
            if (old == null) continue;
            old.transform.SetParent(null, false);             // out of the list now; Destroy lands at the end of the frame
            if (Application.isPlaying) UnityEngine.Object.Destroy(old);
            else UnityEngine.Object.DestroyImmediate(old);
        }
        rowObjects.Clear();
        for (int i = 0; i < rows.Count; i++) rowObjects.Add(BuildRow(rows[i], i));
    }

    private static GameObject BuildRow(RetiredChannelPolicy.Candidate candidate, int index)
    {
        var row = NewChild(rowsContent, $"Row{index}", typeof(RectTransform));
        PlaceTop((RectTransform)row.transform, index * RowHeight, RowHeight, 0f);

        var button = NewChild(row.transform, "Connect", typeof(RectTransform), typeof(Image), typeof(Button));
        var buttonImage = button.GetComponent<Image>();
        buttonImage.color = Theme.Color(ThemeRole.AccentFill);
        buttonImage.raycastTarget = true;
        var rounded = button.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = RowButtonRadius;
        rounded.Validate();
        rounded.Refresh();
        var buttonLabel = BuildTmp(button.transform, "Label", RowButtonSize, titleFontInUse);
        buttonLabel.text = RetiredChannelCopy.SheetRowCta;
        buttonLabel.color = Theme.Color(ThemeRole.AccentOnFill);
        StretchFull((RectTransform)buttonLabel.transform);
        float labelWidth = buttonLabel.GetPreferredValues(RetiredChannelCopy.SheetRowCta).x;
        float buttonWidth = Mathf.Ceil(Mathf.Max(labelWidth, 120f)) + 2f * RowButtonPadding;
        var buttonRect = (RectTransform)button.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0.5f);
        buttonRect.pivot = new Vector2(1f, 0.5f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = new Vector2(buttonWidth, RowButtonHeight);
        var uiButton = button.GetComponent<Button>();
        uiButton.transition = Selectable.Transition.None;
        uiButton.targetGraphic = buttonImage;
        PopupUI.WireFingerUp(uiButton, () => OnRowConnect(candidate));

        var label = BuildTmp(row.transform, "Label", RowLabelSize, bodyFontInUse);
        label.text = RetiredChannelCopy.SheetRow(candidate.BotName, candidate.Channel);
        label.color = Theme.Color(ThemeRole.InkPrimary);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.overflowMode = TextOverflowModes.Ellipsis;    // a long bot name truncates, never runs under the button
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = new Vector2(-(buttonWidth + RowGap), 0f);
        return row;
    }

    // ── Build (once per session; a destroyed panel reads as null and rebuilds) ──

    private static void EnsureBuilt()
    {
        if (panel != null) return;

        var canvasGo = new GameObject("ReconnectBotsSheetCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0f;

        panel = NewChild(canvasGo.transform, "Panel", typeof(RectTransform));
        StretchFull((RectTransform)panel.transform);

        var scrim = NewChild(panel.transform, "Scrim", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        StretchFull((RectTransform)scrim.transform);
        scrimImage = scrim.GetComponent<Image>();
        scrimImage.raycastTarget = true;
        scrimGroup = scrim.GetComponent<CanvasGroup>();
        PopupUI.WireFingerUp(scrim, Dismiss);

        BuildSheet();
        panel.SetActive(false);
    }

    private static void BuildSheet()
    {
        var go = NewChild(panel.transform, "SheetRoot", typeof(RectTransform), typeof(Image));
        sheetRect = (RectTransform)go.transform;
        sheetRect.anchorMin = new Vector2(0f, 0f);
        sheetRect.anchorMax = new Vector2(1f, 0f);
        sheetRect.pivot = new Vector2(0.5f, 0f);
        sheetImage = go.GetComponent<Image>();
        sheetImage.raycastTarget = true;
        var rounded = go.AddComponent<ImageWithIndependentRoundedCorners>();
        rounded.r = new Vector4(TopRadius, TopRadius, 0f, 0f);   // Nobi: x=TL, y=TR — 1:1 with the visual radius
        rounded.Validate();
        rounded.Refresh();
        PopupUI.AbsorbEvents(sheetImage);

        var grabber = NewChild(go.transform, "Grabber", typeof(RectTransform), typeof(Image));
        var grabRt = (RectTransform)grabber.transform;
        grabRt.anchorMin = grabRt.anchorMax = new Vector2(0.5f, 1f);
        grabRt.pivot = new Vector2(0.5f, 1f);
        grabRt.anchoredPosition = new Vector2(0f, -GrabberTop);
        grabRt.sizeDelta = new Vector2(GrabberWidth, GrabberHeight);
        grabberImage = grabber.GetComponent<Image>();
        grabberImage.raycastTarget = false;
        var grabRounded = grabber.AddComponent<ImageWithRoundedCorners>();
        grabRounded.radius = GrabberHeight * 0.5f;
        grabRounded.Validate();
        grabRounded.Refresh();

        titleTmp = BuildTmp(go.transform, "Title", TitleSize, null);
        titleTmp.overflowMode = TextOverflowModes.Ellipsis;
        PlaceTop((RectTransform)titleTmp.transform, TitleTop, TitleHeight, SideInset);

        bodyTmp = BuildTmp(go.transform, "Body", BodySize, null);
        bodyTmp.textWrappingMode = TextWrappingModes.Normal;
        bodyRect = (RectTransform)bodyTmp.transform;
        PlaceTop(bodyRect, BodyTop, BodyFallbackHeight, SideInset);

        BuildRowsList(go.transform);
        BuildSecondary(go.transform);
        BuildDragZone(go.transform);
    }

    private static void BuildRowsList(Transform sheet)
    {
        var viewport = NewChild(sheet, "Rows", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        rowsViewport = (RectTransform)viewport.transform;
        var hit = viewport.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);       // a transparent hit area so a drag between rows still scrolls
        hit.raycastTarget = true;

        var content = NewChild(viewport.transform, "Content", typeof(RectTransform));
        rowsContent = (RectTransform)content.transform;
        rowsContent.anchorMin = new Vector2(0f, 1f);
        rowsContent.anchorMax = new Vector2(1f, 1f);
        rowsContent.pivot = new Vector2(0.5f, 1f);

        rowsScroll = viewport.GetComponent<ScrollRect>();
        rowsScroll.viewport = rowsViewport;
        rowsScroll.content = rowsContent;
        rowsScroll.horizontal = false;
        rowsScroll.movementType = ScrollRect.MovementType.Clamped;
    }

    private static void BuildSecondary(Transform sheet)
    {
        var go = NewChild(sheet, "Later", typeof(RectTransform), typeof(Button));
        secondaryRect = (RectTransform)go.transform;
        // No Image: the label IS the Button's targetGraphic and spans the whole 120-unit row (BillingGateSheet's «Позже»).
        secondaryLabel = BuildTmp(go.transform, "Label", SecondarySize, null);
        StretchFull((RectTransform)secondaryLabel.transform);
        secondaryLabel.raycastTarget = true;
        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = secondaryLabel;
        PopupUI.WireFingerUp(button, Dismiss);
    }

    private static void BuildDragZone(Transform sheet)
    {
        var go = NewChild(sheet, "DragZone", typeof(RectTransform), typeof(Image));
        go.transform.SetAsLastSibling();          // wins the raycast over the title it covers; stops above the body
        PlaceTop((RectTransform)go.transform, 0f, DragZoneHeight, 0f);
        var image = go.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;
        var drag = go.AddComponent<SheetDragDismiss>();
        drag.Bind(sheetRect, scrimGroup);
        drag.onDismiss.AddListener(Dismiss);
    }

    // ── Helpers ──

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

    private static void PlaceTop(RectTransform rt, float top, float height, float sideInset)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(-sideInset * 2f, height);
    }

    private static void ApplyFont(TextMeshProUGUI tmp, TMP_FontAsset font)
    {
        if (tmp != null && font != null && tmp.font != font) tmp.font = font;
    }

    /// <summary>Weight comes from the font asset, never from FontStyles (PaywallBuilder's rule).</summary>
    private static TextMeshProUGUI BuildTmp(Transform parent, string name, float size, TMP_FontAsset font)
    {
        var go = NewChild(parent, name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }
}
