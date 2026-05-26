using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class AttachSheet : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Sheet height in canvas pixels — used as fallback when no keyboard height has been observed yet.")]
    [SerializeField] private float sheetHeightCanvasPx = 290f;

    [Header("References")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private KeyboardAwarePanel keyboardPanel;
    [SerializeField] private MessagesBottomPanel messagesBottomPanel;
    [SerializeField] private Button cameraButton;
    [SerializeField] private Button galleryButton;
    [SerializeField] private Button documentButton;

    [Header("Tween Timings")]
    [SerializeField] private float openDuration  = 0.30f;
    [SerializeField] private float closeDuration = 0.25f;

    public event Action<AttachmentPick> OnPicked;

    private RectTransform _rt;
    private Canvas        _canvas;
    private bool          _isOpen;
    private bool          _openedOverKeyboard;
    private bool          _isAnimating;
    private bool          _suppressDeselectListener;
    private Tween         _insetTween;

    void Awake()
    {
        _rt     = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();

        // Debug self-subscriber so wiring is observable in editor without a real consumer.
        OnPicked += pick =>
            Debug.Log($"[AttachSheet] OnPicked: kind={pick.Kind} file={pick.FileName} " +
                      $"size={pick.FileSizeBytes} mime={pick.MimeType} path={pick.Path}");
    }

    void OnEnable()
    {
        if (cameraButton   != null) cameraButton.onClick.AddListener(OnCameraTapped);
        if (galleryButton  != null) galleryButton.onClick.AddListener(OnGalleryTapped);
        if (documentButton != null) documentButton.onClick.AddListener(OnDocumentTapped);
        if (inputField     != null) inputField.onDeselect.AddListener(OnInputFieldDeselected);
    }

    void OnDisable()
    {
        if (cameraButton   != null) cameraButton.onClick.RemoveListener(OnCameraTapped);
        if (galleryButton  != null) galleryButton.onClick.RemoveListener(OnGalleryTapped);
        if (documentButton != null) documentButton.onClick.RemoveListener(OnDocumentTapped);
        if (inputField     != null) inputField.onDeselect.RemoveListener(OnInputFieldDeselected);

        _insetTween?.Kill();
    }

    void Update()
    {
        if (keyboardPanel == null) return;
        float area = keyboardPanel.EffectiveAreaCanvasPx;
        float y = -sheetHeightCanvasPx + Mathf.Min(sheetHeightCanvasPx, area);
        _rt.anchoredPosition = new Vector2(0, y);
    }

    public void Toggle()
    {
        if (_isAnimating) return;
        if (_isOpen) Close();
        else         Open();
    }

    public void Open()
    {
        if (_isOpen || _isAnimating) return;
        _isOpen = true;
        _openedOverKeyboard = TouchScreenKeyboard.visible;

        // Spec §9.2: icon swap happens BEFORE the open transition starts.
        if (messagesBottomPanel != null) messagesBottomPanel.ShowKeyboardIcon();
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        float sheetHeightScreenPx = CanvasPxToScreenPx(sheetHeightCanvasPx);

        if (_openedOverKeyboard)
        {
            // Case A: keyboard is visible. Park the inset immediately so the area
            // stays "up" while we dismiss the OS keyboard. Sheet's Update tracks
            // the area to y=0 within one frame — visually a panel swap.
            if (keyboardPanel != null) keyboardPanel.ExtraBottomInsetPx = sheetHeightScreenPx;

            if (inputField != null)
            {
                // DeactivateInputField will fire onDeselect synchronously — suppress so it
                // doesn't recursively trigger Close().
                _suppressDeselectListener = true;
                inputField.DeactivateInputField();
                StartCoroutine(ClearSuppressNextFrame());
            }
        }
        else
        {
            // Case B: keyboard is down. Tween the inset up so the area rises smoothly
            // and the sheet follows.
            _isAnimating = true;
            _insetTween?.Kill();
            float start = keyboardPanel != null ? keyboardPanel.ExtraBottomInsetPx : 0f;
            _insetTween = DOTween.To(
                () => keyboardPanel.ExtraBottomInsetPx,
                v  => keyboardPanel.ExtraBottomInsetPx = v,
                sheetHeightScreenPx,
                openDuration)
                .From(start)
                .SetEase(Ease.OutCubic)
                .OnComplete(() => { _isAnimating = false; });

            if (inputField != null)
            {
                // Visual selection without raising the OS keyboard (see spec §9.1).
                _suppressDeselectListener = true;
                EventSystem.current.SetSelectedGameObject(inputField.gameObject);
                StartCoroutine(ClearSuppressNextFrame());
            }
        }
    }

    private float CanvasPxToScreenPx(float canvasPx)
    {
        if (_canvas == null) return canvasPx;
        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay) return canvasPx * _canvas.scaleFactor;
        float screenH = Screen.height;
        float canvasH = ((RectTransform)_canvas.transform).rect.height;
        return canvasH > 0f ? canvasPx * (screenH / canvasH) : canvasPx;
    }

    public void Close()
    {
        if (!_isOpen || _isAnimating) return;
        _isOpen = false;
        Debug.Log("[AttachSheet] Close (stub)");
        // Body filled in Task 8.
    }

    private void OnInputFieldDeselected(string _)
    {
        // Open() in Case A deactivates the input field intentionally, which fires onDeselect.
        // The suppression flag prevents that synthetic deselect from recursively closing the sheet.
        if (_suppressDeselectListener) return;
        if (_isOpen) Close();
    }

    // Used by Open() in Case A to swallow the immediate onDeselect that follows DeactivateInputField.
    private System.Collections.IEnumerator ClearSuppressNextFrame()
    {
        yield return null;
        _suppressDeselectListener = false;
    }

    private void OnCameraTapped()   { /* Filled in Task 9. */ }
    private void OnGalleryTapped()  { /* Filled in Task 9. */ }
    private void OnDocumentTapped() { /* Filled in Task 9. */ }
}
