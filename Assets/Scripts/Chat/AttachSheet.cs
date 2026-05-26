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
    [Tooltip("Sheet height in canvas pixels — sized to feel like a real keyboard area at the 1080×2400 reference resolution.")]
    [SerializeField] private float sheetHeightCanvasPx = 700f;

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
    private Tween         _sheetTween;

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
        _sheetTween?.Kill();
    }

    // Position is tween-driven now. Keeping Update as a no-op so subclasses or
    // future per-frame work has a hook without changing the public surface.
    void Update() { }

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

        // If the OS keyboard was up when the user tapped +, dismiss it.
        // The sheet will visually replace it; ExtraBottomInsetPx keeps the
        // input bar at the same height so nothing snaps down.
        if (_openedOverKeyboard && inputField != null)
        {
            _suppressDeselectListener = true;
            inputField.DeactivateInputField();
            StartCoroutine(ClearSuppressNextFrame());
        }

        _isAnimating = true;
        _insetTween?.Kill();
        _sheetTween?.Kill();

        float sheetHeightScreenPx = CanvasPxToScreenPx(sheetHeightCanvasPx);

        // Tween 1: drive ExtraBottomInsetPx so the input bar rises with the sheet.
        // (KeyboardAwarePanel applies it via Mathf.Max with the live OS keyboard
        // height — so in Case A where keyboard was up at full height, this is a
        // no-op for the input bar position. In Case B, the input bar rises.)
        if (keyboardPanel != null)
        {
            float startInset = keyboardPanel.ExtraBottomInsetPx;
            _insetTween = DOTween.To(
                () => keyboardPanel.ExtraBottomInsetPx,
                v  => keyboardPanel.ExtraBottomInsetPx = v,
                sheetHeightScreenPx,
                openDuration)
                .From(startInset)
                .SetEase(Ease.OutCubic);
        }

        // Tween 2: drive the sheet's own anchoredPosition.y. Independent of
        // EffectiveAreaCanvasPx — this is the source of truth for the sheet's
        // visual position, no cross-component coupling.
        _rt.anchoredPosition = new Vector2(0, _rt.anchoredPosition.y);
        _sheetTween = _rt.DOAnchorPosY(0f, openDuration)
            .SetEase(Ease.OutCubic)
            .OnComplete(() => { _isAnimating = false; });

        // Keep the input field visually selected so the caret blinks, but suppress
        // the OS keyboard so it doesn't reappear over our sheet. Matches WhatsApp.
        if (inputField != null)
        {
            inputField.shouldHideSoftKeyboard = true;
            _suppressDeselectListener = true;
            inputField.ActivateInputField();
            EventSystem.current.SetSelectedGameObject(inputField.gameObject);
            StartCoroutine(ClearSuppressNextFrame());
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

        _isAnimating = true;
        _insetTween?.Kill();
        _sheetTween?.Kill();

        // Tween 1: drive ExtraBottomInsetPx back to 0 — input bar lowers
        // (or stays put if OS keyboard is still up via natural keyboard area).
        if (keyboardPanel != null)
        {
            float startInset = keyboardPanel.ExtraBottomInsetPx;
            _insetTween = DOTween.To(
                () => keyboardPanel.ExtraBottomInsetPx,
                v  => keyboardPanel.ExtraBottomInsetPx = v,
                0f,
                closeDuration)
                .From(startInset)
                .SetEase(Ease.OutCubic);
        }

        // Tween 2: slide the sheet back down to its off-screen position.
        _sheetTween = _rt.DOAnchorPosY(-sheetHeightCanvasPx, closeDuration)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                _isAnimating = false;

                // Reset the keyboard-suppression flag so future activations behave normally.
                if (inputField != null) inputField.shouldHideSoftKeyboard = false;

                if (_openedOverKeyboard && inputField != null)
                {
                    // Came from keyboard-up: bring the OS keyboard back so the user can type.
                    _suppressDeselectListener = true;
                    inputField.ActivateInputField();
                    StartCoroutine(ClearSuppressNextFrame());
                }
                else if (inputField != null)
                {
                    // Came from cold (no keyboard): deselect cleanly so caret stops.
                    _suppressDeselectListener = true;
                    inputField.DeactivateInputField();
                    StartCoroutine(ClearSuppressNextFrame());
                }

                // Spec §9.2: icon swap AFTER the close transition completes.
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
        if (NativeCamera.IsCameraBusy()) return;
        Close();
        InvokeAfterClose(() =>
            NativeCamera.TakePicture(path =>
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
