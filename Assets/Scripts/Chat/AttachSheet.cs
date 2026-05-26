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
            if (keyboardPanel == null) return;
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
        // KeyboardAwarePanel.ConvertToCanvasSpace subtracts Screen.safeArea.y from the raw
        // screen-px keyboard height before dividing by scale. We're going the other way —
        // add it back so the sheet's screen-space inset accounts for the home-bar gap.
        float safeBottom = Screen.safeArea.y;
        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return canvasPx * _canvas.scaleFactor + safeBottom;
        float screenH = Screen.height;
        float canvasH = ((RectTransform)_canvas.transform).rect.height;
        return canvasH > 0f ? canvasPx * (screenH / canvasH) + safeBottom : canvasPx;
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (_openedOverKeyboard)
        {
            // Case A close: bring the OS keyboard back, drop the extra inset (no visual
            // impact because OS keyboard now provides the area height). ActivateInputField
            // may fire onSelect → no recursion risk, but suppress just in case the platform
            // also fires onDeselect on the previously selected target.
            if (keyboardPanel != null) keyboardPanel.ExtraBottomInsetPx = 0f;
            if (inputField != null)
            {
                _suppressDeselectListener = true;
                inputField.ActivateInputField();
                StartCoroutine(ClearSuppressNextFrame());
            }

            // Case A is instant — no slide tween. Spec §9.2 says "swap after the close
            // transition completes"; for instant Case A that's immediately.
            if (messagesBottomPanel != null) messagesBottomPanel.ShowPlusIcon();
            gameObject.SetActive(false);
            return;
        }

        // Case B close: tween the inset down, sheet slides with the area.
        if (keyboardPanel == null)
        {
            if (messagesBottomPanel != null) messagesBottomPanel.ShowPlusIcon();
            gameObject.SetActive(false);
            return;
        }
        _isAnimating = true;
        _insetTween?.Kill();
        float start = keyboardPanel != null ? keyboardPanel.ExtraBottomInsetPx : 0f;
        _insetTween = DOTween.To(
            () => keyboardPanel.ExtraBottomInsetPx,
            v  => keyboardPanel.ExtraBottomInsetPx = v,
            0f,
            closeDuration)
            .From(start)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                _isAnimating = false;
                if (inputField != null)
                {
                    _suppressDeselectListener = true;
                    inputField.DeactivateInputField();
                    StartCoroutine(ClearSuppressNextFrame());
                }
                // Spec §9.2: swap AFTER the close tween completes.
                if (messagesBottomPanel != null) messagesBottomPanel.ShowPlusIcon();
                gameObject.SetActive(false);
            });
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

    private void OnCameraTapped()
    {
        if (NativeGallery.IsMediaPickerBusy()) return;
        Close();
        InvokeAfterClose(() =>
            NativeGallery.TakePicture(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                EmitPick(AttachmentKind.Photo, path);
            }, maxSize: 2048));
    }

    private void OnGalleryTapped()
    {
        if (NativeGallery.IsMediaPickerBusy()) return;
        Close();
        InvokeAfterClose(() =>
            NativeGallery.GetMixedMediaFromGallery(path =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    EmitPick(AttachmentTypeUtil.GalleryKindFromPath(path), path);
                },
                NativeGallery.MediaType.Image | NativeGallery.MediaType.Video,
                "Select a photo or video"));
    }

    private void OnDocumentTapped()
    {
        Close();
        InvokeAfterClose(() =>
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                EmitPick(AttachmentKind.Document, path);
            }));
    }

    private void EmitPick(AttachmentKind kind, string path)
    {
        long size = 0;
        try { if (System.IO.File.Exists(path)) size = new System.IO.FileInfo(path).Length; }
        catch { size = 0; }

        var pick = new AttachmentPick
        {
            Kind          = kind,
            Path          = path,
            FileName      = System.IO.Path.GetFileName(path),
            MimeType      = AttachmentTypeUtil.MimeFromExtension(path),
            FileSizeBytes = size
        };
        OnPicked?.Invoke(pick);
    }

    private void InvokeAfterClose(System.Action action)
    {
        // Case A close calls gameObject.SetActive(false) before this runs.
        // Case B's tween OnComplete also deactivates the sheet, which would kill
        // a coroutine hosted on `this` mid-wait. Host on messagesBottomPanel
        // (always active while the chat screen is visible) so the action survives.
        MonoBehaviour host = messagesBottomPanel != null ? (MonoBehaviour)messagesBottomPanel : this;
        host.StartCoroutine(InvokeAfterCloseRoutine(action));
    }

    private System.Collections.IEnumerator InvokeAfterCloseRoutine(System.Action action)
    {
        // Wait until any close tween is done (or just one frame for Case A).
        while (_isAnimating) yield return null;
        yield return null;
        action?.Invoke();
    }
}
