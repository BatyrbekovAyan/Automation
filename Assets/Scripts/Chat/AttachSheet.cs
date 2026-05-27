using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class AttachSheet : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Sheet height in canvas pixels at the 1080×2400 reference resolution.")]
    [SerializeField] private float sheetHeightCanvasPx = 700f;

    [Header("References")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private GameObject backdrop;
    [SerializeField] private Button backdropButton;
    [SerializeField] private Button cameraButton;
    [SerializeField] private Button galleryButton;
    [SerializeField] private Button documentButton;

    [Header("Tween Timings")]
    [SerializeField] private float openDuration  = 0.30f;
    [SerializeField] private float closeDuration = 0.25f;

    public event Action<AttachmentPick> OnPicked;

    private RectTransform _rt;
    private bool          _isOpen;
    private Tween         _slideTween;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();

        OnPicked += pick =>
            Debug.Log($"[AttachSheet] OnPicked: kind={pick.Kind} file={pick.FileName} " +
                      $"size={pick.FileSizeBytes} mime={pick.MimeType} path={pick.Path}");
    }

    void OnEnable()
    {
        if (cameraButton   != null) cameraButton.onClick.AddListener(OnCameraTapped);
        if (galleryButton  != null) galleryButton.onClick.AddListener(OnGalleryTapped);
        if (documentButton != null) documentButton.onClick.AddListener(OnDocumentTapped);
        if (backdropButton != null) backdropButton.onClick.AddListener(Close);
    }

    void OnDisable()
    {
        if (cameraButton   != null) cameraButton.onClick.RemoveListener(OnCameraTapped);
        if (galleryButton  != null) galleryButton.onClick.RemoveListener(OnGalleryTapped);
        if (documentButton != null) documentButton.onClick.RemoveListener(OnDocumentTapped);
        if (backdropButton != null) backdropButton.onClick.RemoveListener(Close);

        _slideTween?.Kill();
        if (backdrop != null) backdrop.SetActive(false);
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

        // Decouple from input field: always dismiss the keyboard. iOS animates
        // the slide-down naturally; KeyboardAwarePanel's rawKb tracking drops
        // the input bar to the base by itself.
        if (inputField != null) inputField.DeactivateInputField();

        gameObject.SetActive(true);
        if (backdrop != null) backdrop.SetActive(true);

        // Sheet slides up from below the canvas. Width is held by the
        // pre-existing anchors (built by AttachSheetBuilder).
        _rt.sizeDelta        = new Vector2(_rt.sizeDelta.x, sheetHeightCanvasPx);
        _rt.anchoredPosition = new Vector2(0f, -sheetHeightCanvasPx);

        _slideTween?.Kill();
        _slideTween = DOTween.To(
                () => _rt.anchoredPosition.y,
                v  => _rt.anchoredPosition = new Vector2(0f, v),
                0f,
                openDuration)
            .SetEase(Ease.OutCubic);
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        _slideTween?.Kill();
        float startY = _rt.anchoredPosition.y;
        _slideTween = DOTween.To(
                () => _rt.anchoredPosition.y,
                v  => _rt.anchoredPosition = new Vector2(0f, v),
                -sheetHeightCanvasPx,
                closeDuration)
            .From(startY)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                gameObject.SetActive(false);
                if (backdrop != null) backdrop.SetActive(false);
            });
    }

    // ── Tile actions ────────────────────────────────────────────────

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

    private void InvokeAfterClose(Action action)
    {
        // Host the coroutine on the input field so the call survives Close()'s
        // SetActive(false) on this GameObject.
        MonoBehaviour host = inputField != null ? (MonoBehaviour)inputField : null;
        if (host != null) host.StartCoroutine(InvokeAfterCloseRoutine(action));
        else              action?.Invoke();
    }

    private IEnumerator InvokeAfterCloseRoutine(Action action)
    {
        // Wait one frame so Close's tween OnComplete (which SetActive(false)s
        // this GameObject) has landed before the native picker is invoked.
        yield return null;
        action?.Invoke();
    }
}
