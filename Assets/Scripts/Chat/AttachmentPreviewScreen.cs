using System;
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

        if (captionField != null) captionField.text = "";
        if (sendButton   != null) sendButton.interactable = true;
        if (backButton   != null) backButton.interactable = true;

        if (root != null) root.SetActive(true);
        FadeTo(1f, blocksRaycasts: true);
    }

    private void PopulateImagePanel(AttachmentPick pick)
    {
        if (imagePanel == null || imagePreview == null) return;
        imagePanel.SetActive(true);

        _currentPreviewTexture = LoadTextureFromFile(pick.Path);
        imagePreview.texture = _currentPreviewTexture;
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

    private static Texture2D LoadTextureFromFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            // NativeGallery.LoadImageAtPath handles HEIC → JPG conversion on iOS
            // natively. Unity's Texture2D.LoadImage cannot decode HEIC, so iPhone
            // camera shots would silently render as the default 2×2 white texture.
            return NativeGallery.LoadImageAtPath(path);
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
        if (sendButton != null) sendButton.interactable = false;

        string caption = captionField != null ? (captionField.text ?? "").Trim() : "";
        var pick = _currentPick;

        if (ChatManager.Instance != null)
            ChatManager.Instance.StageLocalMedia(pick, caption);
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
}
