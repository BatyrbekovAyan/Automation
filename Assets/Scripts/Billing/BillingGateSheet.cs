using System;
using DG.Tweening;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The plan-limit gate sheet (Task 14d, spec §6 «Гейты в местах действия»): the bottom
/// sheet shown when creating a bot or connecting a channel is refused by the plan, BEFORE
/// the full paywall. One reusable sheet, two copy variants, both from the pure
/// <see cref="BillingGateRows"/>.
///
/// BUILT LAZILY AT RUNTIME on its own overlay canvas rather than living in the scene, for
/// the same reason <see cref="BotActivationConfirm"/> is: the gates fire from THREE
/// different screens — Screen_Bots (<c>BotsPage.StartNewBot</c>), the add-bot wizard
/// (<c>Manager.CreateBotFromForm</c>'s pre-flight, which runs over Screen_New) and Bot
/// Settings' auth tab — and the house rule is that a scene sheet lives inside the screen
/// panel that raises it. Three scene copies would mean three wirings, three builders and a
/// «which screen is active» routing decision at every call site; a runtime overlay has one
/// of each and cannot be unreachable. It also means this task mutates Main.unity ZERO
/// times, which matters while parallel sessions share the one worktree.
///
/// Idempotency is therefore trivial: <see cref="EnsureBuilt"/> returns early once the panel
/// exists, and a destroyed panel (domain-reload-free play mode) reads as null and rebuilds.
///
/// Colours are stamped from <see cref="Theme"/> on every <see cref="Show"/> — the same
/// choice BotActivationConfirm makes, and the reason nothing here carries a
/// <see cref="ThemedColor"/> binding (a transient overlay outlives no theme switch, and two
/// owners for one colour is the documented repaint trap).
/// </summary>
public static class BillingGateSheet
{
    // ── Metrics (1080×1920 canvas reference units) ───────────────────────────

    /// <summary>
    /// Grabber 24+12, title block to 136, body block to 316, then CTA 400..532 and
    /// «Позже» 564..684 measured from the top. Re-derive this if any block moves.
    /// </summary>
    private const float SheetHeight = 780f;

    private const float TopRadius = 60f;          // ProfileSubPages' bottom sheet
    private const float SideInset = 72f;
    /// <summary>Home-bar safe area, same reservation the paywall's bottom bar makes.</summary>
    private const float BottomSafePad = 96f;

    private const float GrabberWidth = 108f;      // SheetDragDismissWirer's pill metrics
    private const float GrabberHeight = 12f;
    private const float GrabberTop = 24f;

    private const float TitleTop = 76f;
    private const float TitleHeight = 60f;
    private const float TitleSize = 44f;          // H3

    private const float BodyTop = 156f;
    /// <summary>Three lines at 38 (~144) — the longest live body is two, so a reword has room.</summary>
    private const float BodyHeight = 160f;
    private const float BodySize = 38f;           // Body2

    private const float PrimaryBottom = 248f;
    private const float PrimaryHeight = 132f;     // house touch floor for a primary CTA
    private const float PrimaryRadius = 66f;      // == half the height: a full pill
    private const float PrimarySize = 44f;

    private const float SecondaryBottom = BottomSafePad;
    private const float SecondaryHeight = 120f;   // house touch floor
    private const float SecondarySize = 40f;

    /// <summary>Grabber strip + title block, with clearance above the body — ItemEditSheet's rule.</summary>
    private const float DragZoneHeight = 156f;

    private const int OverlaySortingOrder = 3;    // above the screens, below SelectionOverlay (4)

    private const float SlideInSeconds = 0.25f;
    private const float SlideOutSeconds = 0.2f;
    private const float ScrimFadeSeconds = 0.2f;

    // ── Live objects (rebuilt on demand; see the class doc on play-mode reloads) ──

    private static GameObject panel;
    private static RectTransform sheetRect;
    private static CanvasGroup scrimGroup;
    private static Image scrimImage, sheetImage, grabberImage, primaryImage;
    private static TextMeshProUGUI titleTmp, bodyTmp, primaryLabel, secondaryLabel;

    private static Tween slide, fade;
    private static bool hiding;
    private static Action pending;

