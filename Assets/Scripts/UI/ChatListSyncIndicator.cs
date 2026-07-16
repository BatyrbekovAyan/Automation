using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// A lightweight "syncing" pill for the Telegram chat list — the WhatsApp-parity affordance
/// requested in D9. WhatsApp has its own post-creation "Setting things up" window
/// (<see cref="SyncingView"/>); Telegram's list appears instantly from cache with NO refresh
/// cue, so this shows a small rotating spinner + «Синхронизация…» pill at the top of the list
/// WHILE a <c>chats/filter</c> sync is in flight — but ONLY on the Telegram channel.
///
/// Driven by ChatManager's channel-agnostic <see cref="ChatManager.OnChatListSyncStart"/> /
/// <see cref="ChatManager.OnChatListSyncEnd"/> (fired around SyncAllChats' try/finally). Display
/// is TG-gated here, so WhatsApp's behaviour — and its SyncingView window — are byte-identical:
/// on WhatsApp the start signal is simply ignored.
///
/// Sibling of <see cref="EmptyStateView"/> / <see cref="SyncingView"/> under ChatsPanel;
/// CanvasGroup-toggled like them. Visual refs are stamped by ChatListSyncIndicatorBuilder; every
/// ref is null-guarded so a missing stamp (or a null ChatManager) is a clean no-op, never a crash.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ChatListSyncIndicator : MonoBehaviour
{
    [Header("UI references (stamped by ChatListSyncIndicatorBuilder)")]
    [SerializeField] private RectTransform spinner;
    [SerializeField] private TextMeshProUGUI label;

    private const string SyncingText = "Синхронизация…";

    private CanvasGroup canvasGroup;
    private Tween spinnerTween;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (label != null) label.text = SyncingText;
        Hide();
    }

    private void OnEnable()
    {
        if (ChatManager.Instance == null) return;
        ChatManager.Instance.OnChatListSyncStart += HandleSyncStart;
        ChatManager.Instance.OnChatListSyncEnd += HandleSyncEnd;
        ChatManager.Instance.OnActiveChannelChanged += HandleActiveChannelChanged;
        ChatManager.Instance.OnActiveBotChanged += HandleActiveBotChanged;

        // Catch up: this panel activates lazily (Screen_Whatsapp is inactive at scene load), so a
        // sync already in flight fired OnChatListSyncStart before we subscribed. Resume the pill
        // only on Telegram — WhatsApp never shows it.
        if (IsTelegram() && ChatManager.Instance.IsChatListSyncing) BeginSpin();
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatListSyncStart -= HandleSyncStart;
            ChatManager.Instance.OnChatListSyncEnd -= HandleSyncEnd;
            ChatManager.Instance.OnActiveChannelChanged -= HandleActiveChannelChanged;
            ChatManager.Instance.OnActiveBotChanged -= HandleActiveBotChanged;
        }
        StopSpinner();
    }

    // TG-gated: on WhatsApp the start signal is ignored, so the WhatsApp list is byte-identical.
    private void HandleSyncStart()
    {
        if (IsTelegram()) BeginSpin();
    }

    private void HandleSyncEnd() => Hide();

    // A switch AWAY from Telegram hides any stale pill immediately (guards the "stuck syncing"
    // case where StopAllCoroutines abandoned the in-flight SyncAllChats before its finally ran).
    // A switch TO Telegram waits for the next OnChatListSyncStart (SetActiveChannel re-syncs).
    private void HandleActiveChannelChanged(ChatChannel channel)
    {
        if (channel != ChatChannel.Telegram) Hide();
    }

    // A bot switch always rebuilds the list; hide any pill. If the new bot is TG-syncing, the
    // follow-on OnChatListSyncStart re-shows it. Mirrors SyncingView.HandleActiveBotChanged.
    private void HandleActiveBotChanged(string _) => Hide();

    private bool IsTelegram() =>
        ChatManager.Instance != null && ChatManager.Instance.ActiveChannel == ChatChannel.Telegram;

    private void BeginSpin()
    {
        Show();
        StartSpinner();
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

    private void StopSpinner()
    {
        spinnerTween?.Kill();
        spinnerTween = null;
    }

    private void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        // Purely informational overlay — never intercept taps on the list beneath it.
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void Hide()
    {
        StopSpinner();
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }
}
