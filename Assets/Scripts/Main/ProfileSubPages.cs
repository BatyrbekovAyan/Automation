using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controller for the Profile tab's sub-pages (Аккаунт / Уведомления /
/// Конфиденциальность / Поддержка / О приложении / Лицензии). Panels are
/// built by ProfileSubPagesBuilder inside Screen_Profile; this component
/// lives on the always-active SubPages root and slides panels in from the
/// right, matching the app's page-transition language. Per-page logic lives
/// in the ProfileSubPages.*.cs partials.
/// </summary>
public partial class ProfileSubPages : MonoBehaviour
{
    public static ProfileSubPages Instance;

    public enum Page { Account = 0, Notifications = 1, Privacy = 2, Support = 3, About = 4, Licenses = 5 }

    [Serializable]
    private struct PageRefs
    {
        public RectTransform panel;
        public Button backButton;
        public SwipeToBackPanel swipe;
    }

    [Header("Pages (indexed by the Page enum)")]
    [SerializeField] private PageRefs[] pages = new PageRefs[6];

    [Header("Shared confirm popup")]
    [SerializeField] private GameObject confirmPopup;
    [SerializeField] private TextMeshProUGUI confirmTitle;
    [SerializeField] private TextMeshProUGUI confirmMessage;
    [SerializeField] private Button confirmActionButton;
    [SerializeField] private TextMeshProUGUI confirmActionLabel;
    [SerializeField] private Button confirmCancelButton;

    private const float SlideInDuration = 0.3f;
    private const float SlideOutDuration = 0.25f;

    private static readonly Color InkColor = new Color32(0x1A, 0x1A, 0x2E, 0xFF);
    private static readonly Color MutedColor = new Color32(0x65, 0x67, 0x6B, 0xFF);
    private static readonly Color DisabledColor = new Color32(0xC7, 0xC7, 0xCC, 0xFF);

    private Canvas _rootCanvas;
    private Action _pendingConfirmAction;
    private Tween _activeSlide;
    private TextMeshProUGUI _toastLabel;
    private Tween _toastTween;

    private void Awake()
    {
        Instance = this;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null) _rootCanvas = canvas.rootCanvas;
    }

    private void Start()
    {
        for (int i = 0; i < pages.Length; i++)
        {
            int index = i;
            if (pages[i].backButton != null)
                pages[i].backButton.onClick.AddListener(() => Close((Page)index));
            if (pages[i].swipe != null)
                pages[i].swipe.OnCommitted = () => FinishClose((Page)index);
        }

        WireConfirmPopup();
        WireAccount();
        WireNotifications();
        WirePrivacy();
        WireSupport();
        WireAbout();
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    public void Open(Page page)
    {
        var panel = PanelFor(page);
        if (panel == null) return;

        panel.gameObject.SetActive(true);
        OnPageOpened(page);

        _activeSlide?.Kill();
        panel.anchoredPosition = new Vector2(CanvasWidth(), panel.anchoredPosition.y);
        _activeSlide = panel.DOAnchorPosX(0f, SlideInDuration).SetEase(Ease.OutCubic);
    }

    public void Close(Page page)
    {
        var panel = PanelFor(page);
        if (panel == null) return;

        _activeSlide?.Kill();
        _activeSlide = panel.DOAnchorPosX(CanvasWidth(), SlideOutDuration)
            .SetEase(Ease.InCubic)
            .OnComplete(() => FinishClose(page));
    }

    // Swipe commit has already animated the panel off-screen — just settle state.
    private void FinishClose(Page page)
    {
        var panel = PanelFor(page);
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private RectTransform PanelFor(Page page)
    {
        int index = (int)page;
        return index >= 0 && index < pages.Length ? pages[index].panel : null;
    }

    private float CanvasWidth() =>
        _rootCanvas != null ? _rootCanvas.GetComponent<RectTransform>().rect.width : 1080f;

    // Per-page refresh on open; bodies live in the page partials.
    private void OnPageOpened(Page page)
    {
        switch (page)
        {
            case Page.Account: RefreshAccountCard(); break;
            case Page.Notifications: RefreshNotificationToggles(); break;
            case Page.Privacy: RefreshPrivacyPage(); break;
            case Page.Support: ResetFaq(); break;
            case Page.About: RefreshAbout(); break;
            case Page.Licenses: RefreshLicenses(); break;
        }
    }

    // ── Shared confirm popup (PopupUI pattern) ─────────────────────────────

    private void WireConfirmPopup()
    {
        if (confirmCancelButton != null) PopupUI.WireFingerUp(confirmCancelButton, HideConfirm);
        if (confirmActionButton != null) PopupUI.WireFingerUp(confirmActionButton, RunConfirmAction);
        if (confirmPopup == null) return;

        PopupUI.WireFingerUp(confirmPopup, HideConfirm);
        var card = confirmPopup.transform.Find("Card");
        if (card != null) PopupUI.AbsorbEvents(card);
    }

    private void ShowConfirm(string title, string message, string actionLabel, Action onConfirm)
    {
        _pendingConfirmAction = onConfirm;
        if (confirmTitle != null) confirmTitle.text = title;
        if (confirmMessage != null) confirmMessage.text = message;
        if (confirmActionLabel != null) confirmActionLabel.text = actionLabel;
        PopupUI.Show(confirmPopup);
    }

    private void HideConfirm()
    {
        _pendingConfirmAction = null;
        PopupUI.Hide(confirmPopup);
    }

    private void RunConfirmAction()
    {
        var action = _pendingConfirmAction;
        _pendingConfirmAction = null;
        PopupUI.Hide(confirmPopup);
        action?.Invoke();
    }

    // ── Transient toast (lazy label, AttachmentPreviewScreen pattern) ──────

    private void ShowToast(RectTransform panel, string text)
    {
        if (panel == null) return;
        EnsureToastLabel();
        if (_toastLabel == null) return;

        var rt = _toastLabel.rectTransform;
        rt.SetParent(panel, false);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 220f);
        rt.sizeDelta = new Vector2(-96f, 100f);

        _toastLabel.text = text;
        _toastLabel.gameObject.SetActive(true);
        _toastLabel.alpha = 1f;

        _toastTween?.Kill();
        _toastTween = DOTween.Sequence()
            .AppendInterval(2.2f)
            .Append(DOTween.To(() => _toastLabel.alpha, v => _toastLabel.alpha = v, 0f, 0.3f))
            .OnComplete(() => { if (_toastLabel != null) _toastLabel.gameObject.SetActive(false); });
    }

    private void EnsureToastLabel()
    {
        if (_toastLabel != null) return;

        var go = new GameObject("Toast", typeof(RectTransform));
        _toastLabel = go.AddComponent<TextMeshProUGUI>();
        if (confirmTitle != null) _toastLabel.font = confirmTitle.font; // Bold SDF stamped by the builder
        _toastLabel.fontSize = 36f;
        _toastLabel.color = InkColor;
        _toastLabel.alignment = TextAlignmentOptions.Center;
        _toastLabel.textWrappingMode = TextWrappingModes.Normal; // project TMP default is NoWrap
        _toastLabel.raycastTarget = false;
    }
}
