using System.Collections;
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

    // Minimum-visible-duration bookkeeping (D9 root cause H1): a fast Telegram sync fires
    // OnChatListSyncStart→OnChatListSyncEnd only a network round-trip apart, flashing the pill
    // sub-legibly ("no cue shows" on device). A settled sync therefore hides THROUGH
    // ChatListSyncIndicatorGate — the pill holds until MinVisibleSeconds elapses. _shownAtRealtime
    // is when the current spin started; _deferredHide is the pending post-floor hide (if any).
    private float _shownAtRealtime;
    private Coroutine _deferredHide;

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
        KillDeferredHide();
        StopSpinner();
    }

    // TG-gated: on WhatsApp the start signal is ignored, so the WhatsApp list is byte-identical.
    private void HandleSyncStart()
    {
        if (IsTelegram()) BeginSpin();
    }

    // Hide THROUGH the min-visible gate (H1): a fast sync must still register as a cue. If the
    // legible floor has already elapsed, hide now; otherwise hold the pill for the remaining floor
    // via a deferred-hide coroutine. The sync has genuinely ended here (OnChatListSyncEnd fires in
    // SyncAllChats' finally, after _chatListSyncing=false), so syncStillInFlight is false.
    private void HandleSyncEnd()
    {
        // Never shown (WhatsApp ignores the start signal, or a switch-away already hid us) — no-op,
        // so WhatsApp stays byte-identical and no deferred coroutine is ever started off-Telegram.
        if (canvasGroup == null || canvasGroup.alpha <= 0f) return;

        float now = Time.realtimeSinceStartup;
        if (ChatListSyncIndicatorGate.ShouldHideNow(
                _shownAtRealtime, now, ChatListSyncIndicatorGate.MinVisibleSeconds, syncStillInFlight: false))
        {
            Hide();
            return;
        }

        KillDeferredHide();
        _deferredHide = StartCoroutine(DeferredHideRoutine());
    }

    // A switch AWAY from Telegram hides any stale pill immediately (guards the "stuck syncing"
    // case where StopAllCoroutines abandoned the in-flight SyncAllChats before its finally ran)
    // and kills any pending deferred-hide. A switch TO Telegram while a chat-list sync is already
    // in flight re-shows the pill (H3): HandleSyncStart already ran under the previous channel and
    // its IsTelegram() gate dropped it; SetActiveChannel re-syncs after the flip, but this catches
    // a sync that is mid-flight at the moment the channel event fires.
    private void HandleActiveChannelChanged(ChatChannel channel)
    {
        if (channel != ChatChannel.Telegram) { Hide(); return; }
        if (ChatManager.Instance != null && ChatManager.Instance.IsChatListSyncing) BeginSpin();
    }

    // A bot switch always rebuilds the list; hide any pill. If the new bot is TG-syncing, the
    // follow-on OnChatListSyncStart re-shows it. Mirrors SyncingView.HandleActiveBotChanged.
    private void HandleActiveBotChanged(string _) => Hide();

    private bool IsTelegram() =>
        ChatManager.Instance != null && ChatManager.Instance.ActiveChannel == ChatChannel.Telegram;

    private void BeginSpin()
    {
        // A new sync cancels any pending deferred-hide and re-arms the visible floor. This is also
        // the H5 privacy-clear rescue: ClearAllLocalHistory does StopAllCoroutines + _chatListSyncing=false
        // WITHOUT firing OnChatListSyncEnd, so the killed sync's hide never fires — the follow-on
        // BeginLoadForActiveBot resync's OnChatListSyncStart lands here and re-owns the pill.
        KillDeferredHide();
        _shownAtRealtime = Time.realtimeSinceStartup;
        Show();
        StartSpinner();
    }

    // Hold the pill for the un-elapsed visible floor, then hide — unless a new sync re-armed it
    // meanwhile (BeginSpin kills this coroutine) or a sync is somehow still in flight (T-08-12-01).
    private IEnumerator DeferredHideRoutine()
    {
        float remaining = ChatListSyncIndicatorGate.RemainingVisibleSeconds(
            _shownAtRealtime, Time.realtimeSinceStartup, ChatListSyncIndicatorGate.MinVisibleSeconds);
        if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);

        _deferredHide = null; // clear before Hide so its KillDeferredHide is a no-op
        if (ChatManager.Instance == null || !ChatManager.Instance.IsChatListSyncing) Hide();
    }

    // Kill a pending deferred-hide so it can never outlive a bot/channel switch, a fresh sync, or a
    // disable (T-08-12-01 stuck-pill mitigation). Safe to call when none is pending.
    private void KillDeferredHide()
    {
        if (_deferredHide != null) { StopCoroutine(_deferredHide); _deferredHide = null; }
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
        KillDeferredHide();
        StopSpinner();
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }
}
