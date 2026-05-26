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
    private bool          _suppressDeselectListener;
    private Tween         _insetTween;
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

        // Safety: if the sheet is disabled mid-animation (e.g. screen change), make
        // sure the input bar isn't stuck at a non-zero inset.
        if (keyboardPanel != null) keyboardPanel.ExtraBottomInsetPx = 0f;
        if (inputField != null) inputField.shouldHideSoftKeyboard = false;
    }

    void Update()
    {
        if (keyboardPanel == null) return;

        float areaCanvas = keyboardPanel.EffectiveAreaCanvasPx;

        // Cache the OS keyboard's canvas-space height while sheet is closed —
        // we'll use this as the sheet height when the user opens cold (no kb up).
        if (!_isOpen && TouchScreenKeyboard.visible && areaCanvas > 0f)
        {
            _lastKeyboardHeightCanvasPx = areaCanvas;
        }

        bool shouldShow = _isOpen && areaCanvas > 0.5f;

        if (shouldShow)
        {
            // Resize and reposition to match the keyboard area exactly.
            _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, areaCanvas);
            _rt.anchoredPosition = new Vector2(0f, 0f);
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }
        else
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);

            // If the sheet was open but the area collapsed (e.g. input lost focus),
            // reset our state so the icon and flags match reality.
            if (_isOpen && areaCanvas <= 0.5f)
            {
                _isOpen = false;
                if (messagesBottomPanel != null) messagesBottomPanel.ShowPlusIcon();
                if (inputField != null) inputField.shouldHideSoftKeyboard = false;
            }
        }
    }

    public void Toggle()
    {
        if (_isOpen) Close();
        else         Open();
    }

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        if (messagesBottomPanel != null) messagesBottomPanel.ShowKeyboardIcon();

        bool wasKeyboardVisible = TouchScreenKeyboard.visible;
        float currentAreaCanvas = keyboardPanel != null ? keyboardPanel.EffectiveAreaCanvasPx : 0f;

        // Drive the keyboard area:
        //   Case A — OS keyboard is currently up: snap the inset to the current
        //     area height so it stays maintained as we dismiss the OS keyboard.
        //   Case B — OS keyboard is down: tween the inset up to mirror the rise.
        if (keyboardPanel != null)
        {
            _insetTween?.Kill();

            if (wasKeyboardVisible && currentAreaCanvas > 0f)
            {
                keyboardPanel.ExtraBottomInsetPx = CanvasPxToScreenPx(currentAreaCanvas);
            }
            else
            {
                float targetCanvas = _lastKeyboardHeightCanvasPx > 0f
                    ? _lastKeyboardHeightCanvasPx
                    : sheetHeightCanvasPx;
                _insetTween = DOTween.To(
                    () => keyboardPanel.ExtraBottomInsetPx,
                    v  => keyboardPanel.ExtraBottomInsetPx = v,
                    CanvasPxToScreenPx(targetCanvas),
                    openDuration)
                    .From(0f)
                    .SetEase(Ease.OutCubic);
            }
        }

        // Dismiss the OS keyboard if up, and keep the input field visually
        // selected so the caret blinks. shouldHideSoftKeyboard prevents the
        // re-activation from raising a new keyboard.
        if (inputField != null)
        {
            _suppressDeselectListener = true;
            inputField.shouldHideSoftKeyboard = true;
            inputField.DeactivateInputField();
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

        if (messagesBottomPanel != null) messagesBottomPanel.ShowPlusIcon();

        // Restore normal keyboard behavior and bring the OS keyboard back up.
        if (inputField != null)
        {
            inputField.shouldHideSoftKeyboard = false;
            _suppressDeselectListener = true;
            inputField.ActivateInputField();
            StartCoroutine(ClearSuppressNextFrame());
        }

        // Tween the inset down — KeyboardAwarePanel uses Max(rawKbHeight, inset),
        // so as the OS keyboard rises (its own ~0.25s animation) the inset fades
        // out without the input bar visibly dipping.
        if (keyboardPanel != null)
        {
            _insetTween?.Kill();
            float startInset = keyboardPanel.ExtraBottomInsetPx;
            _insetTween = DOTween.To(
                () => keyboardPanel.ExtraBottomInsetPx,
                v  => keyboardPanel.ExtraBottomInsetPx = v,
                0f,
                closeDuration)
                .From(startInset)
                .SetEase(Ease.OutCubic);
        }
    }

    private void OnInputFieldDeselected(string _)
    {
        if (_suppressDeselectListener) return;

        // The input field lost focus while the sheet was open (likely user tapped
        // outside the input area). Clean up: drop the inset, reset the suppress
        // flag, and let Update() hide the sheet on the next tick.
        if (_isOpen)
        {
            _isOpen = false;
            if (messagesBottomPanel != null) messagesBottomPanel.ShowPlusIcon();
            if (inputField != null) inputField.shouldHideSoftKeyboard = false;
            if (keyboardPanel != null) keyboardPanel.ExtraBottomInsetPx = 0f;
            _insetTween?.Kill();
        }
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
        // Host on messagesBottomPanel (always active while chat is visible) so the
        // coroutine survives Update() deactivating this sheet on the same frame.
        MonoBehaviour host = messagesBottomPanel != null ? (MonoBehaviour)messagesBottomPanel : this;
        host.StartCoroutine(InvokeAfterCloseRoutine(action));
    }

    private System.Collections.IEnumerator InvokeAfterCloseRoutine(System.Action action)
    {
        // Wait one frame for Update() to deactivate the sheet before invoking.
        yield return null;
        action?.Invoke();
    }
}
