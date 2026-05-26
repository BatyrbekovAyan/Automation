using System;
using System.Runtime.InteropServices;
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

    // Native iOS hook — hides/shows the OS keyboard window directly, no animation.
    // Implemented in Assets/Plugins/iOS/AttachSheetKeyboardHider.mm
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void ASKeyboardHider_SetHidden(bool hidden);
#endif

    private static void SetOsKeyboardHidden(bool hidden)
    {
#if UNITY_IOS && !UNITY_EDITOR
        ASKeyboardHider_SetHidden(hidden);
#endif
        // Editor / non-iOS builds: no-op. AttachSheet falls back to existing
        // shouldHideSoftKeyboard + DeactivateInputField behavior in editor.
    }

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
        // sure the input bar isn't stuck at a non-zero inset and the keyboard
        // window isn't left hidden.
        if (keyboardPanel != null) keyboardPanel.ExtraBottomInsetPx = 0f;
        if (inputField != null) inputField.shouldHideSoftKeyboard = false;
        SetOsKeyboardHidden(false);
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

        // Hide the OS keyboard window NOW (no animation, no dismissal).
        // The input field stays focused; the keyboard is "still up" logically
        // but its rendering is hidden. Sheet appears in its place.
        SetOsKeyboardHidden(true);

        if (keyboardPanel != null)
        {
            _insetTween?.Kill();

            if (!wasKeyboardVisible)
            {
                // Cold start (no keyboard): raise the area via inset so the sheet
                // has a place to appear. Mirrors a keyboard rising.
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
            // If kb was visible, no inset tween needed — KeyboardAwarePanel
            // already sees the OS keyboard's area via TouchScreenKeyboard.area,
            // and our native plugin hid only the rendering, not the logical state.
        }

        // Ensure the input field is selected so the caret blinks.
        if (inputField != null)
        {
            inputField.shouldHideSoftKeyboard = true; // belt-and-suspenders for cold start
            if (!wasKeyboardVisible)
            {
                _suppressDeselectListener = true;
                inputField.ActivateInputField();
                EventSystem.current.SetSelectedGameObject(inputField.gameObject);
                StartCoroutine(ClearSuppressNextFrame());
            }
            // If kb was visible, field is already selected — leave it alone.
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

        // Unhide the keyboard window. If it was hidden during Open (Case A),
        // it instantly reappears at its existing position — keyboard is back.
        SetOsKeyboardHidden(false);

        if (inputField != null)
        {
            inputField.shouldHideSoftKeyboard = false;
            // Re-activate so a fresh keyboard is created if one wasn't already up
            // (Case B cold-close path). For Case A this is a no-op refresh.
            _suppressDeselectListener = true;
            inputField.ActivateInputField();
            StartCoroutine(ClearSuppressNextFrame());
        }

        // Tween inset down for Case B; for Case A inset was never raised, so this
        // is a no-op (0 → 0).
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

        if (_isOpen)
        {
            _isOpen = false;
            if (messagesBottomPanel != null) messagesBottomPanel.ShowPlusIcon();
            // Safety: ensure the keyboard window isn't left hidden.
            SetOsKeyboardHidden(false);
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
