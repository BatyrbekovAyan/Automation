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
    private float         _lastKeyboardHeightCanvasPx;

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

        // Safety: if the sheet is disabled mid-animation (e.g. screen change), make
        // sure the input bar isn't stuck at a non-zero inset.
        if (keyboardPanel != null) keyboardPanel.ExtraBottomInsetPx = 0f;
        if (inputField != null) inputField.shouldHideSoftKeyboard = false;
    }

    void Update()
    {
        // Cache the OS keyboard's canvas-space height while the sheet is closed.
        // We use this on the next Open() so the sheet's height matches the keyboard
        // that was last visible — making the AttachSheet feel like the same panel
        // the keyboard occupies, not a different-sized panel that slides in.
        if (!_isOpen && keyboardPanel != null && TouchScreenKeyboard.visible)
        {
            float h = keyboardPanel.EffectiveAreaCanvasPx;
            if (h > 0f) _lastKeyboardHeightCanvasPx = h;
        }
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

        // Pick a sheet height that matches the keyboard:
        //   Case A: read the current keyboard's height directly
        //   Case B (cold): use the last-cached keyboard height, or fall back to the
        //     inspector value if we've never seen the keyboard this session
        float effectiveHeightCanvas;
        if (_openedOverKeyboard && keyboardPanel != null && keyboardPanel.EffectiveAreaCanvasPx > 0f)
            effectiveHeightCanvas = keyboardPanel.EffectiveAreaCanvasPx;
        else if (_lastKeyboardHeightCanvasPx > 0f)
            effectiveHeightCanvas = _lastKeyboardHeightCanvasPx;
        else
            effectiveHeightCanvas = sheetHeightCanvasPx;

        // Resize the sheet so its height matches the keyboard panel — this is the
        // key change that makes the sheet feel like it occupies the same panel.
        _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, effectiveHeightCanvas);

        float sheetHeightScreenPx = CanvasPxToScreenPx(effectiveHeightCanvas);

        if (messagesBottomPanel != null) messagesBottomPanel.ShowKeyboardIcon();
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        _insetTween?.Kill();
        _sheetTween?.Kill();

        if (_openedOverKeyboard)
        {
            // CASE A — instant content swap. Sheet snaps to the keyboard's exact
            // position. ExtraBottomInsetPx is set immediately so the input bar
            // doesn't drop when the OS keyboard slides away. No tween — the OS
            // keyboard's own slide-down animation visually "covers" the swap.
            _rt.anchoredPosition = new Vector2(0f, 0f);
            if (keyboardPanel != null) keyboardPanel.ExtraBottomInsetPx = sheetHeightScreenPx;
        }
        else
        {
            // CASE B — cold start. No keyboard panel exists yet, so slide up like
            // a keyboard would. The input bar rises in parallel via tweened inset.
            _isAnimating = true;
            _rt.anchoredPosition = new Vector2(0f, -effectiveHeightCanvas);

            if (keyboardPanel != null)
            {
                _insetTween = DOTween.To(
                    () => keyboardPanel.ExtraBottomInsetPx,
                    v  => keyboardPanel.ExtraBottomInsetPx = v,
                    sheetHeightScreenPx,
                    openDuration)
                    .From(0f)
                    .SetEase(Ease.OutCubic);
            }

            _sheetTween = _rt.DOAnchorPosY(0f, openDuration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() => { _isAnimating = false; });
        }

        // Dismiss OS keyboard if it was up, then suppress + reselect so the caret
        // stays blinking without a new keyboard appearing. shouldHideSoftKeyboard
        // is set FIRST so that ActivateInputField below doesn't create a keyboard.
        if (inputField != null)
        {
            if (_openedOverKeyboard)
            {
                _suppressDeselectListener = true;
                inputField.DeactivateInputField();
            }
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

        _insetTween?.Kill();
        _sheetTween?.Kill();

        if (_openedOverKeyboard)
        {
            // CASE A close — instant content swap back. Sheet hides immediately,
            // OS keyboard is restored. The OS keyboard's slide-up animation visually
            // covers the swap. ExtraBottomInsetPx is cleared so the OS keyboard's
            // own area drives KeyboardAwarePanel from here.
            if (inputField != null) inputField.shouldHideSoftKeyboard = false;
            if (keyboardPanel != null) keyboardPanel.ExtraBottomInsetPx = 0f;

            if (inputField != null)
            {
                _suppressDeselectListener = true;
                inputField.ActivateInputField();
                StartCoroutine(ClearSuppressNextFrame());
            }

            if (messagesBottomPanel != null) messagesBottomPanel.ShowPlusIcon();
            gameObject.SetActive(false);
            return;
        }

        // CASE B close — cold path. Slide the sheet down and tween the inset back
        // to 0 so the input bar lowers smoothly.
        _isAnimating = true;

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

        _sheetTween = _rt.DOAnchorPosY(-_rt.sizeDelta.y, closeDuration)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                _isAnimating = false;

                if (inputField != null)
                {
                    inputField.shouldHideSoftKeyboard = false;
                    _suppressDeselectListener = true;
                    inputField.DeactivateInputField();
                    StartCoroutine(ClearSuppressNextFrame());
                }

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
