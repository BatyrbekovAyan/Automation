using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Floating "scroll to newest" button with an unread-count badge. A self-contained widget:
/// it raises OnClicked and exposes Show/Hide/SetCount, but knows nothing about chats or
/// scrolling — MessageListView owns that policy. Starts hidden.
/// </summary>
public class ScrollToBottomFab : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private GameObject badgeRoot;
    [SerializeField] private TMP_Text badgeText;

    public bool IsShown { get; private set; }

    public event Action OnClicked;

    private Tween _fadeTween;

    // The unread-count badge pill's authored (WhatsApp-green #26B25A) fill, captured once from
    // the scene so the Telegram-blue recolor maps FROM the real authored value and never hardcodes
    // a scene green. Only the badge is a green accent — the FAB body is a white circle and the
    // Button's image is a transparent hit area, so neither is touched (badge text stays white,
    // readable on both green and blue). The FAB is a persistent widget: it is NOT re-instantiated
    // across chat opens or channel switches, so ApplyChannelAccent sets the color explicitly EVERY
    // call — a WhatsApp bind after a Telegram one reverts the pill to exactly this cached green.
    // Mirrors ChatItemView's unread-pill cache (same #26B25A accent; this badge is its messages-view twin).
    private Image _badgeImage;
    private Color _authoredBadgeColor;
    private bool _accentCached;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(HandleClick);

        CacheAccentColor();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        IsShown = false;
        if (badgeRoot != null) badgeRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
        _fadeTween?.Kill();
    }

    private void CacheAccentColor()
    {
        if (_accentCached) return;
        _accentCached = true;
        if (badgeRoot != null) _badgeImage = badgeRoot.GetComponent<Image>();
        if (_badgeImage != null) _authoredBadgeColor = _badgeImage.color;
    }

    /// <summary>
    /// Recolor the green unread-count badge for <paramref name="channel"/>: Telegram ⇒ brand blue,
    /// every other channel ⇒ the authored WhatsApp green (ChannelAccent passthrough, byte-identical).
    /// Only the badge pill is recolored — the FAB's white circle body and transparent hit-area image
    /// are not green accents. MessageListView calls this on chat-open bind (which also covers channel
    /// switches, since a switch closes+reopens the chat); the FAB persists, so the color is set every
    /// call rather than left, guaranteeing a WhatsApp rebind reverts the pill to the cached green.
    /// </summary>
    public void ApplyChannelAccent(ChatChannel channel)
    {
        CacheAccentColor();
        if (_badgeImage != null)
            _badgeImage.color = ChannelAccent.Resolve(channel, _authoredBadgeColor);
    }

    public void Show()
    {
        if (IsShown) return;
        IsShown = true;
        if (canvasGroup == null) return;

        _fadeTween?.Kill();
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        _fadeTween = canvasGroup.DOFade(1f, 0.2f).SetEase(Ease.OutQuad);
    }

    public void Hide()
    {
        if (!IsShown) return;
        IsShown = false;
        if (canvasGroup == null) return;

        _fadeTween?.Kill();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        _fadeTween = canvasGroup.DOFade(0f, 0.2f).SetEase(Ease.OutQuad);
    }

    public void SetCount(int count)
    {
        if (badgeRoot != null) badgeRoot.SetActive(count > 0);
        if (badgeText != null) badgeText.text = count > 99 ? "99+" : count.ToString();
    }

    private void HandleClick()
    {
        transform.DOPunchScale(Vector3.one * 0.08f, 0.2f, 6, 0.8f).SetLink(gameObject);
        OnClicked?.Invoke();
    }
}
