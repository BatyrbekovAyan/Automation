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

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(HandleClick);

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
