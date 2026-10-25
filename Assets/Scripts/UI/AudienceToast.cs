using System.Collections.Generic;
using DG.Tweening;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// «Личные и рабочие чаты»: the feature's short line at the bottom of the screen — «Чат перенесён в «Личные»», «Чат стал
/// рабочим», «Не удалось перенести чат», … (every text is PersonalChatsCopy's). A lazy overlay: its own canvas at
/// sortingOrder 3 (BotActivationConfirm's place — above every screen, below the text-selection overlay), built on the first
/// <see cref="Show"/> and never in the scene. It stays <see cref="HoldSeconds"/> and fades; a second Show replaces the text
/// and restarts the clock. It never takes a tap (no raycaster, no raycast targets). The label is TMP's default font asset —
/// SF Pro Regular in this project (TMP Settings) — and the colours are stamped from the theme on every Show.
///
/// A move the server refused (<see cref="ChatAudienceSyncService.OnWriteFailed"/>) is told here as
/// <see cref="PersonalChatsCopy.WriteFailed"/>: the subscription is made at launch, so a refusal that lands before anything
/// was ever shown is heard too. A network fault never raises that event — the move waits in the queue and is retried.
/// </summary>
public static class AudienceToast
{
    /// <summary>How long a toast is on the screen, its fades included.</summary>
    public const float HoldSeconds = 3f;

    private const float FadeInSeconds = 0.2f;
    private const float FadeOutSeconds = 0.3f;
    private const int OverlaySortingOrder = 3;      // above screens, below SelectionOverlay (4)
    private const float BottomOffset = 320f;        // clears the 210u tab bar and the chat composer
    private const float FontSize = 36f;
    private const float MaxTextWidth = 880f;
    private const int PadX = 40, PadY = 24;
    private const float Radius = 32f;

    private static GameObject pill;
    private static CanvasGroup group;
    private static Image background;
    private static TextMeshProUGUI label;
    private static Sequence running;

    /// <summary>What the toast was last asked to say (EditMode builds nothing — the tests read this).</summary>
    internal static string LastText { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    internal static void Boot()
    {
        // Remove first: a launch without a domain reload keeps the static event's old subscribers.
        ChatAudienceSyncService.OnWriteFailed -= HandleWriteFailed;
        ChatAudienceSyncService.OnWriteFailed += HandleWriteFailed;
    }

    /// <summary>Shows <paramref name="text"/> for <see cref="HoldSeconds"/>; an empty text shows nothing.</summary>
    public static void Show(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        LastText = text;
        if (!Application.isPlaying) return;

        EnsureBuilt();
        label.text = text;
        Paint();
        pill.SetActive(true);

        running?.Kill();
        group.alpha = 0f;
        running = DOTween.Sequence()
            .Append(group.DOFade(1f, FadeInSeconds))
            .AppendInterval(HoldSeconds - FadeInSeconds - FadeOutSeconds)
            .Append(group.DOFade(0f, FadeOutSeconds))
            .OnComplete(() => { if (pill != null) pill.SetActive(false); });
    }

    internal static void ResetForTests()
    {
        ChatAudienceSyncService.OnWriteFailed -= HandleWriteFailed;
        LastText = null;
    }

    private static void HandleWriteFailed(string botName, IReadOnlyList<string> chatIds) => Show(PersonalChatsCopy.WriteFailed);

    // The pill fits its text (a layout group + a fitter), capped so a long text wraps inside the screen.
    private static void EnsureBuilt()
    {
        if (pill != null) return;

        var canvasGo = new GameObject("AudienceToastCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0f;

        pill = new GameObject("Toast", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(CanvasGroup), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        pill.layer = canvasGo.layer;
        pill.transform.SetParent(canvasGo.transform, false);
        var rect = (RectTransform)pill.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, BottomOffset);

        background = pill.GetComponent<Image>();
        background.raycastTarget = false;
        pill.AddComponent<ImageWithRoundedCorners>().radius = Radius;
        group = pill.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var layout = pill.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(PadX, PadX, PadY, PadY);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = pill.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildLabel();
        pill.SetActive(false);
    }

    private static void BuildLabel()
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = pill.layer;
        go.transform.SetParent(pill.transform, false);
        label = go.GetComponent<TextMeshProUGUI>();   // TMP's default font asset: SF Pro Regular
        label.fontSize = FontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;   // the project's TMP default is NoWrap
        label.raycastTarget = false;
        go.AddComponent<TMPMaxWidthLayoutElement>().MaxWidth = MaxTextWidth;
    }

    // A theme snapshot per Show — a three-second line needs no live binding.
    private static void Paint()
    {
        background.color = Theme.Color(ThemeRole.Border);
        label.color = Theme.Color(ThemeRole.InkPrimary);
    }
}
