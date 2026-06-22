using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Drives the WhatsApp "Setting things up" syncing screen. Shows while the active
/// bot is inside its fixed sync window, ticking a time-based progress bar and
/// countdown, then hides when ChatManager signals the window has elapsed.
/// Sibling of EmptyState under ChatsPanel; CanvasGroup-toggled like EmptyStateView.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class SyncingView : MonoBehaviour
{
    [Header("UI references")]
    [SerializeField] private RectTransform spinner;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI bodyLabel;
    [SerializeField] private Image progressFill;          // Image.type = Filled, Horizontal, fillAmount 0
    [SerializeField] private TextMeshProUGUI countdownLabel;
    [SerializeField] private TextMeshProUGUI footnoteLabel;

    private CanvasGroup canvasGroup;
    private Coroutine tickRoutine;
    private Tween spinnerTween;
    private long syncUntilUnixMs;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        ApplyCopy();
        Hide();
    }

    private void OnEnable()
    {
        if (ChatManager.Instance == null) return;
        ChatManager.Instance.OnWhatsAppSyncing += HandleSyncing;
        ChatManager.Instance.OnWhatsAppSyncReady += HandleReady;
        ChatManager.Instance.OnActiveBotChanged += HandleActiveBotChanged;

        // Catch up: tab re-opened or app relaunched mid-window — resume without an event.
        if (ChatManager.Instance.IsWhatsAppSyncing(ChatManager.Instance.CurrentBotId, out long untilMs))
            HandleSyncing(untilMs);
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnWhatsAppSyncing -= HandleSyncing;
            ChatManager.Instance.OnWhatsAppSyncReady -= HandleReady;
            ChatManager.Instance.OnActiveBotChanged -= HandleActiveBotChanged;
        }
        StopTicking();
    }

    private void HandleSyncing(long untilMs)
    {
        syncUntilUnixMs = untilMs;
        Show();
        StopTicking();
        StartSpinner();
        tickRoutine = StartCoroutine(TickRoutine());
    }

    private void HandleReady() => Hide();

    // A bot switch hides any stale syncing screen. If the newly active bot is also
    // syncing, BeginLoadForActiveBot fires OnWhatsAppSyncing right after and we re-show.
    private void HandleActiveBotChanged(string _) => Hide();

    private IEnumerator TickRoutine()
    {
        while (true)
        {
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long remaining = WhatsAppSyncGate.RemainingMs(syncUntilUnixMs, now);
            if (progressFill != null)
                progressFill.fillAmount =
                    WhatsAppSyncGate.ProgressFraction(syncUntilUnixMs, now, ChatManager.WhatsAppSyncWindowSeconds);
            if (countdownLabel != null)
                countdownLabel.text = WhatsAppSyncGate.FormatCountdown(remaining);
            if (remaining <= 0L) { tickRoutine = null; yield break; }
            yield return new WaitForSecondsRealtime(1f);
        }
    }

    private void StartSpinner()
    {
        if (spinner == null) return;
        spinnerTween?.Kill();
        spinner.localEulerAngles = Vector3.zero;
        spinnerTween = spinner
            .DOLocalRotate(new Vector3(0f, 0f, -360f), 1f, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear).SetLoops(-1).SetUpdate(true);
    }

    private void StopTicking()
    {
        if (tickRoutine != null) { StopCoroutine(tickRoutine); tickRoutine = null; }
        spinnerTween?.Kill();
        spinnerTween = null;
    }

    private void ApplyCopy()
    {
        if (titleLabel != null) titleLabel.text = "Setting things up";
        if (bodyLabel != null) bodyLabel.text = "We're importing your chats and messages from WhatsApp.";
        if (footnoteLabel != null) footnoteLabel.text = "You can keep using the app. Chats appear here when ready.";
    }

    private void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    private void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        StopTicking();
    }
}
