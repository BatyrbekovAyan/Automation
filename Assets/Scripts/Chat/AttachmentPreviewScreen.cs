using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AttachmentPreviewScreen : MonoBehaviour
{
    [Serializable]
    public struct MimeIconEntry
    {
        public string mimePrefix;
        public Sprite sprite;
    }

    [Header("References — wired by AttachmentPreviewScreenBuilder")]
    [SerializeField] private AttachSheet attachSheet;
    [SerializeField] private GameObject  root;
    [SerializeField] private CanvasGroup rootCanvasGroup;
    [SerializeField] private GameObject  imagePanel;
    [SerializeField] private GameObject  videoPanel;
    [SerializeField] private GameObject  documentPanel;
    [SerializeField] private RectTransform topBarRect;
    [SerializeField] private RectTransform bottomBarRect;
    [SerializeField] private RectTransform contentAreaRect;
    [SerializeField] private RawImage    imagePreview;
    [SerializeField] private RawImage    videoPreview;
    [SerializeField] private GameObject  videoPlayOverlay;
    [SerializeField] private GameObject  videoDurationBadge;
    [SerializeField] private TextMeshProUGUI videoDurationLabel;
    [SerializeField] private TextMeshProUGUI documentFileName;
    [SerializeField] private TextMeshProUGUI documentFileSize;
    [SerializeField] private Image       documentIcon;
    [SerializeField] private DeferredDismissInputField captionField;
    [SerializeField] private Button      sendButton;
    [SerializeField] private Button      backButton;

    [Header("MIME → icon mapping (first prefix match wins)")]
    [SerializeField] private List<MimeIconEntry> mimeIcons = new List<MimeIconEntry>();
    [SerializeField] private Sprite documentFallbackIcon;

    [Header("Tween")]
    [SerializeField] private float fadeDuration = 0.18f;

    private AttachmentPick _currentPick;
    private Texture2D      _currentPreviewTexture;
    private Tween          _fadeTween;
    private AspectRatioFitter _imagePreviewFitter;
    private AspectRatioFitter _videoPreviewFitter;
    private TextMeshProUGUI _sizeErrorLabel;
    private Tween           _sizeErrorTween;

    // Pathological-pick ceiling only: reject absurdly large videos before we bother
    // converting. The real ~16 MB Wappi cap is enforced post-conversion in
    // ChatManager.PostMediaMessageRoutine, since conversion shrinks the file.
    private const long MaxVideoPickBytes = 1024L * 1024 * 1024;

    // Dark placeholder shown in the preview area while the thumbnail/image decodes,
    // so the just-opened screen has no white flash before the media appears.
    private static readonly Color LoadingPlaceholderColor = new Color(0.12f, 0.12f, 0.12f, 1f);

    void Awake()
    {
        if (root != null) root.SetActive(false);
        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.alpha          = 0f;
            rootCanvasGroup.interactable   = false;
            rootCanvasGroup.blocksRaycasts = false;
        }

        if (imagePreview != null) _imagePreviewFitter = imagePreview.GetComponent<AspectRatioFitter>();
        if (videoPreview != null) _videoPreviewFitter = videoPreview.GetComponent<AspectRatioFitter>();

        ApplySafeArea();
    }

    // This component must live on a permanently-active GameObject (the script
    // holder) as built by AttachmentPreviewScreenBuilder. If the holder is ever
    // SetActive(false) then re-enabled, onClick listeners will be added a second
    // time, causing double-fire. The builder enforces this; do not change it.
    void OnEnable()
    {
        if (attachSheet != null) attachSheet.OnPicked += Show;
        if (sendButton  != null) sendButton.onClick.AddListener(OnSendTapped);
        if (backButton  != null) backButton.onClick.AddListener(OnBackTapped);
    }

    void OnDisable()
    {
        if (attachSheet != null) attachSheet.OnPicked -= Show;
        if (sendButton  != null) sendButton.onClick.RemoveListener(OnSendTapped);
        if (backButton  != null) backButton.onClick.RemoveListener(OnBackTapped);

        _fadeTween?.Kill();
        _sizeErrorTween?.Kill();
        ReleasePreviewTexture();
        _currentPick = null;
    }

    public void Show(AttachmentPick pick)
    {
        if (pick == null) return;
        _currentPick = pick;

        ReleasePreviewTexture();
        if (imagePanel    != null) imagePanel.SetActive(false);
        if (videoPanel    != null) videoPanel.SetActive(false);
        if (documentPanel != null) documentPanel.SetActive(false);

        if (captionField != null) captionField.text = "";
        if (sendButton   != null) sendButton.interactable = true;
        if (backButton   != null) backButton.interactable = true;

        // Bring the correct panel up EMPTY and present the screen immediately, so the
        // chat behind it never flashes between the picker dismissing and the preview
        // appearing. The expensive native decode (video thumbnail / HEIC image — up to
        // a few seconds main-thread) is deferred one frame so it runs with the screen
        // already on top instead of blocking its activation.
        switch (pick.Kind)
        {
            case AttachmentKind.Photo:
            case AttachmentKind.GalleryImage:
                if (imagePanel != null) imagePanel.SetActive(true);
                ShowPreviewPlaceholder(imagePreview, _imagePreviewFitter);
                break;
            case AttachmentKind.GalleryVideo:
                if (videoPanel != null) videoPanel.SetActive(true);
                ShowPreviewPlaceholder(videoPreview, _videoPreviewFitter);
                if (videoPlayOverlay   != null) videoPlayOverlay.SetActive(false);
                if (videoDurationBadge != null) videoDurationBadge.SetActive(false);
                break;
            case AttachmentKind.Document:
                if (documentPanel != null) documentPanel.SetActive(true);
                break;
        }

        if (root != null) root.SetActive(true);
        FadeTo(1f, blocksRaycasts: true);

        StartCoroutine(PopulateDeferred(pick));
    }

    // Lets the just-activated screen render one frame before we block the main thread
    // on native decode, so the preview is already covering the chat. Bails if the user
    // backed out or picked something else during the wait.
    private IEnumerator PopulateDeferred(AttachmentPick pick)
    {
        yield return null;
        if (_currentPick != pick) yield break;

        switch (pick.Kind)
        {
            case AttachmentKind.Photo:
            case AttachmentKind.GalleryImage:
                PopulateImagePanel(pick);
                break;
            case AttachmentKind.GalleryVideo:
                PopulateVideoPanel(pick);
                break;
            case AttachmentKind.Document:
                PopulateDocumentPanel(pick);
                break;
        }
    }

    private static void ShowPreviewPlaceholder(RawImage preview, AspectRatioFitter fitter)
    {
        if (preview == null) return;
        preview.texture = null;
        preview.color   = LoadingPlaceholderColor;
        ApplyAspectRatio(fitter, null);
    }

    private void PopulateImagePanel(AttachmentPick pick)
    {
        if (imagePanel == null || imagePreview == null) return;
        imagePanel.SetActive(true);

        _currentPreviewTexture = LoadTextureFromFile(pick.Path);
        imagePreview.texture = _currentPreviewTexture;
        if (_currentPreviewTexture != null) imagePreview.color = Color.white;
        ApplyAspectRatio(_imagePreviewFitter, _currentPreviewTexture);
    }

    private void PopulateVideoPanel(AttachmentPick pick)
    {
        if (videoPanel == null || videoPreview == null) return;
        videoPanel.SetActive(true);

        if (videoDurationLabel != null) videoDurationLabel.text = "";

        Texture2D thumb = null;
        try { thumb = NativeGallery.GetVideoThumbnail(pick.Path); }
        catch (Exception ex) { Debug.LogWarning($"[AttachmentPreviewScreen] thumb extract failed: {ex.Message}"); }

        _currentPreviewTexture = thumb;
        videoPreview.texture = thumb;
        if (thumb != null) videoPreview.color = Color.white;
        ApplyAspectRatio(_videoPreviewFitter, thumb);

        int durationSec = 0;
        try
        {
            var props = NativeGallery.GetVideoProperties(pick.Path);
            durationSec = (int)(props.duration / 1000);
        }
        catch { durationSec = 0; }

        if (videoPlayOverlay != null) videoPlayOverlay.SetActive(true);
        if (videoDurationBadge != null) videoDurationBadge.SetActive(durationSec > 0);
        if (videoDurationLabel != null && durationSec > 0)
            videoDurationLabel.text = $"{durationSec / 60:D1}:{durationSec % 60:D2}";
    }

    private void PopulateDocumentPanel(AttachmentPick pick)
    {
        if (documentPanel == null) return;
        documentPanel.SetActive(true);

        if (documentFileName != null) documentFileName.text = pick.FileName ?? "";

        string sizeText = AttachmentDisplayFormat.HumanReadableBytes(pick.FileSizeBytes);
        string mimeText = AttachmentDisplayFormat.ShortMime(pick.MimeType);
        if (documentFileSize != null)
            documentFileSize.text = string.IsNullOrEmpty(mimeText) ? sizeText : $"{sizeText} · {mimeText}";

        if (documentIcon != null) documentIcon.sprite = SpriteForMime(pick.MimeType);
    }

    private Sprite SpriteForMime(string mime)
    {
        if (string.IsNullOrEmpty(mime)) return documentFallbackIcon;
        foreach (var entry in mimeIcons)
        {
            if (string.IsNullOrEmpty(entry.mimePrefix)) continue;
            if (mime.StartsWith(entry.mimePrefix, StringComparison.OrdinalIgnoreCase))
                return entry.sprite != null ? entry.sprite : documentFallbackIcon;
        }
        return documentFallbackIcon;
    }

    private static void ApplyAspectRatio(AspectRatioFitter fitter, Texture2D tex)
    {
        if (fitter == null) return;
        if (tex == null || tex.width <= 0 || tex.height <= 0)
        {
            fitter.aspectRatio = 1f;
            return;
        }
        fitter.aspectRatio = (float)tex.width / tex.height;
    }

    private void ApplySafeArea()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float topSafePx    = Mathf.Max(0f, (Screen.height - Screen.safeArea.yMax) / scale);
        float bottomSafePx = Mathf.Max(0f, Screen.safeArea.y / scale);

        if (topBarRect != null)
        {
            var pos = topBarRect.anchoredPosition;
            topBarRect.anchoredPosition = new Vector2(pos.x, -topSafePx);
        }
        if (bottomBarRect != null)
        {
            var pos = bottomBarRect.anchoredPosition;
            bottomBarRect.anchoredPosition = new Vector2(pos.x, bottomSafePx);
        }
        if (contentAreaRect != null)
        {
            var maxOff = contentAreaRect.offsetMax;
            var minOff = contentAreaRect.offsetMin;
            contentAreaRect.offsetMax = new Vector2(maxOff.x, maxOff.y - topSafePx);
            contentAreaRect.offsetMin = new Vector2(minOff.x, minOff.y + bottomSafePx);
        }
    }

    private static Texture2D LoadTextureFromFile(string path)
    {
        try
        {
            // Guard inside the try so a TOCTOU race (file deleted between this
            // check and NativeGallery's own File.Exists at line 733) is caught by
            // the FileNotFoundException handler below instead of escaping uncaught.
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;

            // NativeGallery.LoadImageAtPath handles HEIC → JPG conversion on iOS
            // natively. Unity's Texture2D.LoadImage cannot decode HEIC, so iPhone
            // camera shots would silently render as the default 2×2 white texture.
            //
            // maxSize: 1024 keeps the iOS hardware decode fast (full-resolution
            // HEIC decode is 1-3 seconds main-thread). 1024px is plenty for both
            // the preview area and the chat bubble.
            //
            // markTextureNonReadable: false — we hand this texture to
            // ChatManager.StageLocalMedia which calls EncodeToJPG on it to
            // populate the bubble's cache. EncodeToJPG requires readable.
            return NativeGallery.LoadImageAtPath(path,
                                                 maxSize: 1024,
                                                 markTextureNonReadable: false,
                                                 generateMipmaps: false);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AttachmentPreviewScreen] LoadTextureFromFile failed: {ex.Message}");
            return null;
        }
    }

    private void OnSendTapped()
    {
        if (_currentPick == null) return;

        // Reject only absurdly large videos here; normal large clips are shrunk by
        // on-device conversion before upload (see PostMediaMessageRoutine).
        if (_currentPick.Kind == AttachmentKind.GalleryVideo &&
            _currentPick.FileSizeBytes > MaxVideoPickBytes)
        {
            ShowSizeError("This video is too large to process.");
            if (sendButton != null) sendButton.interactable = true;   // let the user go Back and re-pick
            return;                                                   // do NOT stage, do NOT close
        }

        if (sendButton != null) sendButton.interactable = false;

        string caption = captionField != null ? (captionField.text ?? "").Trim() : "";
        var pick = _currentPick;
        var preloadedImage = _currentPreviewTexture;   // may be null for video / document

        if (ChatManager.Instance != null)
            ChatManager.Instance.StageLocalMedia(pick, caption, preloadedImage);
        else
            Debug.LogWarning("[AttachmentPreviewScreen] ChatManager.Instance is null; cannot stage.");

        if (captionField != null && captionField.isFocused) captionField.DeactivateInputField();
        Close();
    }

    private void OnBackTapped()
    {
        if (backButton != null) backButton.interactable = false;
        if (captionField != null && captionField.isFocused) captionField.DeactivateInputField();
        Close();
    }

    private void Close()
    {
        FadeTo(0f, blocksRaycasts: false, onComplete: () =>
        {
            if (root != null) root.SetActive(false);
            ReleasePreviewTexture();
            _currentPick = null;
        });
    }

    private void FadeTo(float targetAlpha, bool blocksRaycasts, Action onComplete = null)
    {
        if (rootCanvasGroup == null)
        {
            onComplete?.Invoke();
            return;
        }

        rootCanvasGroup.interactable   = targetAlpha > 0f;
        rootCanvasGroup.blocksRaycasts = blocksRaycasts;

        _fadeTween?.Kill();
        _fadeTween = DOTween.To(
                () => rootCanvasGroup.alpha,
                v  => rootCanvasGroup.alpha = v,
                targetAlpha,
                fadeDuration)
            .SetEase(Ease.OutQuad)
            .OnComplete(() => onComplete?.Invoke());
    }

    private void ReleasePreviewTexture()
    {
        if (_currentPreviewTexture != null)
        {
            UnityEngine.Object.Destroy(_currentPreviewTexture);
            _currentPreviewTexture = null;
        }
        if (imagePreview != null) imagePreview.texture = null;
        if (videoPreview != null) videoPreview.texture = null;
    }

    /// <summary>
    /// Shows a transient error line on the preview (e.g. "video too large").
    /// The label is created lazily in code so this needs no builder change and
    /// no serialized reference. Auto-fades after a few seconds.
    /// </summary>
    private void ShowSizeError(string message)
    {
        Debug.LogWarning($"[AttachmentPreviewScreen] {message}");
        EnsureSizeErrorLabel();
        if (_sizeErrorLabel == null) return;

        _sizeErrorLabel.text = message;
        _sizeErrorLabel.gameObject.SetActive(true);
        _sizeErrorLabel.alpha = 1f;

        _sizeErrorTween?.Kill();
        _sizeErrorTween = DOTween.Sequence()
            .AppendInterval(2.5f)
            .Append(DOTween.To(() => _sizeErrorLabel.alpha, v => _sizeErrorLabel.alpha = v, 0f, 0.3f))
            .OnComplete(() => { if (_sizeErrorLabel != null) _sizeErrorLabel.gameObject.SetActive(false); });
    }

    private void EnsureSizeErrorLabel()
    {
        if (_sizeErrorLabel != null) return;
        if (bottomBarRect == null) return;   // built hierarchy missing; skip gracefully

        var go = new GameObject("SizeErrorLabel", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(bottomBarRect, worldPositionStays: false);
        // Stretch horizontally, sit just above the bottom bar, in the thumb zone.
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(16f, 8f);
        rt.offsetMax = new Vector2(-16f, 44f);

        _sizeErrorLabel = go.AddComponent<TextMeshProUGUI>();
        _sizeErrorLabel.fontSize          = 14f;
        _sizeErrorLabel.color             = new Color(1f, 0.42f, 0.42f, 1f);  // soft red
        _sizeErrorLabel.alignment         = TextAlignmentOptions.Center;
        _sizeErrorLabel.raycastTarget     = false;
        _sizeErrorLabel.textWrappingMode  = TextWrappingModes.Normal;
        go.SetActive(false);
    }
}
