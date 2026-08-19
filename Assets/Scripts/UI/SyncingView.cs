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

    // Authored (WhatsApp) accent colors captured ONCE at Awake so the Telegram recolor maps FROM
    // the real authored greens (never a hardcoded scene value) and reverts BYTE-IDENTICAL on the
    // WhatsApp channel — the cover is a persistent widget reused across channel switches (D14).
    // Covered: the spinner ring Image (#25D366), the "sync" progress fill (#25D366), and the
    // countdown label (#1FA855). All null-guarded. Mirrors EmptyStateView.CacheAccentColors.
    private Image spinnerImage;                 // resolved from the spinner RectTransform at Awake
    private Color spinnerAuthoredColor;
    private Color progressFillAuthoredColor;
    private Color countdownAuthoredColor;
    private bool accentColorsCached;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        ApplyCopy();
        ApplyChannelAccent();
        Hide();
    }

    private void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnWhatsAppSyncing += HandleSyncing;
            ChatManager.Instance.OnWhatsAppSyncReady += HandleReady;
            ChatManager.Instance.OnActiveBotChanged += HandleActiveBotChanged;
            ChatManager.Instance.OnActiveChannelChanged += HandleActiveChannelChanged;

            // Catch up: tab re-opened or app relaunched mid-window — resume without an
            // event, for whichever channel is active (the window is per-channel since 08-19).
            if (ChatManager.Instance.IsChannelSyncing(
                    ChatManager.Instance.CurrentBotId, ChatManager.Instance.ActiveChannel, out long untilMs))
            {
                // Windows can open outside BeginLoadForActiveBot (the settings late-auth
                // stamp writes {bot}…SyncUntil with no ChatManager call), so a shown cover
                // is not guaranteed a running OnWhatsAppSyncReady producer — arm it here.
                ChatManager.Instance.EnsureSyncWaitArmed();
                HandleSyncing(untilMs);
                return;
            }
        }

        // Not inside a window (or no gate to ask): hide. OnWhatsAppSyncReady is a
        // one-shot this view unsubscribes from in OnDisable, so a window that expired
        // while the screen was inactive already fired it into nobody — a cover left
        // visible at disable time would otherwise be stranded until app restart.
        Hide();
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
        ApplyCopy();          // re-resolve wording for the channel that is showing the cover
        ApplyChannelAccent(); // re-resolve accent (spinner/fill/countdown) for that same channel
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
    /// (its buckets are pinned by WhatsAppSyncTests); Telegram mirrors the same
    /// rounding buckets. Both channels are Russian — the app ships RU-only.
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

        // WhatsApp copy — RU, mirrors the Telegram branch word for word.
        if (titleLabel != null) titleLabel.text = "Готовим всё к работе";
        if (bodyLabel != null) bodyLabel.text = "Импортируем ваши чаты и сообщения из WhatsApp.";
        if (footnoteLabel != null) footnoteLabel.text = "Можно пользоваться приложением — чаты появятся здесь, когда будут готовы.";
    }

    // Capture each green element's OWN authored scene color once, so WhatsApp reverts
    // byte-identically (never a hardcoded scene green). Mirrors EmptyStateView.CacheAccentColors.
    private void CacheAccentColors()
    {
        if (accentColorsCached) return;
        accentColorsCached = true;
        if (spinner != null) spinnerImage = spinner.GetComponent<Image>();
        if (spinnerImage != null) spinnerAuthoredColor = spinnerImage.color;
        if (progressFill != null) progressFillAuthoredColor = progressFill.color;
        if (countdownLabel != null) countdownAuthoredColor = countdownLabel.color;
    }

    /// <summary>
    /// D14: recolor the cover's green elements (spinner ring, "sync" progress fill, countdown) to
    /// Telegram brand blue on the Telegram channel; every other channel keeps its authored green
    /// byte-identically (Resolve pass-through). Mirrors EmptyStateView.ApplyChannelAccent
    /// — runtime only, no scene stamp. Re-applied whenever the cover (re)shows so a channel switch is
    /// reflected without re-authoring the scene. TickRoutine sets countdownLabel.text and
    /// progressFill.fillAmount but NOT their colors, so a one-time recolor at show holds all window.
    /// </summary>
    private void ApplyChannelAccent()
    {
        CacheAccentColors();
        ChatChannel channel = ActiveChannelOrDefault();
        if (spinnerImage != null)   spinnerImage.color   = ChannelAccent.Resolve(channel, spinnerAuthoredColor);
        if (progressFill != null)   progressFill.color   = ChannelAccent.Resolve(channel, progressFillAuthoredColor);
        if (countdownLabel != null) countdownLabel.color = ChannelAccent.Resolve(channel, countdownAuthoredColor);
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