    /// <summary>
    /// Statics survive a domain-reload-free play-mode enter, so clear the ones that would
    /// otherwise point at last session's objects/tweens. The GameObject reference is
    /// Unity-fake-null after a destroy and would rebuild on its own; the Tween and the
    /// pending action would not.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        panel = null;
        slide = null;
        fade = null;
        hiding = false;
        pending = null;
    }

    // ── Show / hide ──────────────────────────────────────────────────────────

    /// <summary>
    /// Raise the sheet for <paramref name="trigger"/>. <paramref name="onSeePlans"/> runs
    /// once «Посмотреть тарифы» is tapped AND the sheet has finished leaving, so the paywall
    /// slides in over a clear screen rather than through a dissolving scrim.
    ///
    /// The fonts come from the caller because there is no runtime path to AssetDatabase and
    /// TMP's default asset ships an empty weight table — <see cref="PaywallController"/>
    /// hands over the very font assets PaywallBuilder stamped onto the paywall's own labels.
    /// </summary>
    public static void Show(PaywallTrigger trigger, TMP_FontAsset titleFont, TMP_FontAsset bodyFont,
                            Action onSeePlans)
    {
        EnsureBuilt(titleFont, bodyFont);
        if (panel == null) return;

        pending = onSeePlans;

        // A sheet that is genuinely parked on screen only needs a repaint. Mid-slide (either
        // direction) it is NOT where it looks like it is, so it must re-travel — the same
        // reasoning as PaywallController.Open, and the reason IsActive() is the test rather
        // than IsPlaying() (a paused/queued tween still means «parked mid-travel»).
        bool wasActive = panel.activeSelf;
        bool slideInFlight = slide != null && slide.IsActive();
        bool settledOpen = wasActive && !hiding && !slideInFlight;

        // Kill() defaults to complete:false, so a mid-flight exit tween's OnComplete — which
        // would deactivate the panel and fire the previous pending action — can never land on
        // the sheet we are re-showing.
        slide?.Kill();
        fade?.Kill();
        hiding = false;

        Paint(trigger);
        panel.SetActive(true);
        if (settledOpen) return;

        // Always travel from wherever we are: hidden is exactly -SheetHeight (set at build and
        // at the end of every Hide), and a killed exit tween leaves us part-way down.
        slide = sheetRect.DOAnchorPosY(0f, SlideInSeconds).SetEase(Ease.OutCubic).SetLink(panel);
        fade = scrimGroup.DOFade(1f, ScrimFadeSeconds).SetLink(panel);
    }

    /// <summary>Dismiss without acting — scrim tap, «Позже», or a completed drag-dismiss.</summary>
    public static void Dismiss()
    {
        pending = null;
        Hide(null);
    }

    private static void OnPrimary()
    {
        // Take the action BEFORE the tween so a re-entrant Show cannot swap it mid-flight.
        Action next = pending;
        pending = null;
        Hide(next);
    }

    private static void Hide(Action after)
    {
        if (panel == null || !panel.activeSelf)
        {
            after?.Invoke();
            return;
        }

        hiding = true;
        slide?.Kill();
        fade?.Kill();

        slide = sheetRect.DOAnchorPosY(-SheetHeight, SlideOutSeconds)
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

    // ── Paint ────────────────────────────────────────────────────────────────

    private static void Paint(PaywallTrigger trigger)
    {
        PlanTier tier = EntitlementGate.CurrentTier;

        if (titleTmp != null) titleTmp.text = BillingGateRows.Title(trigger);
        if (bodyTmp != null) bodyTmp.text = BillingGateRows.Body(trigger, tier);
        if (primaryLabel != null) primaryLabel.text = BillingGateRows.PrimaryCta();
        if (secondaryLabel != null) secondaryLabel.text = BillingGateRows.SecondaryCta();

        // ThemeRole.Scrim is opaque black in both themes — the veil's alpha is authored per
        // scrim, and this one matches every other modal in the app.
        if (scrimImage != null)
        {
            Color veil = Theme.Color(ThemeRole.Scrim);
            scrimImage.color = new Color(veil.r, veil.g, veil.b, PopupUI.DefaultBackdropAlpha);
        }
        if (sheetImage != null) sheetImage.color = Theme.Color(ThemeRole.Surface);
        if (grabberImage != null) grabberImage.color = Theme.Color(ThemeRole.Border);
        if (titleTmp != null) titleTmp.color = Theme.Color(ThemeRole.InkPrimary);
        if (bodyTmp != null) bodyTmp.color = Theme.Color(ThemeRole.InkSecondary);
        if (primaryImage != null) primaryImage.color = Theme.Color(ThemeRole.AccentFill);
        if (primaryLabel != null) primaryLabel.color = Theme.Color(ThemeRole.AccentOnFill);
        if (secondaryLabel != null) secondaryLabel.color = Theme.Color(ThemeRole.InkSecondary);
    }

    // ── Build ────────────────────────────────────────────────────────────────

    private static void EnsureBuilt(TMP_FontAsset titleFont, TMP_FontAsset bodyFont)
    {
        if (panel != null) return;

        var canvasGo = new GameObject("BillingGateSheetCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0f;   // match WIDTH, like the main canvas

        panel = NewChild(canvasGo.transform, "Panel", typeof(RectTransform));
        StretchFull((RectTransform)panel.transform);

        BuildScrim();
        BuildSheet(titleFont, bodyFont);

        // Hidden state is authored, not tweened into: the very first Show travels from here.
        sheetRect.anchoredPosition = new Vector2(0f, -SheetHeight);
        scrimGroup.alpha = 0f;
        panel.SetActive(false);
    }

    private static void BuildScrim()
    {
        var go = NewChild(panel.transform, "Scrim", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        StretchFull((RectTransform)go.transform);

        scrimImage = go.GetComponent<Image>();
        scrimImage.raycastTarget = true;
        scrimGroup = go.GetComponent<CanvasGroup>();

        // Wired ONCE: WireFingerUp APPENDS to OnRealRelease, so re-wiring per Show would
        // stack handlers (BotActivationConfirm's rule).
        PopupUI.WireFingerUp(go, Dismiss);
    }

    private static void BuildSheet(TMP_FontAsset titleFont, TMP_FontAsset bodyFont)
    {
        var go = NewChild(panel.transform, "SheetRoot", typeof(RectTransform), typeof(Image));
        sheetRect = (RectTransform)go.transform;
        sheetRect.anchorMin = new Vector2(0f, 0f);
        sheetRect.anchorMax = new Vector2(1f, 0f);
        sheetRect.pivot = new Vector2(0.5f, 0f);
        sheetRect.sizeDelta = new Vector2(0f, SheetHeight);

        sheetImage = go.GetComponent<Image>();
        sheetImage.raycastTarget = true;   // the sheet occludes the scrim's dismiss target
        // Top-only corners. Nobi's Vector4 maps x=TL, y=TR, z=BR, w=BL, and the radius is 1:1
        // with the visual radius — do NOT halve or double it.
        var rounded = go.AddComponent<ImageWithIndependentRoundedCorners>();
        rounded.r = new Vector4(TopRadius, TopRadius, 0f, 0f);
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

        titleTmp = BuildTmp(go.transform, "Title", TitleSize, titleFont);
        SetTopStretch((RectTransform)titleTmp.transform, TitleTop, TitleHeight, SideInset);

        bodyTmp = BuildTmp(go.transform, "Body", BodySize, bodyFont);
        bodyTmp.enableWordWrapping = true;
        SetTopStretch((RectTransform)bodyTmp.transform, BodyTop, BodyHeight, SideInset);

        BuildPrimary(go.transform, titleFont);
        BuildSecondary(go.transform, bodyFont);
        BuildDragZone(go.transform);
    }

    private static void BuildPrimary(Transform sheet, TMP_FontAsset font)
    {
        var go = NewChild(sheet, "PrimaryCta", typeof(RectTransform), typeof(Image), typeof(Button));
        SetBottomStretch((RectTransform)go.transform, PrimaryBottom, PrimaryHeight, SideInset);

        primaryImage = go.GetComponent<Image>();
        primaryImage.raycastTarget = true;
        var rounded = go.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = PrimaryRadius;
        rounded.Validate();
        rounded.Refresh();

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = primaryImage;
        PopupUI.WireFingerUp(button, OnPrimary);

        primaryLabel = BuildTmp(go.transform, "Label", PrimarySize, font);
        StretchFull((RectTransform)primaryLabel.transform);
    }

    private static void BuildSecondary(Transform sheet, TMP_FontAsset font)
    {
        var go = NewChild(sheet, "SecondaryCta", typeof(RectTransform), typeof(Button));
        SetBottomStretch((RectTransform)go.transform, SecondaryBottom, SecondaryHeight, SideInset);

        // No Image: the label TMP IS the Button's targetGraphic, and its rect spans the whole
        // 120-unit row — so the tap target is the row, not the glyphs, without an extra
        // transparent fill to keep in sync.
        secondaryLabel = BuildTmp(go.transform, "Label", SecondarySize, font);
        StretchFull((RectTransform)secondaryLabel.transform);
        secondaryLabel.raycastTarget = true;

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = secondaryLabel;
        PopupUI.WireFingerUp(button, Dismiss);
    }

    /// <summary>
    /// Drag-to-dismiss over the grabber/title strip, reusing the shared
    /// <see cref="SheetDragDismiss"/> that the scene sheets already use. Last sibling so it
    /// wins the pointer raycast over the title it covers (uGUI awards a pointer to the LATER
    /// sibling); it deliberately stops above the body so it never shadows the two buttons.
    /// </summary>
    private static void BuildDragZone(Transform sheet)
    {
        var go = NewChild(sheet, "DragZone", typeof(RectTransform), typeof(Image));
        go.transform.SetAsLastSibling();
        SetTopStretch((RectTransform)go.transform, 0f, DragZoneHeight, 0f);

        var image = go.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;

        var drag = go.AddComponent<SheetDragDismiss>();
        drag.Bind(sheetRect, scrimGroup);
        drag.onDismiss.AddListener(Dismiss);
    }

    // ── Build helpers (BotActivationConfirm's set) ────────────────────────────

    /// <summary>
    /// Every call names typeof(RectTransform) explicitly: only a Graphic's RequireComponent
    /// brings one along, and two of these nodes (Panel, SecondaryCta) carry no Graphic.
    /// </summary>
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

    private static void SetBottomStretch(RectTransform rt, float bottom, float height, float sideInset)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, bottom);
        rt.sizeDelta = new Vector2(-sideInset * 2f, height);
    }

    /// <summary>
    /// Weight comes from the FONT ASSET, never from <see cref="FontStyles"/> — the same rule
    /// PaywallBuilder follows. Stacking a synthetic bold on the semibold asset would make this
    /// sheet the one place in the billing UI whose type is heavier than everything around it.
    /// </summary>
    private static TextMeshProUGUI BuildTmp(Transform parent, string name, float size, TMP_FontAsset font)
    {
        var go = NewChild(parent, name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;   // never rely on TMP's default
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        return tmp;
    }
}
