using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Drives the post-creation "setting things up" cover shared by BOTH channels
/// (08-19 D13a). Shows while the active bot's ACTIVE channel is inside its fixed
/// sync window, ticking a time-based progress bar and countdown, then hides when
/// ChatManager signals the window has elapsed. Copy is channel-aware: WhatsApp
/// keeps its original English wording byte-identically; Telegram shows Russian.
/// Sibling of EmptyState under the shared ChatsPanel; CanvasGroup-toggled.
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
        ChatManager.Instance.OnActiveChannelChanged += HandleActiveChannelChanged;

        // Catch up: tab re-opened or app relaunched mid-window — resume without an
        // event, for whichever channel is active (the window is per-channel since 08-19).
        if (ChatManager.Instance.IsChannelSyncing(
                ChatManager.Instance.CurrentBotId, ChatManager.Instance.ActiveChannel, out long untilMs))
            HandleSyncing(untilMs);
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnWhatsAppSyncing -= HandleSyncing;
            ChatManager.Instance.OnWhatsAppSyncReady -= HandleReady;
            ChatManager.Instance.OnActiveBotChanged -= HandleActiveBotChanged;
            ChatManager.Instance.OnActiveChannelChanged -= HandleActiveChannelChanged;
        }
        StopTicking();
    }

    private void HandleSyncing(long untilMs)
    {
        syncUntilUnixMs = untilMs;
        ApplyCopy(); // re-resolve wording for the channel that is showing the cover
        Show();
        StopTicking();
        StartSpinner();
        tickRoutine = StartCoroutine(TickRoutine());
    }

    private void HandleReady() => Hide();

    // A bot switch hides any stale syncing screen. If the newly active bot is also
    // syncing, BeginLoadForActiveBot fires OnWhatsAppSyncing right after and we re-show.
    private void HandleActiveBotChanged(string _) => Hide();

    // A channel switch hides any stale cover the same way: SetActiveChannel calls
    // BeginLoadForActiveBot in the SAME synchronous stack right after this event, so if
    // the newly active channel is also mid-window we re-show with that channel's copy —
    // through the load path's own guards (profile validity), never a duplicate of them.
    private void HandleActiveChannelChanged(ChatChannel _) => Hide();

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
                countdownLabel.text = FormatCountdownFor(ActiveChannelOrDefault(), remaining);
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

    /// <summary>
    /// Countdown label per channel: WhatsApp delegates to WhatsAppSyncGate byte-identically
    /// (its English buckets are pinned by WhatsAppSyncGateTests); Telegram mirrors the same
    /// rounding buckets in Russian. Pure + static so EditMode tests pin both contracts.
    /// </summary>
    public static string FormatCountdownFor(ChatChannel channel, long remainingMs)
    {
        if (channel != ChatChannel.Telegram) return WhatsAppSyncGate.FormatCountdown(remainingMs);
        if (remainingMs <= 0L) return "Завершаем…";
        int totalSeconds = (int)((remainingMs + 999L) / 1000L); // round up to whole seconds
        if (totalSeconds <= 60) return "Осталось меньше минуты";
        int minutes = (totalSeconds + 59) / 60;                 // round up to whole minutes
        return $"Осталось около {minutes} мин";
    }

    /// <summary>Active channel, defaulting to WhatsApp when ChatManager is not up yet (Awake order).</summary>
    private static ChatChannel ActiveChannelOrDefault() =>
        ChatManager.Instance != null ? ChatManager.Instance.ActiveChannel : ChatChannel.WhatsApp;

    private void ApplyCopy()
    {
        if (ActiveChannelOrDefault() == ChatChannel.Telegram)
        {
            // Telegram wording is Russian — the app's Telegram-facing copy language (D8).
            if (titleLabel != null) titleLabel.text = "Готовим всё к работе";
            if (bodyLabel != null) bodyLabel.text = "Импортируем ваши чаты и сообщения из Telegram.";
            if (footnoteLabel != null) footnoteLabel.text = "Можно пользоваться приложением — чаты появятся здесь, когда будут готовы.";
            return;
        }

        // WhatsApp copy — byte-identical to the original cover.
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
