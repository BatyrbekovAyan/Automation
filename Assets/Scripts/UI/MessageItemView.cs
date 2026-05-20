using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Nobi.UiRoundedCorners; 
using WebP; 

public class MessageItemView : MonoBehaviour
{
    [Header("Core UI")]
    public TextMeshProUGUI messageText;
    public TextMeshProUGUI senderNameText;
    public Image messageImage;
    public Image bubbleBackground; 
    public GameObject bubbleTail; 
    public GameObject outline; 
    public GameObject timeBackground; 

    [Header("Media Controls")]
    public GameObject playOverlay; 
    public GameObject audioPanel;
    public TextMeshProUGUI audioDurationText;
    public Button audioPlayButton;

    [Header("Icons")] 
    public Sprite playIcon; 
    public Sprite stopIcon; 
    public Image audioButtonIcon;
    
    [Header("Status UI")]
    public Button downloadButton;      
    public GameObject expiredPlaceholder; 
    public GameObject loadingSpinner; 
    public Sprite stickerPlaceholder;
    public Sprite downloadArrowIcon;
    
    public TextMeshProUGUI timeText;

    [Header("Document UI")]
    public GameObject documentPanel;
    public Image documentIcon;
    public TextMeshProUGUI documentNameText;
    public TextMeshProUGUI documentInfoText;
    
    [Header("Link Preview UI")]
    public GameObject linkPreviewCard;
    public TextMeshProUGUI linkPreviewTitle;
    public TextMeshProUGUI linkPreviewDescription;
    public TextMeshProUGUI linkPreviewDomain;
    public Image linkPreviewImage;
    private string activeScrapedUrl = ""; // To remember what to open!
    
    [Header("Settings")]
    public float fixedWidth = 464f;
    [Tooltip("How much of the screen should the text bubble take up before wrapping? (0.7 = 70%)")]
    public float maxBubbleWidthPercentage = 0.7f; 
    public Color incomingColor = Color.white;
    public Color outgoingColor = new Color(0.8f, 1f, 0.8f);
    private static readonly Color downloadButtonFillColor = new Color32(0xF1, 0xF1, 0xF1, 0xFF);

    // Light-gray on incoming (white bubble), white on outgoing (green bubble) so the
    // placeholder reads as a card layered on top of the bubble in both directions.
    private Color DownloadFillColor =>
        (currentVm != null && !currentVm.isIncoming) ? Color.white : downloadButtonFillColor;

    [Header("Layout Settings")]
    public float downloadButtonHeight = 284f; 
    
    public Slider audioSlider;
    public bool isDragging;
    
    // === Bubble container ===
    private const float MaxBubbleWidth        = 810f;   // 0.75 × 1080 canvas — text + caption ceiling
    private const float MinBubbleWidth        =  90f;   // Set on text-bubble LayoutElement.minWidth so very short messages (e.g. "ok") still fit an inline timestamp

    // === Bubble reset padding (LRTB) — used by ResetBubbleLayoutToDefault and synced into both prefabs ===
    private const int   BubblePadLeft         = 8;
    private const int   BubblePadRight        = 8;
    private const int   BubblePadTop          = 8;
    private const int   BubblePadBottom       = 12;

    // === Image / Video ===
    private const float ImageLandscapeWidth   = 810f;   // 0.75 × canvas
    private const float ImagePortraitWidth    = 648f;   // 0.60 × canvas
    private const float ImageSquareWidth      = 700f;   // 0.65 × canvas
    private const float ImageMaxHeight        = 1080f;  // tall-portrait clamp
    private const float MinAspectRatio        = 0.56f;  // 9:16
    private const float MaxAspectRatio        = 1.78f;  // 16:9
    private const float AspectLandscapeThreshold = 1.1f; // > → landscape
    private const float AspectPortraitThreshold  = 0.9f; // < → portrait

    // === Voice / Audio ===
    private const float VoiceWidth            = 720f;   // 0.67 × canvas
    private const float VoiceHeight           = 150f;
    private const float AudioFileWidth        = 760f;   // 0.70 × canvas
    private const float AudioFileHeight       = 190f;

    // === Sticker (no bubble bg) ===
    private const float StickerWidth          = 432f;   // 0.40 × canvas
    private const float StickerHeight         = 432f;

    // === Document ===
    private const float DocumentWidth         = 760f;   // 0.70 × canvas
    private const float DocumentMinWidth      = 480f;
    private const float DocumentHeight        = 200f;

    // === Caption + link preview ===
    private const float CaptionInset          = 32f;    // captionWidth = mediaWidth - inset
    private const float LinkPreviewRatio      = 0.65f;  // × bubbleWidth

    /// <summary>
    /// Resolves the bubble content size for any message type.
    /// For text/caption-only bubbles, returns (MaxBubbleWidth, 0) — height is text-driven.
    /// For media, returns final width and height after aspect/clamp logic.
    /// </summary>
    private Vector2 ResolveContentSize(MessageType type, float aspect)
    {
        switch (type)
        {
            case MessageType.Image:
            case MessageType.Video:
                return ResolveMediaSize(aspect);

            case MessageType.Sticker:
                return new Vector2(StickerWidth, StickerHeight);

            case MessageType.Voice:
                return new Vector2(VoiceWidth, VoiceHeight);

            case MessageType.Audio:
                return new Vector2(AudioFileWidth, AudioFileHeight);

            case MessageType.Document:
                return new Vector2(DocumentWidth, DocumentHeight);

            default: // Chat, Unknown
                return new Vector2(MaxBubbleWidth, 0f);
        }
    }

    private Vector2 ResolveMediaSize(float aspect)
    {
        if (!float.IsFinite(aspect) || aspect <= 0f) aspect = 1f;
        aspect = Mathf.Clamp(aspect, MinAspectRatio, MaxAspectRatio);

        float width, height;

        if (aspect >= AspectLandscapeThreshold)
        {
            width  = ImageLandscapeWidth;
            height = width / aspect;
        }
        else if (aspect <= AspectPortraitThreshold)
        {
            width  = ImagePortraitWidth;
            height = width / aspect;

            if (height > ImageMaxHeight)
            {
                height = ImageMaxHeight;
                width  = height * aspect;
            }
        }
        else
        {
            width  = ImageSquareWidth;
            height = width / aspect;
        }

        return new Vector2(width, height);
    }

    [SerializeField] private MessageViewModel currentVm;

    /// <summary>
    /// Read-only access to the message this bubble is currently bound to.
    /// Used by MessageListView for tail-merging, date-separator placement,
    /// and pagination boundary checks. Never null after the first Bind().
    /// </summary>
    public MessageViewModel BoundVm => currentVm;
    private float defaultFontSize = -1f;
    private bool hideBubble = false;
    private bool isJumboEmoji = false;
    private bool currentShowTail;
    private bool floatingTimeConfigured = false;

    private string _mainMessageOriginalText;
    private AudioSource audioSource;
    private RectTransform rectTransform;
    private TextMeshProUGUI downloadButtonText;
    private Sprite fullScreenSprite;
    private Button retryButton;

#if UNITY_IOS
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ShowQuickLook(string path);
#endif
    
    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        if (downloadButton != null)
        {
            downloadButtonText = downloadButton.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        ConfigureFloatingTime();
    }

    void OnEnable()
    {
        if (AudioController.Instance != null)
        {
            AudioController.Instance.OnAudioStarted += HandleAudioStarted;
            AudioController.Instance.OnAudioStopped += HandleAudioStopped;
            AudioController.Instance.OnAudioProgress += HandleAudioProgress;
        }

        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnMessageStatusChanged += HandleStatusChanged;
            ChatManager.Instance.OnMessageMediaRefreshed += HandleMediaRefreshed;
        }
    }

    void OnDisable()
    {
        if (AudioController.Instance != null)
        {
            AudioController.Instance.OnAudioStarted -= HandleAudioStarted;
            AudioController.Instance.OnAudioStopped -= HandleAudioStopped;
            AudioController.Instance.OnAudioProgress -= HandleAudioProgress;
        }

        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnMessageStatusChanged -= HandleStatusChanged;
            ChatManager.Instance.OnMessageMediaRefreshed -= HandleMediaRefreshed;
        }

        if (retryButton != null)
        {
            retryButton.onClick.RemoveAllListeners();
            retryButton.interactable = false;
        }

        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
    }

    private void SubscribeToEmojiReady()
    {
        // Avoid duplicate subscription
        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
        EmojiPatchService.OnEmojiReady += HandleEmojiReady;
    }

    private void HandleEmojiReady(string spriteName)
    {
        if (messageText == null || string.IsNullOrEmpty(_mainMessageOriginalText)) return;

        var reconverted = UnicodeEmojiConverter.ConvertRealEmojisToSprites(
            _mainMessageOriginalText, out bool stillMissing);

        if (reconverted != messageText.text)
            messageText.text = reconverted;

        if (!stillMissing)
            EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
    }

    public void Bind(MessageViewModel vm, bool showTail = true, bool skipLayoutRebuild = false, bool showSenderName = false)    
    {
        currentVm = vm;
        currentShowTail = showTail; 
        
// --- SENDER NAME LOGIC ---
        if (senderNameText != null)
        {
            senderNameText.gameObject.SetActive(showSenderName);
            if (showSenderName)
            {
                senderNameText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(vm.senderName);
                senderNameText.color = GetSenderColor(vm.senderName);
            }
        }
        
        if (downloadButton != null)
        {
            downloadButton.onClick.RemoveAllListeners();
            downloadButton.onClick.AddListener(() => OnDownloadClicked(vm));

            // --- THE FIX: Clean up object pooling! Ensure recycled buttons restore their arrows ---
            var btnImg = downloadButton.GetComponent<Image>();
            if (btnImg != null)
            {
                btnImg.enabled = true;
                if (downloadArrowIcon != null) btnImg.sprite = downloadArrowIcon;
                btnImg.color = DownloadFillColor;
            }
        }

        if (defaultFontSize < 0) defaultFontSize = messageText.fontSize;
        messageText.fontSize = defaultFontSize; 
        
        messageText.alignment = TextAlignmentOptions.TopLeft; 
        messageText.margin = Vector4.zero; 
        
        hideBubble = false;
        isJumboEmoji = false; 
        
        string safeText = string.IsNullOrEmpty(vm.text) ? "" : vm.text;
        string textToProcess = safeText;

        if (linkPreviewCard != null) linkPreviewCard.SetActive(false);

        if (vm.type == MessageType.Chat)
        {
            System.Text.RegularExpressions.Match rawLinkMatch = System.Text.RegularExpressions.Regex.Match(safeText, @"(https?://[^\s]+)");

            if (rawLinkMatch.Success && LinkScraper.Instance != null)
            {
                activeScrapedUrl = rawLinkMatch.Groups[1].Value;

                // --- THE UPDATE: Use the helper to format the text! ---
                textToProcess = FormatTextWithWrappableLinks(safeText);

                if (linkPreviewCard != null) 
                {
                    Button cardBtn = linkPreviewCard.GetComponent<Button>();
                    if (cardBtn == null) cardBtn = linkPreviewCard.gameObject.AddComponent<Button>();
                    
                    cardBtn.onClick.RemoveAllListeners();
                    cardBtn.onClick.AddListener(() => 
                    {
                        if (ScrollClickBlocker.IsBlocking) return;
                        Application.OpenURL(activeScrapedUrl);
                    });
                }
                
                StartCoroutine(ProcessLinkPreviewSilently(activeScrapedUrl, vm));
            }
            else
            {
                // --- THE UPDATE: Use the helper to format the text! ---
                textToProcess = FormatTextWithWrappableLinks(safeText);
            }
        }
        else
        {
            // --- THE UPDATE: Use the helper to format the text! ---
            textToProcess = FormatTextWithWrappableLinks(safeText);
        }

        _mainMessageOriginalText = textToProcess;
        string processedText = UnicodeEmojiConverter.ConvertRealEmojisToSprites(textToProcess, out bool hasMissingMain);
        processedText ??= "";
        if (hasMissingMain) SubscribeToEmojiReady();
        
        if (linkPreviewCard != null) linkPreviewCard.SetActive(false);

        if (vm.type == MessageType.Chat)
        {
            System.Text.RegularExpressions.Match rawLinkMatch = System.Text.RegularExpressions.Regex.Match(vm.text ?? "", @"(https?://[^\s]+)");
            
            if (rawLinkMatch.Success && LinkScraper.Instance != null)
            {
                activeScrapedUrl = rawLinkMatch.Groups[1].Value;

                if (linkPreviewCard != null) 
                {
                    Button cardBtn = linkPreviewCard.GetComponent<Button>();
                    if (cardBtn == null) cardBtn = linkPreviewCard.gameObject.AddComponent<Button>();
                    
                    cardBtn.onClick.RemoveAllListeners();
                    cardBtn.onClick.AddListener(() => 
                    {
                        if (ScrollClickBlocker.IsBlocking) return;
                        Application.OpenURL(activeScrapedUrl);
                    });
                }
                
                // Start the silent assembler!
                StartCoroutine(ProcessLinkPreviewSilently(activeScrapedUrl, vm));
            }
        }
        
        int spriteCount = System.Text.RegularExpressions.Regex.Matches(processedText, @"<sprite").Count;
        string textOnly = System.Text.RegularExpressions.Regex.Replace(processedText, @"<[^>]*>", "").Replace("\u200B", "").Trim();
        bool isOnlyEmojis = (textOnly.Length == 0 && spriteCount > 0);

        if (isOnlyEmojis && spriteCount <= 3) 
        {
            messageText.alignment = TextAlignmentOptions.Top; 

            if (spriteCount == 1)
            {
                messageText.fontSize = defaultFontSize * 3f; 
                
                // --- THE FIX: Don't hide the bubble if we are displaying a name! ---
                hideBubble = !showSenderName; 
                
                isJumboEmoji = true; 
                messageText.margin = new Vector4(24, 0, 0, 8); 
            }
            else if (spriteCount >= 2 && spriteCount <= 3)
            {
                messageText.fontSize = defaultFontSize * 1.5f; 
                isJumboEmoji = true;

                if (spriteCount == 2)
                {
                    messageText.margin = new Vector4(14, 0, 0, 8);
                }
                else
                {
                    messageText.margin = new Vector4(16, 0, 0, 8);
                }
            }
        }

        messageText.text = processedText;
        messageText.gameObject.SetActive(!string.IsNullOrEmpty(processedText));

        RefreshTimeAndTick();
        UpdateRetryButton(!currentVm.isIncoming && currentVm.deliveryStatus == DeliveryStatus.Failed);

        playOverlay.SetActive(false);
        audioPanel.SetActive(false);
        if (documentPanel) documentPanel.SetActive(false);
        if (loadingSpinner) loadingSpinner.SetActive(false);
        if (expiredPlaceholder) expiredPlaceholder.SetActive(false);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool isLinkExpired = vm.expireTime > 0 && vm.expireTime < now;
        string urlToCheck = (vm.type == MessageType.Video) ? vm.videoUrl : vm.mediaUrl;
        
        now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        isLinkExpired = vm.expireTime > 0 && vm.expireTime < now;
        
if (vm.type == MessageType.Image || vm.type == MessageType.Video)
        {
            float realRatio = vm.aspectRatio > 0 ? vm.aspectRatio : 1.0f;
            float bubbleRatio = Mathf.Clamp(realRatio, MinAspectRatio, MaxAspectRatio);

            // Hide everything initially and let the Smart Routine sequence the UI perfectly
            messageImage.gameObject.SetActive(false);
            downloadButton.gameObject.SetActive(false);

            StartCoroutine(SmartMediaRoutine(vm, bubbleRatio, false));

            var btn = messageImage.GetComponent<Button>();
            if (!btn) btn = messageImage.gameObject.AddComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnVisualClicked(vm));
        }
        else if (vm.type == MessageType.Sticker)
        {
            urlToCheck = vm.mediaUrl;
            bool isMissing = string.IsNullOrEmpty(urlToCheck);
                    
            // --- NEW: Check the cache! ---
            bool isCached = !isMissing && MediaCacheManager.Instance.IsImageCached(urlToCheck);

            // If it's cached, we don't care if the link is expired! Let it through!
            if (isCached || (!isMissing && !isLinkExpired))
            {
                downloadButton.gameObject.SetActive(false);
                messageImage.gameObject.SetActive(true);

                float realRatio = vm.aspectRatio > 0 ? vm.aspectRatio : 1.0f;
                float bubbleRatio = realRatio;

                SetupMaskedLayout(bubbleRatio, realRatio, true);
                        
                DisplayMedia(urlToCheck, true, false, bubbleRatio);

                var btn = messageImage.GetComponent<Button>();
                if (!btn) btn = messageImage.gameObject.AddComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnVisualClicked(vm));
            }
            else
            {
                if (!string.IsNullOrEmpty(vm.thumbnailUrl))
                {
                    messageImage.gameObject.SetActive(true);
                    
                    float realRatio = vm.aspectRatio > 0 ? vm.aspectRatio : 1.0f;
                    float bubbleRatio = realRatio;
                    
                    SetupMaskedLayout(bubbleRatio, realRatio, true);
                    DisplayMedia(vm.thumbnailUrl, true, false, bubbleRatio);
                    
                    var btnLe = downloadButton.GetComponent<LayoutElement>();
                    if (!btnLe) btnLe = downloadButton.gameObject.AddComponent<LayoutElement>();
                    btnLe.ignoreLayout = true;
                }
                else
                {
                    messageImage.gameObject.SetActive(false);
                    SetLayoutToButton(); 
                }

                downloadButton.gameObject.SetActive(false); 
                SetDownloadButtonText(vm.type); 
                
                StartDownload(vm, 0, false); 
            }
        }
        else if (vm.type == MessageType.Audio || vm.type == MessageType.Voice)
        {
            messageImage.gameObject.SetActive(false);
            bool isMissing = string.IsNullOrEmpty(vm.mediaUrl);

            if (!isMissing && !isLinkExpired)
            {
                downloadButton.gameObject.SetActive(false);
                ResetBubbleLayoutToDefault();
                HandleAudioMedia(vm);
            }
            else
            {
                audioPanel.SetActive(false);
                downloadButton.gameObject.SetActive(false); 
                
                SetDownloadButtonText(vm.type); 
                SetLayoutToButton();
                
                StartDownload(vm, 0, false); 
            }
        }
        else if (vm.type == MessageType.Document)
        {
            messageImage.gameObject.SetActive(false);
            audioPanel.SetActive(false);
            playOverlay.SetActive(false);
            // messageText.gameObject.SetActive(false);

            string rawName = string.IsNullOrEmpty(vm.fileName) ? "Document.file" : vm.fileName;
            string decodedName = System.Uri.UnescapeDataString(rawName);

            bool isMissing = string.IsNullOrEmpty(vm.mediaUrl);
            isLinkExpired = vm.expireTime > 0 && vm.expireTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool needsDownload = isMissing || isLinkExpired;

            if (needsDownload)
            {
                if (documentPanel) documentPanel.SetActive(false);
                
                if (downloadButton != null)
                {
                    Image btnImg = downloadButton.GetComponent<Image>();
                    if (btnImg != null && downloadArrowIcon != null) btnImg.sprite = downloadArrowIcon;
                }

                downloadButton.gameObject.SetActive(false); 
                SetDownloadButtonText(vm.type);
                SetLayoutToButton();

                StartDownload(vm, 0, false);
            }
            else
            {
                SetupDocumentView(vm, decodedName);
            }
            ResetBubbleLayoutToDefault();
        }
        else
        {
            messageImage.gameObject.SetActive(false);
            downloadButton.gameObject.SetActive(false);
        }
        
        UpdateBubbleVisuals();
        ApplyDynamicLayout(vm.type);
        
// --- THE TRIGGER FIX ---
        // We must tell the script to run the calculator for Media Captions too!
        // Documents also need it without a caption: the document card otherwise
        // has no explicit preferredWidth and the bubble's ContentSizeFitter lets
        // it grow past the maxBubbleWidthPercentage clamp for long filenames.
        bool hasCaption = !string.IsNullOrEmpty(vm.text);
        bool isDocument = vm.type == MessageType.Document;
        bool isCaptionableMedia = vm.type == MessageType.Chat || vm.type == MessageType.Image || vm.type == MessageType.Video;
        if (isDocument || (hasCaption && isCaptionableMedia))
        {
            AdjustTextBubbleSize();
        }
        
// --- THE HIERARCHY FIX: Force Caption BELOW Media ---
        int currentIndex = 0;

        if (senderNameText != null && senderNameText.gameObject.activeSelf)
            senderNameText.transform.SetSiblingIndex(currentIndex++);

        // 2. The Media File (Grabbing the correct parent container!)
        // Include the download/expired placeholders so the caption lands below them
        // when a media message hasn't finished downloading yet.
        Transform activeMedia = null;
        if (messageImage != null && messageImage.gameObject.activeSelf)
        {
            // We must move the MediaContainer, not the Image itself!
            activeMedia = messageImage.transform.parent.name == "MediaContainer" ? messageImage.transform.parent : messageImage.transform;
        }
        else if (documentPanel != null && documentPanel.gameObject.activeSelf) activeMedia = documentPanel.transform;
        else if (audioPanel != null && audioPanel.activeSelf) activeMedia = audioPanel.transform;
        else if (downloadButton != null && downloadButton.gameObject.activeSelf) activeMedia = downloadButton.transform;
        else if (expiredPlaceholder != null && expiredPlaceholder.activeSelf) activeMedia = expiredPlaceholder.transform;
        else if (linkPreviewCard != null && linkPreviewCard.activeSelf) activeMedia = linkPreviewCard.transform;

        if (activeMedia != null) activeMedia.SetSiblingIndex(currentIndex++);

        // 3. Text or Caption ALWAYS at the very bottom
        if (messageText != null && messageText.gameObject.activeSelf)
        {
            messageText.transform.SetSiblingIndex(currentIndex++);
            // Align the caption's visible left edge with plain text's (~16px from the bubble
            // border) regardless of which media branch set the bubble's left padding.
            var bubbleLayout = bubbleBackground != null ? bubbleBackground.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
            float marginX = bubbleLayout != null ? Mathf.Max(24f - bubbleLayout.padding.left, 0f) : 8f;
            messageText.margin = new Vector4(marginX, 0, marginX, 0);
        }
        
        if (!skipLayoutRebuild)
        {
            StartCoroutine(ForceRebuildRoutine());
        }    
    }

    void ApplyDynamicLayout(MessageType type)
    {
        if (bubbleBackground == null) return;
        var layout = bubbleBackground.GetComponent<VerticalLayoutGroup>();
        if (layout == null) return;

        layout.childForceExpandWidth = true;

        if (senderNameText != null && senderNameText.gameObject.activeSelf)
        {
            var le = senderNameText.GetComponent<LayoutElement>();
            if (le == null) le = senderNameText.gameObject.AddComponent<LayoutElement>();
            le.minHeight = -1; 
        }
        
        if (timeText != null)
        {
            timeText.margin = new Vector4(0, 0, 0, 0);
        }

        if (type == MessageType.Document)
        {
            bool isDownloadActive = downloadButton != null && downloadButton.gameObject.activeSelf;
            bool isExpiredActive = expiredPlaceholder != null && expiredPlaceholder.activeSelf;
            bool useCardLayout = isDownloadActive || isExpiredActive;
            
            // --- ADDED: Check if the document has a caption! ---
            bool hasCaption = messageText != null && messageText.gameObject.activeSelf && !string.IsNullOrEmpty(messageText.text);

            if (useCardLayout)
            {
                layout.spacing = 8;
                layout.padding = new RectOffset(6, 6, 6, 6);

                if (timeText != null)
                {
                    PositionFloatingTime(20f, 16f);
                }
            }
            else
            {
                // --- THE SPACING FIX: 12px if there is a caption, 8px if there isn't! ---
                layout.spacing = hasCaption ? 12 : 8;

                layout.padding = new RectOffset(6, 6, 6, 12);

                // Without a caption, match the 16px bottom inset used by image/video so
                // the time sits at the same offset from the bubble border. With a caption
                // the time rides the caption's last line, so the 10px inset is fine.
                if (timeText != null) PositionFloatingTime(20f, hasCaption ? 10f : 16f);
            }
        }
        else if (type == MessageType.Chat)
        {
            layout.spacing = 4; 
            
            bool hasLinkCard = linkPreviewCard != null && linkPreviewCard.activeSelf;
            
            if (hasLinkCard)
            {
                bool hasSenderName = senderNameText != null && senderNameText.gameObject.activeSelf;
                
                string rawCaption = currentVm?.text ?? "";
                rawCaption = System.Text.RegularExpressions.Regex.Replace(rawCaption, @"https?://\S+", "");
                rawCaption = System.Text.RegularExpressions.Regex.Replace(rawCaption, @"[​-‏ - ﻿]", "");
                rawCaption = rawCaption.Trim();
                bool vmHasCaption = !string.IsNullOrWhiteSpace(rawCaption);
                bool messageTextActive = messageText != null && messageText.gameObject.activeSelf;
                bool hasCaption = messageTextActive && vmHasCaption;

                layout.spacing = hasCaption ? 12 : 8;

                layout.padding = new RectOffset(6, 6, hasSenderName ? 14 : 6, hasCaption ? 12 : 0);

                if (hasCaption)
                {
                    messageText.margin = new Vector4(18, 0, 18, 0);
                }

                if (hasSenderName)
                {
                    // If layout spacing increased to 12px, we offset the sender name's bottom
                    // margin by -4px so the name doesn't float too far away from the card!
                    senderNameText.margin = new Vector4(18, 0, 0, hasCaption ? -4 : 0);
                }

                if (timeText != null)
                {
                    PositionFloatingTime(20f, 10f);
                }
            }
            else if (isJumboEmoji)
            {
                if (hideBubble)
                {
                    layout.padding = new RectOffset(-24, -24, 12, 10);
                    layout.spacing = 16;

                    if (timeText != null)
                    {
                        PositionFloatingTime(-6f, layout.padding.bottom + 4f);
                    }

                    timeText.color = Color.white;
                }
                else
                {
                    layout.padding = new RectOffset(8, 8, 12, 10);
                    layout.spacing = 16;

                    if (timeText != null)
                    {
                        PositionFloatingTime(20f, layout.padding.bottom + 4f);
                    }
                }
            }
            else
            {
                layout.padding = new RectOffset(16, 16, 14, 18);

                if (timeText != null)
                {
                    timeText.overflowMode = TextOverflowModes.Overflow;
                    PositionFloatingTime(20f, layout.padding.bottom - 8f);
                }
            }
        }
        else if (type == MessageType.Audio || type == MessageType.Voice)
        {
            bool isDownloadActive = downloadButton != null && downloadButton.gameObject.activeSelf;
            bool isExpiredActive = expiredPlaceholder != null && expiredPlaceholder.activeSelf;
            bool useCardLayout = isDownloadActive || isExpiredActive;

            // Only size the audio panel when it's the active visual (not the download/expired card).
            if (!useCardLayout && audioPanel != null && audioPanel.activeSelf)
            {
                var audioLe = audioPanel.GetComponent<LayoutElement>();
                if (audioLe == null) audioLe = audioPanel.AddComponent<LayoutElement>();

                Vector2 size = ResolveContentSize(type, 1f);
                audioLe.preferredWidth  = size.x;
                audioLe.preferredHeight = size.y;
            }

            bool hasSenderName = senderNameText != null && senderNameText.gameObject.activeSelf;

            if (useCardLayout)
            {
                layout.spacing = 8;
                layout.padding = new RectOffset(6, 6, 6, 6);

                if (timeText != null)
                {
                    PositionFloatingTime(20f, 16f);
                }

                if (senderNameText != null && hasSenderName)
                {
                    senderNameText.margin = new Vector4(16, 0, 0, 0);
                }
            }
            else
            {
                layout.spacing = -34;
                layout.padding = new RectOffset(16, 14, 12, 12);

                if (timeText != null)
                {
                    PositionFloatingTime(20f, layout.padding.bottom - 2f);
                }

                if (senderNameText != null && hasSenderName)
                {
                    senderNameText.margin = new Vector4(8, 0, 0, 34);
                }
            }
        }
        else if  (type == MessageType.Image || type == MessageType.Video || type == MessageType.Sticker)
        {
            bool isDownloadActive = downloadButton != null && downloadButton.gameObject.activeSelf;
            bool isExpiredActive = expiredPlaceholder != null && expiredPlaceholder.activeSelf;
            
            bool isLoadingNoThumb = loadingSpinner != null && loadingSpinner.activeSelf && string.IsNullOrEmpty(currentVm?.thumbnailUrl);
            bool useCardLayout = isDownloadActive || isExpiredActive || isLoadingNoThumb; 
            bool hasCaption = messageText != null && messageText.gameObject.activeSelf && !string.IsNullOrEmpty(messageText.text);
            
            if (!useCardLayout && senderNameText != null && senderNameText.gameObject.activeSelf)
            {
                var le = senderNameText.GetComponent<LayoutElement>();
                le.minHeight = hasCaption ? senderNameText.preferredHeight : senderNameText.preferredHeight + 56f;
            }
            
            if (type == MessageType.Sticker)
            {
                layout.spacing = useCardLayout ? 8 : 0;
            }
            else
            {
                if (useCardLayout) layout.spacing = 8;
                else if (hasCaption) layout.spacing = 12; // Push the caption safely below the image!
                else layout.spacing = -42;            
            }
            
            bool hasSenderName = senderNameText != null && senderNameText.gameObject.activeSelf;
            
            if (useCardLayout)
            {
                layout.padding = new RectOffset(6, 6, 6, 6);
                if (timeText != null) PositionFloatingTime(20f, 16f);
            }
            else
            {
                // Stickers get the wider 54px bottom padding (matches the document card visual);
                // images and videos keep the tight 6px so the media fills the bubble, but
                // get 12px when there's a caption so the text doesn't crowd the bubble edge.
                int bottomPad = (type == MessageType.Sticker) ? 54 : (hasCaption ? 12 : 6);
                layout.padding = new RectOffset(6, 6, hasSenderName ? 14 : 6, bottomPad);
                // Image/video without a caption float the time over the media at 16; with
                // a caption (or for stickers) the time drops to 10 to sit alongside the text.
                float timeBottomInset = (type == MessageType.Sticker || hasCaption) ? 10f : 16f;
                if (timeText != null) PositionFloatingTime(20f, timeBottomInset);
            }

            if (senderNameText != null && hasSenderName)
            {
                senderNameText.margin = new Vector4(18, 0, 0, 0);
            }
        }        
        else
        {
            layout.spacing = 5;
            layout.padding = new RectOffset(6, 6, 6, 16);

            if (timeText != null)
            {
                PositionFloatingTime(20f, 10f);
            }
        }
        
        if (timeText != null)
        {
            bool isDownloadActive = downloadButton != null && downloadButton.gameObject.activeSelf;
            bool isExpiredActive = expiredPlaceholder != null && expiredPlaceholder.activeSelf;
            bool hasCaption = messageText != null && messageText.gameObject.activeSelf && !string.IsNullOrEmpty(messageText.text);

            if (isDownloadActive || isExpiredActive)
            {
                // The download/expired card uses a white background — white time would vanish.
                timeText.color = new Color(0.4f, 0.4f, 0.4f, 1f);
                if (timeBackground != null) timeBackground.SetActive(false);
            }
            else if ((type == MessageType.Image || type == MessageType.Video) && !hasCaption)
            {
                // NO CAPTION: Time is white and floats over the image!
                timeText.color = Color.white;
                if (timeBackground != null) timeBackground.SetActive(true);
            }
            else
            {
                // WITH CAPTION (or normal text): Time is gray and sits neatly underneath!
                timeText.color = new Color(0.4f, 0.4f, 0.4f, 1f); 
                if (timeBackground != null) timeBackground.SetActive(false);
            }
        }
    }

    void AdjustTextBubbleSize()
    {
        LayoutElement textLayout = messageText.GetComponent<LayoutElement>();
        if (textLayout == null) textLayout = messageText.gameObject.AddComponent<LayoutElement>();

        LayoutElement timeLayout = null;
        if (timeText != null)
        {
            timeLayout = timeText.GetComponent<LayoutElement>();
            if (timeLayout == null) timeLayout = timeText.gameObject.AddComponent<LayoutElement>();
        }

        // Reserve trailing space in the wrappable text so the floating timeText
        // sits inline. Skipped automatically for jumbo emoji / empty / inactive text.
        ApplyInlineTimeReservation(messageText);

        if (isJumboEmoji)
        {
            messageText.textWrappingMode = TextWrappingModes.NoWrap;
            textLayout.ignoreLayout = false;
            
            if ((currentVm.type == MessageType.Image || currentVm.type == MessageType.Video) && !currentVm.isSticker)
            {
                if (messageText.gameObject.activeSelf && !string.IsNullOrEmpty(messageText.text))
                {
                    // Force the caption to wrap tightly under the Image!
                    float maxCaptionWidth = fixedWidth + 12f; 
                    textLayout.preferredWidth = Mathf.Min(textLayout.preferredWidth, maxCaptionWidth);
                }
            }
            
            textLayout.preferredWidth = -1; 
            
            if (timeLayout != null) timeLayout.ignoreLayout = false; 
            return; 
        }
        
        ScrollRect scroll = GetComponentInParent<ScrollRect>();
        float containerWidth = scroll != null ? scroll.GetComponent<RectTransform>().rect.width : Screen.width;

        if (containerWidth <= 50f) 
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null) containerWidth = canvas.GetComponent<RectTransform>().rect.width;
            if (containerWidth <= 50f) containerWidth = 1080f; 
        }
        
        float paddingX = 40f; 

        if (bubbleBackground != null)
        {
            var lg = bubbleBackground.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (lg != null) 
            {
                paddingX = lg.padding.left + lg.padding.right;
            }
        }
        
        if (currentVm != null && currentVm.type == MessageType.Document)
        {
            paddingX = 24f; 
        }

        float maxAllowedTextWidth = (containerWidth * maxBubbleWidthPercentage) - paddingX;

        if (currentVm != null && currentVm.type == MessageType.Document)
        {
            bool isDownloaded = documentPanel != null && documentPanel.activeInHierarchy;
            bool isDownloadActive = downloadButton != null && downloadButton.gameObject.activeSelf;
            float mediaWidth = 0f;
            bool hasCaption = messageText != null && messageText.gameObject.activeSelf && !string.IsNullOrEmpty(messageText.text);

            if (isDownloaded && documentNameText != null)
            {
                LayoutElement docLayout = documentPanel.GetComponent<LayoutElement>();
                if (!docLayout) docLayout = documentPanel.gameObject.AddComponent<LayoutElement>();

                documentNameText.textWrappingMode = TextWrappingModes.Normal;
                documentNameText.overflowMode = TextOverflowModes.Ellipsis;
                documentNameText.maxVisibleLines = 2;

                float maxTextWidthAllowed = maxAllowedTextWidth - 90f;

                string rawName = string.IsNullOrEmpty(currentVm.fileName) ? "Document.file" : currentVm.fileName;
                string decodedName = UnicodeEmojiConverter.ConvertRealEmojisToSprites(System.Uri.UnescapeDataString(rawName));

                documentNameText.text = SplitLongWord(decodedName, documentNameText, maxTextWidthAllowed);

                float unbrokenNameWidth = documentNameText.GetPreferredValues(documentNameText.text, Mathf.Infinity, Mathf.Infinity).x;

                float nameWidth = Mathf.Min(unbrokenNameWidth, maxTextWidthAllowed);

                float infoWidth = 0f;
                if (documentInfoText != null)
                {
                    documentInfoText.textWrappingMode = TextWrappingModes.NoWrap;
                    infoWidth = documentInfoText.GetPreferredValues(documentInfoText.text, Mathf.Infinity, Mathf.Infinity).x;
                }

                float maxTextWidth = Mathf.Max(nameWidth, infoWidth);
                // Always float the Time on documents so it gets the standard 20px right
                // inset from PositionFloatingTime (with a caption it sits inline on the
                // caption's last line; without one it floats at the card's bottom-right).
                if (timeLayout != null) timeLayout.ignoreLayout = true;

                float finalWidth = maxTextWidth + 132f;
                float minDocWidth = 240f;

                if (finalWidth < minDocWidth) finalWidth = minDocWidth;

                docLayout.preferredWidth = finalWidth;
                mediaWidth = finalWidth;

                LayoutElement nameLe = documentNameText.GetComponent<LayoutElement>();
                if (!nameLe) nameLe = documentNameText.gameObject.AddComponent<LayoutElement>();

                float textWidthInsidePanel = finalWidth - 90f;
                float actualHeight = documentNameText.GetPreferredValues(documentNameText.text, textWidthInsidePanel, Mathf.Infinity).y;
                float singleLineHeight = documentNameText.GetPreferredValues("A", textWidthInsidePanel, Mathf.Infinity).y;

                nameLe.preferredHeight = Mathf.Min(actualHeight, singleLineHeight * 2f);
            }
            else if (isDownloadActive)
            {
                // Standard media-download placeholder width comes from the button's prefab
                // LayoutElement (444 in both incoming/outgoing prefabs).
                var btnLe = downloadButton.GetComponent<LayoutElement>();
                mediaWidth = (btnLe != null && btnLe.preferredWidth > 0f) ? btnLe.preferredWidth : 444f;
            }

            // Clamp caption width to the active media element. Without this, a long
            // caption stretches the bubble — and since the bubble VLG has
            // childForceExpandWidth=true, that stretches the download button or
            // document card right along with it.
            if (hasCaption && mediaWidth > 0f)
            {
                messageText.textWrappingMode = TextWrappingModes.Normal;
                textLayout.ignoreLayout = false;
                textLayout.minWidth = 0;
                textLayout.flexibleWidth = 0;

                Vector2 wrappedSize = messageText.GetPreferredValues(messageText.text, mediaWidth - 16f, Mathf.Infinity);
                textLayout.preferredWidth = Mathf.Min(wrappedSize.x + 21f, mediaWidth);
            }

            return;
        }
        
        messageText.textWrappingMode = TextWrappingModes.Normal;
        textLayout.ignoreLayout = false;
        
        
        Vector2 singleLineSize = messageText.GetPreferredValues(messageText.text, Mathf.Infinity, Mathf.Infinity);

// 1. SAFELY SIZE THE TEXT OR FORCE IT TO ZERO
        if (messageText.gameObject.activeSelf && !string.IsNullOrEmpty(messageText.text))
        {
            float availableWidthForText = maxAllowedTextWidth - 16f; 
            
            bool isMediaCaption = (currentVm.type == MessageType.Image || currentVm.type == MessageType.Video) && !currentVm.isSticker;
            
            if (isMediaCaption)
            {
                // Force the measuring tape to be no wider than the image container!
                availableWidthForText = Mathf.Min(availableWidthForText, fixedWidth - 16f);
            }

            Vector2 wrappedSize = messageText.GetPreferredValues(messageText.text, availableWidthForText, Mathf.Infinity);
            textLayout.preferredWidth = Mathf.Min(wrappedSize.x + 21f, maxAllowedTextWidth); 

            if (isMediaCaption)
            {
                // Clamp the text block so its preferred width physically cannot exceed the image
                textLayout.preferredWidth = Mathf.Min(textLayout.preferredWidth, fixedWidth);
            }
            
            textLayout.minWidth = 0; 
            textLayout.flexibleWidth = 0; // <--- ADD THIS LINE! Forces Unity to NEVER stretch the text!
        }
        else
        {
            // If text is hidden, force its width to 0 so the Bubble instantly shrinks!
            textLayout.preferredWidth = 0; 
            textLayout.minWidth = 0; 
            textLayout.flexibleWidth = 0; // <--- ADD THIS LINE HERE TOO!
        }

// 2. BUILD THE CARD
// 2. BUILD THE CARD
        if (linkPreviewCard != null && linkPreviewCard.activeSelf)
        {
            var previewLe = linkPreviewCard.GetComponent<LayoutElement>();
            if (previewLe == null) previewLe = linkPreviewCard.gameObject.AddComponent<LayoutElement>();

            float targetWidth = containerWidth * 0.55f; // Safe default

            // --- THE NEW FIX: IMAGE-DRIVEN WIDTH ---
            // We ignore the text length completely. The Image Aspect Ratio controls the card size!
            if (linkPreviewImage != null && linkPreviewImage.gameObject.activeSelf && linkPreviewImage.sprite != null)
            {
                float aspect = (float)linkPreviewImage.sprite.texture.width / linkPreviewImage.sprite.texture.height;
                
                // Base the width on a "Square" image taking up 60% of the screen.
                // Landscape images will scale up (and hit the max limit). Portrait images will scale down.
                float calculatedWidth = (containerWidth * 0.6f) * aspect;
                
                float minCardWidth = containerWidth * 0.45f; // Don't let portrait cards get too skinny
                targetWidth = Mathf.Clamp(calculatedWidth, minCardWidth, maxAllowedTextWidth);

                var imgLe = linkPreviewImage.GetComponent<LayoutElement>();
                if (imgLe == null) imgLe = linkPreviewImage.gameObject.AddComponent<LayoutElement>();
                
                imgLe.preferredHeight = targetWidth / aspect;
                imgLe.minWidth = 0; imgLe.preferredWidth = 0; // Prevent stretching
            }
            else
            {
                // If there is NO image, then we fall back to measuring the text (but we clamp it strictly!)
                float titleWidth = 0f;
                if (linkPreviewTitle != null && linkPreviewTitle.gameObject.activeSelf)
                    titleWidth = linkPreviewTitle.GetPreferredValues(linkPreviewTitle.text, Mathf.Infinity, Mathf.Infinity).x + 32f; 

                float domainWidth = 0f;
                if (linkPreviewDomain != null && linkPreviewDomain.gameObject.activeSelf)
                    domainWidth = linkPreviewDomain.GetPreferredValues(linkPreviewDomain.text, Mathf.Infinity, Mathf.Infinity).x + 32f;

                float maxTextWidth = Mathf.Max(titleWidth, domainWidth);
                targetWidth = Mathf.Clamp(maxTextWidth, containerWidth * 0.45f, maxAllowedTextWidth);
            }

            previewLe.preferredWidth = targetWidth;

            // Title Math (Now strictly forced to wrap inside our new Image-based width!)
            if (linkPreviewTitle != null && linkPreviewTitle.gameObject.activeSelf)
            {
                var titleLe = linkPreviewTitle.GetComponent<LayoutElement>();
                if (titleLe == null) titleLe = linkPreviewTitle.gameObject.AddComponent<LayoutElement>();
                
                float singleLineHeight = linkPreviewTitle.GetPreferredValues("A", targetWidth, Mathf.Infinity).y;
                float actualHeight = linkPreviewTitle.GetPreferredValues(linkPreviewTitle.text, targetWidth, Mathf.Infinity).y;
                
                titleLe.preferredHeight = Mathf.Min(actualHeight, singleLineHeight * 3f) + 16f;
                titleLe.minWidth = 0; titleLe.preferredWidth = 0;
            }

            // Description Math
            bool descActive = linkPreviewDescription != null && linkPreviewDescription.gameObject.activeSelf;
            if (descActive)
            {
                var descLe = linkPreviewDescription.GetComponent<LayoutElement>();
                if (descLe == null) descLe = linkPreviewDescription.gameObject.AddComponent<LayoutElement>();

                float singleLineHeight = linkPreviewDescription.GetPreferredValues("A", targetWidth, Mathf.Infinity).y;

                descLe.preferredHeight = singleLineHeight * 2f + 16f;
                descLe.minWidth = 0; descLe.preferredWidth = 0;
            }

            // Domain Math
            if (linkPreviewDomain != null && linkPreviewDomain.gameObject.activeSelf)
            {
                var domainLe = linkPreviewDomain.GetComponent<LayoutElement>();
                if (domainLe == null) domainLe = linkPreviewDomain.gameObject.AddComponent<LayoutElement>();

                domainLe.preferredHeight = linkPreviewDomain.GetPreferredValues("A", targetWidth, Mathf.Infinity).y + 16;
                domainLe.minWidth = 0; domainLe.preferredWidth = 0;

                // Prefab default of -16 collapses the 16px VLG spacing — desired between
                // title and domain, but with a description in between it eats the gap.
                Vector4 domainMargin = linkPreviewDomain.margin;
                domainMargin.y = descActive ? 0f : -16f;
                linkPreviewDomain.margin = domainMargin;
            }
            
            // Text Math (If the user typed a message with the link, don't let their text stretch the card out!)
            if (messageText.gameObject.activeSelf && !string.IsNullOrEmpty(messageText.text))
            {
                 textLayout.preferredWidth = Mathf.Min(textLayout.preferredWidth, targetWidth);
            }
        }
    }
    
    public void FinalizeCustomVisuals()
    {
        if (bubbleBackground != null)
        {
            var mirror = bubbleBackground.GetComponent<MirrorSize>();
            if (mirror != null) mirror.UpdateSize();
        }

        RefreshCorners(messageImage != null ? messageImage.gameObject : null);
        RefreshCorners(bubbleBackground != null ? bubbleBackground.gameObject : null);
        RefreshCorners(outline);
    }
    
    void SetDownloadButtonText(MessageType type)
    {
        if (downloadButtonText == null) return;

        string typeText = type switch
        {
            MessageType.Image => "Image",
            MessageType.Video => "Video",
            MessageType.Sticker => "Sticker",
            MessageType.Audio => "Audio",
            MessageType.Voice => "Voice Note",
            MessageType.Document => "Document", 
            _ => "Media"
        };

        downloadButtonText.text = typeText;
    }

    void SetupMaskedLayout(float bubbleRatio, float imageRealRatio, bool isSticker)
    {
        Transform bubbleParent = messageImage.transform.parent;
        
        if (bubbleParent.name == "MediaContainer")
            bubbleParent = bubbleParent.parent;

        Transform containerTr = bubbleParent.Find("MediaContainer");
        if (containerTr == null)
        {
            GameObject containerGo = new GameObject("MediaContainer", typeof(RectTransform));
            containerTr = containerGo.transform;
            containerTr.SetParent(bubbleParent, false);
            containerTr.SetSiblingIndex(messageImage.transform.GetSiblingIndex());
            messageImage.transform.SetParent(containerTr, false);
        }

        containerTr.gameObject.SetActive(true);

        var bubbleLayout = bubbleParent.GetComponent<VerticalLayoutGroup>();
        if (!bubbleLayout) bubbleLayout = bubbleParent.gameObject.AddComponent<VerticalLayoutGroup>();
        
        int pad = 10;
        bubbleLayout.padding = new RectOffset(pad, pad, pad, pad);
        bubbleLayout.childControlHeight = true;
        bubbleLayout.childControlWidth = true;
        bubbleLayout.childForceExpandHeight = false; 
        
        bubbleLayout.childForceExpandWidth = true; 

        var sizeFitter = bubbleParent.GetComponent<ContentSizeFitter>();
        if (!sizeFitter) sizeFitter = bubbleParent.gameObject.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; 

        var contLayout = containerTr.GetComponent<LayoutElement>();
        if (!contLayout) contLayout = containerTr.gameObject.AddComponent<LayoutElement>();
        contLayout.ignoreLayout = false;

        Vector2 mediaSize = isSticker
            ? new Vector2(StickerWidth, StickerHeight)
            : ResolveMediaSize(bubbleRatio);
        contLayout.preferredWidth  = mediaSize.x;
        contLayout.preferredHeight = mediaSize.y;

        if (containerTr.TryGetComponent<RectMask2D>(out var rMask)) Destroy(rMask);
        if (containerTr.TryGetComponent<Mask>(out var mask)) Destroy(mask);
        if (containerTr.TryGetComponent<Image>(out var maskImg)) Destroy(maskImg);

        if (bubbleParent.TryGetComponent<AspectRatioFitter>(out var pFit)) pFit.enabled = false;
        if (bubbleParent.TryGetComponent<LayoutElement>(out var pLayout)) pLayout.preferredHeight = -1; 

        RectTransform imgRect = messageImage.GetComponent<RectTransform>();
        imgRect.anchorMin = Vector2.zero;
        imgRect.anchorMax = Vector2.one;
        imgRect.sizeDelta = Vector2.zero;
        imgRect.anchoredPosition = Vector2.zero;

        if (messageImage.TryGetComponent<AspectRatioFitter>(out var imgFitter)) imgFitter.enabled = false;

        if (messageImage.TryGetComponent<ImageWithRoundedCorners>(out var oldR)) Destroy(oldR);

        var rounded = messageImage.GetComponent<ImageWithRoundedCorners>();
        if (!rounded) rounded = messageImage.gameObject.AddComponent<ImageWithRoundedCorners>();
        
        if (isSticker)
        {
            rounded.enabled = false;
            messageImage.material = null; 
        }
        else
        {
            rounded.enabled = true;
            rounded.radius = 23f; 
        }
    }

    void SetLayoutToButton()
    {
        Transform currentParent = messageImage.transform.parent;
        Transform bubbleParent = (currentParent.name == "MediaContainer") ? currentParent.parent : currentParent;

        if (bubbleParent.TryGetComponent<VerticalLayoutGroup>(out var bubbleLayout))
        {
            bubbleLayout.padding = new RectOffset(6, 6, 6, 6);
        }

        if (bubbleParent.TryGetComponent<LayoutElement>(out var pLayout)) pLayout.preferredHeight = -1; 
        
        var sizeFitter = bubbleParent.GetComponent<ContentSizeFitter>();
        if (!sizeFitter) sizeFitter = bubbleParent.gameObject.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (currentParent.name == "MediaContainer")
        {
            currentParent.gameObject.SetActive(false);
        }

        var btnLayout = downloadButton.GetComponent<LayoutElement>();
        if (!btnLayout) btnLayout = downloadButton.gameObject.AddComponent<LayoutElement>();
    
        btnLayout.minHeight = downloadButtonHeight;
        btnLayout.preferredHeight = downloadButtonHeight;

        if (currentVm != null)
        {
            btnLayout.minWidth = -1;
            var btnImg = downloadButton.GetComponent<Image>();
            
            if (btnImg != null) btnImg.preserveAspect = false; 
        }
    
        if (messageImage.TryGetComponent<LayoutElement>(out var imgLayout)) imgLayout.ignoreLayout = true;
    }
    
    void ResetBubbleLayoutToDefault()
    {
        Transform currentParent = messageImage.transform.parent;
        Transform bubbleParent = (currentParent.name == "MediaContainer") ? currentParent.parent : currentParent;

        if (bubbleParent.TryGetComponent<VerticalLayoutGroup>(out var bubbleLayout))
        {
            bubbleLayout.padding = new RectOffset(8, 8, 8, 12);
            bubbleLayout.childForceExpandWidth = true; 
        }

        var sizeFitter = bubbleParent.GetComponent<ContentSizeFitter>();
        if (!sizeFitter) sizeFitter = bubbleParent.gameObject.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (bubbleParent.TryGetComponent<LayoutElement>(out var pLayout)) pLayout.preferredHeight = -1; 
    }

    void OnDownloadClicked(MessageViewModel vm)
    {
        if (ScrollClickBlocker.IsBlocking) return;
        
        if (vm.type == MessageType.Image || vm.type == MessageType.Video)
        {
            float realRatio = vm.aspectRatio > 0 ? vm.aspectRatio : 1.0f;
            float bubbleRatio = Mathf.Clamp(realRatio, MinAspectRatio, MaxAspectRatio);
            StartCoroutine(SmartMediaRoutine(vm, bubbleRatio, true));
        }
        else
        {
            StartDownload(vm, 0, true);
        }
    }

void StartDownload(MessageViewModel vm, int attemptNumber, bool isManual)
    {
        bool isAudio = (vm.type == MessageType.Audio || vm.type == MessageType.Voice);
        bool isDoc = (vm.type == MessageType.Document); 

        if (downloadButton) downloadButton.gameObject.SetActive(true);
        if (expiredPlaceholder) expiredPlaceholder.SetActive(false); 

        Image btnImg = downloadButton != null ? downloadButton.GetComponent<Image>() : null;
        if (btnImg != null) btnImg.enabled = false; 
        
        if (downloadButtonText) downloadButtonText.gameObject.SetActive(false); 

        if (loadingSpinner) 
        {
            loadingSpinner.SetActive(true);
            loadingSpinner.transform.SetParent(downloadButton.transform, false);
            var rt = loadingSpinner.GetComponent<RectTransform>();
            if (rt != null) 
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }
            var le = loadingSpinner.GetComponent<LayoutElement>();
            if (!le) le = loadingSpinner.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        ChatManager.Instance.DownloadMediaForMessage(vm.messageId, 
            (source) => 
            {
                if (this == null || !gameObject.activeInHierarchy) return;
                
                if (btnImg != null) btnImg.enabled = true;
                if (downloadButtonText) downloadButtonText.gameObject.SetActive(true);
                
                if (loadingSpinner)
                {
                    loadingSpinner.transform.SetParent(downloadButton.transform.parent, false);
                    loadingSpinner.SetActive(false);
                }

                if (downloadButton) downloadButton.gameObject.SetActive(false);

                vm.expireTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400;

                if (vm.type == MessageType.Video) vm.videoUrl = source;
                else vm.mediaUrl = source;

                UpdateBubbleVisuals();

                if (isAudio)
                {
                    ResetBubbleLayoutToDefault();
                    HandleAudioMedia(vm);
                    
                    ApplyDynamicLayout(vm.type);
                    
                    if (isManual && !string.IsNullOrEmpty(vm.mediaUrl))
                    {
                        AudioController.Instance.ToggleAudio(vm.mediaUrl);
                    }
                    StartCoroutine(ForceRebuildRoutine());
                }
                else if (isDoc) 
                {
                    string rawName = string.IsNullOrEmpty(vm.fileName) ? "Document.file" : vm.fileName;
                    string decodedName = System.Uri.UnescapeDataString(rawName);

                    if (isManual && !string.IsNullOrEmpty(vm.mediaUrl))
                    {
                        StartCoroutine(DownloadAndOpenDocumentLocal(vm, decodedName, false));
                    }
                    else
                    {
                        ResetBubbleLayoutToDefault();
                        SetupDocumentView(vm, decodedName);
                        ApplyDynamicLayout(vm.type);
                        StartCoroutine(ForceRebuildRoutine());
                    }
                }
                else
                {
                    float realRatio = vm.aspectRatio > 0 ? vm.aspectRatio : 1.0f;
                    float bubbleRatio = vm.isSticker ? realRatio : Mathf.Clamp(realRatio, MinAspectRatio, MaxAspectRatio);

                    messageImage.gameObject.SetActive(true);
                    SetupMaskedLayout(bubbleRatio, realRatio, vm.isSticker); 
                    
                    ApplyDynamicLayout(vm.type);
                    
                    if (vm.type == MessageType.Video)
                    {
                        playOverlay.SetActive(true);
                        DisplayMedia(vm.mediaUrl, false, true, bubbleRatio);
                    }
                    else
                    {
                        DisplayMedia(source, vm.type == MessageType.Sticker, true, bubbleRatio);
                    }

                    var btn = messageImage.GetComponent<Button>();
                    if (!btn) btn = messageImage.gameObject.AddComponent<Button>();
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => OnVisualClicked(vm));
                }
            },
            () => 
            {
                if (this == null || !gameObject.activeInHierarchy) return;
                
                if (!isManual && attemptNumber < 1) 
                {
                    StartCoroutine(WaitAndRetry(vm, attemptNumber + 1));
                }
                else
                {
                    if (btnImg != null) btnImg.enabled = true;
                    if (downloadButtonText) downloadButtonText.gameObject.SetActive(true);
                    
                    if (loadingSpinner)
                    {
                        loadingSpinner.transform.SetParent(downloadButton.transform.parent, false);
                        loadingSpinner.SetActive(false);
                    }
                    
                    HandleFinalFailure(isManual, isAudio); 
                }
            }
        );
    }
    
// --- THE NEW SMART MEDIA FLOW (Images & Videos Only) ---

IEnumerator SmartMediaRoutine(MessageViewModel vm, float bubbleRatio, bool isManual)
    {
        // --- 1. THE VIDEO FAST-TRACK ---
        // Videos do not need to download the giant .mp4 just to show a chat bubble!
        // We just show the thumbnail and the Play button. The MP4 downloads when they click Play.
        if (vm.type == MessageType.Video)
        {
            ShowSmartThumbnail(vm, bubbleRatio, false);
            yield break;
        }

        // --- 2. IMAGE LOGIC ---
        string targetUrl = vm.mediaUrl;

        // INSTANT CACHE INTERCEPT
        if (!string.IsNullOrEmpty(targetUrl) && MediaCacheManager.Instance.IsImageCached(targetUrl))
        {
            ShowSmartThumbnail(vm, bubbleRatio, false); 
            
            string filePath = MediaCacheManager.Instance.GetFilePathFromUrl(targetUrl);
            byte[] bytes = System.IO.File.ReadAllBytes(filePath);
            Texture2D tex = new Texture2D(2, 2);
            
            if (tex.LoadImage(bytes)) 
            {
                ApplyTextureAspectFill(tex, false, bubbleRatio);
            }
            else 
            {
                // THE FIX: If the cached file is corrupt, trigger the fallback!
                HandleFinalFailure(isManual, false); 
            }
            
            yield break; 
        }

        // NOT CACHED: PROCEED WITH NETWORK LOGIC
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool isLinkExpired = vm.expireTime > 0 && vm.expireTime < now;
        
        bool hasValidUrl = !string.IsNullOrEmpty(targetUrl) && !isLinkExpired && targetUrl.StartsWith("http");

        if (hasValidUrl)
        {
            ShowSmartThumbnail(vm, bubbleRatio, true); 
            yield return StartCoroutine(DownloadSmartHDBytes(targetUrl, vm, bubbleRatio, isManual, 0));
        }
        else
        {
            // Link is dead and we don't have it cached. Ask the API!
            ShowSmartLoadingCard(vm);
            
            bool apiSuccess = false;
            string fetchedUrl = "";
            bool apiDone = false;

            ChatManager.Instance.DownloadMediaForMessage(vm.messageId, 
                (url) => { apiSuccess = true; fetchedUrl = url; apiDone = true; },
                () => { apiSuccess = false; apiDone = true; }
            );

            while (!apiDone) yield return null;

            if (apiSuccess)
            {
                vm.expireTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400;
                vm.mediaUrl = fetchedUrl;

                ShowSmartThumbnail(vm, bubbleRatio, true);
                yield return StartCoroutine(DownloadSmartHDBytes(fetchedUrl, vm, bubbleRatio, isManual, 0));
            }
            else HandleFinalFailure(isManual, false);
        }
    }

    IEnumerator DownloadSmartHDBytes(string url, MessageViewModel vm, float bubbleRatio, bool isManual, int attempt)
    {
        if (url.StartsWith("base64://"))
        {
            LoadBase64Image(url.Substring(9), false, bubbleRatio);
            if (loadingSpinner) loadingSpinner.SetActive(false);
            yield break;
        }

        Texture2D cachedTex = MediaCacheManager.Instance.LoadImageFromCache(url);
        if (cachedTex != null)
        {
            if (loadingSpinner) loadingSpinner.SetActive(false);
            ApplyTextureAspectFill(cachedTex, false, bubbleRatio);
            yield break; 
        }

        using UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            if (loadingSpinner) loadingSpinner.SetActive(false);

            byte[] imageBytes = www.downloadHandler.data;
            Texture2D tex = new Texture2D(2, 2);
            
            if (tex.LoadImage(imageBytes)) 
            {
                MediaCacheManager.Instance.SaveImageToCache(url, imageBytes);
                ApplyTextureAspectFill(tex, false, bubbleRatio);
            }
            else
            {
                // --- THE FIX: The bytes downloaded, but they are corrupt/not an image! ---
                // Show the download button so the user can try again!
                HandleFinalFailure(isManual, false);
            }
        }
        else
        {
            if (attempt < 1) 
            {
                yield return new WaitForSeconds(3f);
                yield return StartCoroutine(DownloadSmartHDBytes(url, vm, bubbleRatio, isManual, attempt + 1));
            }
            else
            {
                if (loadingSpinner) loadingSpinner.SetActive(false);
                HandleFinalFailure(isManual, false);
            }
        }
    }
    
void ShowSmartThumbnail(MessageViewModel vm, float bubbleRatio, bool showSpinner = true) 
    {
        downloadButton.gameObject.SetActive(false);
        messageImage.gameObject.SetActive(true);

        float realRatio = vm.aspectRatio > 0 ? vm.aspectRatio : 1.0f;
        SetupMaskedLayout(bubbleRatio, realRatio, false);

        bool imageLoaded = false;

        if (!string.IsNullOrEmpty(vm.thumbnailUrl))
        {
            if (vm.thumbnailUrl.StartsWith("thumb://"))
            {
                Texture2D cachedTex = MediaCacheManager.Instance.LoadImageFromCache(vm.thumbnailUrl);
                if (cachedTex != null) 
                {
                    ApplyTextureAspectFill(cachedTex, false, bubbleRatio);
                    imageLoaded = true;
                }
            }
            else if (vm.thumbnailUrl.StartsWith("base64://"))
            {
                LoadBase64Image(vm.thumbnailUrl.Substring(9), false, bubbleRatio);
                imageLoaded = true;
            }
        }
        
        // If we didn't find a thumbnail in the cache or Base64, show the dark blank card
        if (!imageLoaded)
        {
            messageImage.sprite = null;
            messageImage.color = new Color(0.15f, 0.15f, 0.15f, 1f); 
        }

        // --- THE FIX: This is no longer skipped! ---
        if (vm.type == MessageType.Video) playOverlay.SetActive(true);

        if (loadingSpinner)
        {
            loadingSpinner.SetActive(showSpinner); 
            
            loadingSpinner.transform.SetParent(messageImage.transform, false);
            loadingSpinner.transform.SetAsLastSibling();
            
            var rt = loadingSpinner.GetComponent<RectTransform>();
            if (rt)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }

            var le = loadingSpinner.GetComponent<LayoutElement>();
            if (!le) le = loadingSpinner.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            
            if (loadingSpinner.TryGetComponent<Image>(out var img)) img.color = Color.clear;
        }

        UpdateBubbleVisuals();
        ApplyDynamicLayout(vm.type);
        StartCoroutine(ForceRebuildRoutine());
    }
    void ShowSmartLoadingCard(MessageViewModel vm)
    {
        messageImage.gameObject.SetActive(false);
        
        if (downloadButton)
        {
            downloadButtonText.text = vm.type.ToString();
            downloadButton.gameObject.SetActive(true);
        }

        SetLayoutToButton();

        if (loadingSpinner)
        {
            loadingSpinner.SetActive(true);
            loadingSpinner.transform.SetParent(downloadButton.transform, false);
            loadingSpinner.transform.SetAsLastSibling();
            
            var rt = loadingSpinner.GetComponent<RectTransform>();
            if (rt)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }

            var le = loadingSpinner.GetComponent<LayoutElement>();
            if (!le) le = loadingSpinner.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        UpdateBubbleVisuals();
        ApplyDynamicLayout(vm.type);
        StartCoroutine(ForceRebuildRoutine());
    }
    
    IEnumerator DownloadAndOpenDocumentLocal(MessageViewModel vm, string decodedName, bool openImmediately)
    {
        Transform originalSpinnerParent = null;
        string localPath = GetLocalDocumentPath(vm.messageId, decodedName);

        if (!System.IO.File.Exists(localPath))
        {
            if (loadingSpinner) 
            {
                originalSpinnerParent = loadingSpinner.transform.parent;
                loadingSpinner.SetActive(true);
                loadingSpinner.transform.SetParent(downloadButton.transform, false);
                var rt = loadingSpinner.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = Vector2.zero;
                
                var le = loadingSpinner.GetComponent<LayoutElement>();
                if (!le) le = loadingSpinner.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
            }

            Image btnImg = downloadButton.GetComponent<Image>();
            Color originalBtnColor = Color.white;
            if (btnImg != null)
            {
                originalBtnColor = btnImg.color;
                btnImg.color = Color.clear;
            }
            if (downloadButtonText) downloadButtonText.text = "Downloading...";

            using (UnityWebRequest www = new UnityWebRequest(vm.mediaUrl, UnityWebRequest.kHttpVerbGET))
            {
                www.downloadHandler = new DownloadHandlerFile(localPath);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("Failed to download document: " + www.error);
                    if (loadingSpinner)
                    {
                        loadingSpinner.transform.SetParent(originalSpinnerParent, false);
                        loadingSpinner.SetActive(false);
                    }
                    if (btnImg != null) btnImg.color = originalBtnColor;
                    if (downloadButtonText) downloadButtonText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(decodedName);
                    yield break;
                }
            }

            if (loadingSpinner) 
            {
                loadingSpinner.transform.SetParent(originalSpinnerParent, false);
                loadingSpinner.SetActive(false);
            }
            if (btnImg != null) btnImg.color = originalBtnColor;
        }

        SetupDocumentView(vm, decodedName);
        ResetBubbleLayoutToDefault();
        UpdateBubbleVisuals();
        ApplyDynamicLayout(vm.type);
        AdjustTextBubbleSize();
        StartCoroutine(ForceRebuildRoutine());

        if (openImmediately)
        {
            #if UNITY_IOS && !UNITY_EDITOR
                _ShowQuickLook(localPath);
            #else
                NativeShare share = new NativeShare();
                if (!string.IsNullOrEmpty(vm.mimeType)) share.AddFile(localPath, vm.mimeType);
                else share.AddFile(localPath);
                share.Share();
            #endif
        }
    }
    
    IEnumerator ForceRebuildRoutine()
    {
        yield return null;

        // Self rebuild stays immediate — MirrorSize and RefreshCorners below
        // read this bubble's rect on the same frame.
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

        // Parent (content) and grandparent (viewport) are marked dirty instead
        // of force-rebuilt. ForceRebuildLayoutImmediate on these was the main
        // amplifier of chat-open freeze: every async media decode triggered
        // two full content-tree rebuilds (50+ bubbles). MarkLayoutForRebuild
        // coalesces concurrent dirties into a single deferred pass.
        if (transform.parent is RectTransform parentRt)
            LayoutRebuilder.MarkLayoutForRebuild(parentRt);

        if (transform.parent != null && transform.parent.parent is RectTransform grandparentRt)
            LayoutRebuilder.MarkLayoutForRebuild(grandparentRt);

        if (bubbleBackground != null)
        {
            var mirror = bubbleBackground.GetComponent<MirrorSize>();
            if (mirror != null) mirror.UpdateSize();
        }

        RefreshCorners(messageImage != null ? messageImage.gameObject : null);
        RefreshCorners(bubbleBackground != null ? bubbleBackground.gameObject : null);
        RefreshCorners(outline);
    }

    private void RefreshCorners(GameObject targetObject)
    {
        if (targetObject == null) return;
        
        var rounded = targetObject.GetComponent<ImageWithRoundedCorners>();
        if (rounded != null && rounded.enabled)
        {
            float currentRadius = rounded.radius;
            rounded.radius = 0;
            rounded.radius = currentRadius;
            
            rounded.Validate();
            rounded.Refresh();
        }
    }

    void HandleFinalFailure(bool isManual, bool isAudio = false)
    {
        if (loadingSpinner) loadingSpinner.SetActive(false);

        if (isManual)
        {
            if (expiredPlaceholder)
            {
                expiredPlaceholder.SetActive(true);
                // Match the download button fill so the expired placeholder reads as
                // the same kind of card-on-bubble that preceded it.
                if (expiredPlaceholder.TryGetComponent<Image>(out var expiredImg))
                    expiredImg.color = DownloadFillColor;
            }
            if (downloadButton) downloadButton.gameObject.SetActive(false);
        }
        else
        {
            if (expiredPlaceholder) expiredPlaceholder.SetActive(false);
            if (downloadButton) 
            {
                downloadButton.gameObject.SetActive(true);
                if (currentVm != null && currentVm.type != MessageType.Document)
                    SetLayoutToButton();
                else
                    ResetBubbleLayoutToDefault();
            }
        }
        
        if (!isAudio && currentVm != null && currentVm.type != MessageType.Document) 
        {
            messageImage.gameObject.SetActive(false);
            
            Transform currentParent = messageImage.transform.parent;
            if (currentParent != null && currentParent.name == "MediaContainer")
            {
                currentParent.gameObject.SetActive(false);
            }
        }
        
        // --- THE FIX: Restore the arrow after failure! ---
        if (downloadButton)
        {
            var btnImg = downloadButton.GetComponent<Image>();
            if (btnImg)
            {
                btnImg.enabled = true;
                if (downloadArrowIcon != null) btnImg.sprite = downloadArrowIcon;
                btnImg.color = DownloadFillColor;
            }
            if (downloadButtonText) downloadButtonText.gameObject.SetActive(true);
        }

        if (bubbleBackground != null && bubbleBackground.TryGetComponent<LayoutElement>(out var pLayout))
        {
            pLayout.preferredHeight = -1; 
        }

        UpdateBubbleVisuals();
        if (currentVm != null) ApplyDynamicLayout(currentVm.type);
        
        StartCoroutine(ForceRebuildRoutine());
    }
    
    IEnumerator WaitAndRetry(MessageViewModel vm, int nextAttempt)
    {
        yield return new WaitForSeconds(2.0f);
        
        if (this == null || !gameObject.activeInHierarchy) yield break;
        
        StartDownload(vm, nextAttempt, false);      
    }

    void DisplayMedia(string source, bool isSticker, bool isManual, float bubbleRatio = 1.0f)
    {
        if (string.IsNullOrEmpty(source)) 
        {
            messageImage.sprite = null;
            messageImage.color = Color.black;
            StartCoroutine(ForceRebuildRoutine());
            return;
        }

        if (isSticker && stickerPlaceholder != null) 
        {
            messageImage.sprite = stickerPlaceholder; 
            fullScreenSprite = stickerPlaceholder;
        }
        else 
        {
            messageImage.sprite = null; 
            fullScreenSprite = null;
        }
            
        messageImage.color = Color.white; 

        if (source.StartsWith("base64://"))
        {
            LoadBase64Image(source.Substring(9), isSticker, bubbleRatio);
        }
        else
        {
            // --- THE UNIVERSAL INSTANT CACHE INTERCEPT ---
            if (MediaCacheManager.Instance.IsImageCached(source))
            {
                string filePath = MediaCacheManager.Instance.GetFilePathFromUrl(source);
                byte[] bytes = System.IO.File.ReadAllBytes(filePath);

                if (isSticker) TryDecodeSticker(bytes, bubbleRatio);
                else 
                {
                    Texture2D tex = new Texture2D(2, 2);
                    if (tex.LoadImage(bytes)) ApplyTextureAspectFill(tex, false, bubbleRatio);
                }
            }
            else
            {
                StartCoroutine(LoadWebPOrImage(source, isSticker, bubbleRatio)); 
            }
        }
    }
    
    IEnumerator VisualMediaWaterfall(MessageViewModel vm, float bubbleRatio, bool isManual)
    {
        if (isManual)
        {
            if (loadingSpinner) loadingSpinner.SetActive(false);

            Transform currentParent = messageImage.transform.parent;

            if (!string.IsNullOrEmpty(vm.thumbnailUrl))
            {
                if (downloadButton) downloadButton.gameObject.SetActive(false);
                if (currentParent != null && currentParent.name == "MediaContainer")
                {
                    currentParent.gameObject.SetActive(true);
                    SetupMaskedLayout(bubbleRatio, (vm.aspectRatio > 0 ? vm.aspectRatio : 1.0f), vm.isSticker);
                }
            }
            else
            {
                if (currentParent != null && currentParent.name == "MediaContainer") currentParent.gameObject.SetActive(false);
                
                if (downloadButton) 
                {
                    downloadButton.gameObject.SetActive(true);
                    var btnImg = downloadButton.GetComponent<Image>();
                    
                    // --- THE FIX: Leave button enabled, erase the arrow, paint it solid! ---
                    if (btnImg)
                    {
                        btnImg.enabled = true; 
                        btnImg.sprite = null; // Hide the arrow
                        btnImg.color = vm.isSticker ? Color.white : new Color(0.15f, 0.15f, 0.15f, 1f); // Make it a solid card
                    }
                    
                    if (downloadButtonText) downloadButtonText.gameObject.SetActive(false);
                }
                
                if (bubbleBackground != null && bubbleBackground.TryGetComponent<LayoutElement>(out var bgLayout)) 
                {
                    bgLayout.preferredHeight = -1; 
                }
            }

            UpdateBubbleVisuals();
            ApplyDynamicLayout(vm.type);
            StartCoroutine(ForceRebuildRoutine());

            if (messageImage.sprite == null)
                messageImage.color = vm.isSticker ? Color.clear : new Color(0.15f, 0.15f, 0.15f, 1f);

            if (loadingSpinner)
            {
                loadingSpinner.SetActive(true);
                
                if (bubbleBackground != null)
                {
                    loadingSpinner.transform.SetParent(bubbleBackground.transform, false);
                    loadingSpinner.transform.SetAsLastSibling(); 
                }
                
                // I have completely removed the script modifying the spinner's color, 
                // so your native white spinner background will display perfectly!

                RectTransform rt = loadingSpinner.GetComponent<RectTransform>();
                if (rt)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                }

                LayoutElement le = loadingSpinner.GetComponent<LayoutElement>();
                if (!le) le = loadingSpinner.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
            }
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool isLinkExpired = vm.expireTime > 0 && vm.expireTime < now;
        string hdUrl = (vm.type == MessageType.Video) ? vm.videoUrl : vm.mediaUrl;

        bool hasS3 = !string.IsNullOrEmpty(hdUrl) && !isLinkExpired && hdUrl.StartsWith("http");
        bool s3Success = false;

        if (hasS3)
        {
            using UnityWebRequest www = UnityWebRequest.Get(hdUrl);
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                s3Success = true;
                ProcessHDBytes(www.downloadHandler.data, vm, bubbleRatio);
                if (loadingSpinner) loadingSpinner.SetActive(false);
            }
        }

        if (!s3Success)
        {
            AttemptWappiApiFetch(vm, bubbleRatio, 0, isManual);
        }
        else
        {
            if (loadingSpinner) loadingSpinner.SetActive(false);
        }
    }

    void AttemptWappiApiFetch(MessageViewModel vm, float bubbleRatio, int attempt, bool isManual)
    {
        ChatManager.Instance.DownloadMediaForMessage(vm.messageId,
            (newUrl) =>
            {
                if (this == null || !gameObject.activeInHierarchy) return;

                vm.expireTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400;
                if (vm.type == MessageType.Video) vm.videoUrl = newUrl;
                else vm.mediaUrl = newUrl;

                StartCoroutine(DownloadAndApplyHDImage(newUrl, vm, bubbleRatio, isManual));
            },
            () =>
            {
                if (this == null || !gameObject.activeInHierarchy) return;

                if (attempt < 1) 
                {
                    StartCoroutine(WaitAndRetryVisualApi(vm, bubbleRatio, attempt + 1, isManual));
                }
                else 
                {
                    if (isManual) HandleFinalFailure(true, false);
                    else ShowVisualDownloadButton(vm);
                }
            }
        );
    }

    IEnumerator WaitAndRetryVisualApi(MessageViewModel vm, float bubbleRatio, int nextAttempt, bool isManual)
    {
        yield return new WaitForSeconds(1.5f);
        AttemptWappiApiFetch(vm, bubbleRatio, nextAttempt, isManual);
    }

    IEnumerator DownloadAndApplyHDImage(string url, MessageViewModel vm, float bubbleRatio, bool isManual)
    {
        if (url.StartsWith("base64://"))
        {
            LoadBase64Image(url.Substring(9), vm.isSticker, bubbleRatio);
            if (loadingSpinner) loadingSpinner.SetActive(false);
            yield break;
        }

        using UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            ProcessHDBytes(www.downloadHandler.data, vm, bubbleRatio);
            if (loadingSpinner) loadingSpinner.SetActive(false);
        }
        else
        {
            if (isManual) HandleFinalFailure(true, false);
            else ShowVisualDownloadButton(vm);
        }
    }

    void ProcessHDBytes(byte[] bytes, MessageViewModel vm, float targetRatio)
    {
        if (loadingSpinner) 
        {
            loadingSpinner.SetActive(false); 
        }

        Transform currentParent = messageImage.transform.parent;
        Transform bubbleParent = (currentParent.name == "MediaContainer") ? currentParent.parent : currentParent;
        
        if (currentParent != null && currentParent.name == "MediaContainer")
        {
            if (currentParent.TryGetComponent<LayoutElement>(out var cLayout)) 
                cLayout.preferredHeight = -1;
        }

        // --- THE FIX: Restore the arrow after success! ---
        if (downloadButton)
        {
            var btnImg = downloadButton.GetComponent<Image>();
            if (btnImg)
            {
                btnImg.enabled = true;
                if (downloadArrowIcon != null) btnImg.sprite = downloadArrowIcon;
                btnImg.color = DownloadFillColor;
            }
            if (downloadButtonText) downloadButtonText.gameObject.SetActive(true);
        }

        float realRatio = vm.aspectRatio > 0 ? vm.aspectRatio : 1.0f;
        SetupMaskedLayout(targetRatio, realRatio, vm.isSticker);
        
        UpdateBubbleVisuals();
        ApplyDynamicLayout(vm.type);

        if (vm.isSticker) TryDecodeSticker(bytes, targetRatio);
        else
        {
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes)) ApplyTextureAspectFill(tex, vm.isSticker, targetRatio);
        }
    }

    void ShowVisualDownloadButton(MessageViewModel vm)
    {
        if (loadingSpinner) 
        {
            loadingSpinner.SetActive(false);
        }

        messageImage.gameObject.SetActive(false);

        // --- THE FIX: Restore the arrow if fallback triggers! ---
        if (downloadButton)
        {
            downloadButton.gameObject.SetActive(true);
            var btnImg = downloadButton.GetComponent<Image>();
            if (btnImg)
            {
                btnImg.enabled = true;
                if (downloadArrowIcon != null) btnImg.sprite = downloadArrowIcon;
                btnImg.color = DownloadFillColor;
            }
            if (downloadButtonText) downloadButtonText.gameObject.SetActive(true);
        }

        SetDownloadButtonText(vm.type);
        SetLayoutToButton();

        var btnLe = downloadButton.GetComponent<LayoutElement>();
        if (!btnLe) btnLe = downloadButton.gameObject.AddComponent<LayoutElement>();
        btnLe.ignoreLayout = false;

        UpdateBubbleVisuals();
        ApplyDynamicLayout(vm.type);
        StartCoroutine(ForceRebuildRoutine());
    }
    
    IEnumerator LoadWebPOrImage(string url, bool isSticker, float targetRatio)
    {
        using UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            byte[] bytes = www.downloadHandler.data;
            
            // --- NEW: Save ALL media (including stickers) to hard drive! ---
            MediaCacheManager.Instance.SaveImageToCache(url, bytes);

            if (isSticker) TryDecodeSticker(bytes, targetRatio);
            else
            {
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes)) ApplyTextureAspectFill(tex, false, targetRatio);
            }
        }
    }

    void LoadBase64Image(string base64, bool isSticker, float targetRatio)
    {
        try {
            int mod4 = base64.Length % 4;
            if (mod4 > 0) 
            {
                base64 += new string('=', 4 - mod4);
            }

            byte[] bytes = Convert.FromBase64String(base64);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes)) 
            {
                ApplyTextureAspectFill(tex, isSticker, targetRatio);
            }
        } catch (Exception e) { 
            Debug.LogError("Base64 Decode Error: " + e.Message); 
        }
    }

    void TryDecodeSticker(byte[] rawBytes, float targetRatio)
    {
        try 
        {
            byte[] staticBytes = GetFirstFrameOfWebP(rawBytes);
            Texture2D tex = Texture2DExt.CreateTexture2DFromWebP(staticBytes, true, false, out Error error);
            
            if (error == Error.Success && tex != null) 
            {
                ApplyTextureAspectFill(tex, true, targetRatio);
            }
            else
            {
                Texture2D fallbackTex = new Texture2D(2, 2);
                if (fallbackTex.LoadImage(rawBytes)) ApplyTextureAspectFill(fallbackTex, true, targetRatio);
            }
        } 
        catch (Exception)
        {
            Texture2D fallbackTex = new Texture2D(2, 2);
            if (fallbackTex.LoadImage(rawBytes)) ApplyTextureAspectFill(fallbackTex, true, targetRatio);
        }
    }

    private byte[] GetFirstFrameOfWebP(byte[] data)
    {
        try 
        {
            if (data == null || data.Length < 16) return data;
            if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F') return data;
            if (data[8] != 'W' || data[9] != 'E' || data[10] != 'B' || data[11] != 'P') return data;

            bool isAnimated = false;
            
            for (int i = 12; i < data.Length - 8; i++) 
            {
                if (data[i] == 'V' && data[i+1] == 'P' && data[i+2] == '8' && data[i+3] == 'X') 
                {
                    if ((data[i+8] & 2) != 0) isAnimated = true; 
                    break;
                }
            }

            if (!isAnimated) return data; 

            for (int i = 12; i < data.Length - 8; i++) 
            {
                if (data[i] == 'A' && data[i+1] == 'N' && data[i+2] == 'M' && data[i+3] == 'F') 
                {
                    int anmfSize = data[i+4] | (data[i+5] << 8) | (data[i+6] << 16) | (data[i+7] << 24);
                    int frameDataStart = i + 8 + 16;
                    int frameDataSize = anmfSize - 16;

                    if (frameDataStart + frameDataSize <= data.Length) 
                    {
                        byte[] innerData = new byte[frameDataSize];
                        Array.Copy(data, frameDataStart, innerData, 0, frameDataSize);

                        int w = 1 + (data[i+8+6] | (data[i+8+7] << 8) | (data[i+8+8] << 16));
                        int h = 1 + (data[i+8+9] | (data[i+8+10] << 8) | (data[i+8+11] << 16));

                        byte[] vp8xChunk = new byte[18];
                        vp8xChunk[0] = (byte)'V'; vp8xChunk[1] = (byte)'P'; vp8xChunk[2] = (byte)'8'; vp8xChunk[3] = (byte)'X';
                        vp8xChunk[4] = 10; vp8xChunk[5] = 0; vp8xChunk[6] = 0; vp8xChunk[7] = 0;
                        
                        bool hasAlpha = (innerData.Length > 4 && innerData[0] == 'A' && innerData[1] == 'L' && innerData[2] == 'P' && innerData[3] == 'H');
                        bool isVP8L = (innerData.Length > 4 && innerData[0] == 'V' && innerData[1] == 'P' && innerData[2] == '8' && innerData[3] == 'L');
                        
                        vp8xChunk[8] = (byte)((hasAlpha || isVP8L) ? 0x10 : 0); 
                        
                        vp8xChunk[9] = 0; vp8xChunk[10] = 0; vp8xChunk[11] = 0; 
                        
                        int cw = w - 1;
                        vp8xChunk[12] = (byte)(cw & 0xFF);
                        vp8xChunk[13] = (byte)((cw >> 8) & 0xFF);
                        vp8xChunk[14] = (byte)((cw >> 16) & 0xFF);
                        
                        int ch = h - 1;
                        vp8xChunk[15] = (byte)(ch & 0xFF);
                        vp8xChunk[16] = (byte)((ch >> 8) & 0xFF);
                        vp8xChunk[17] = (byte)((ch >> 16) & 0xFF);

                        int newFileSize = 4 + 18 + innerData.Length;
                        byte[] newWebp = new byte[8 + newFileSize];
                        
                        newWebp[0] = (byte)'R'; newWebp[1] = (byte)'I'; newWebp[2] = (byte)'F'; newWebp[3] = (byte)'F';
                        newWebp[4] = (byte)(newFileSize & 0xFF);
                        newWebp[5] = (byte)((newFileSize >> 8) & 0xFF);
                        newWebp[6] = (byte)((newFileSize >> 16) & 0xFF);
                        newWebp[7] = (byte)((newFileSize >> 24) & 0xFF);
                        newWebp[8] = (byte)'W'; newWebp[9] = (byte)'E'; newWebp[10] = (byte)'B'; newWebp[11] = (byte)'P';

                        Array.Copy(vp8xChunk, 0, newWebp, 12, 18);
                        Array.Copy(innerData, 0, newWebp, 30, innerData.Length);

                        return newWebp; 
                    }
                }
            }
            return data;
        } 
        catch { return data; }
    }

    void ApplyTextureAspectFill(Texture2D tex, bool isSticker, float targetRatio)
    {
        messageImage.color = Color.white;
        
        tex.wrapMode = TextureWrapMode.Clamp;

        if (isSticker)
        {
            messageImage.type = Image.Type.Simple;
            messageImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            fullScreenSprite = messageImage.sprite;
            messageImage.preserveAspect = true;
        }
        else
        {
            float imageRatio = (float)tex.width / tex.height;
            int cropW = tex.width;
            int cropH = tex.height;
            int x = 0;
            int y = 0;

            if (Mathf.Abs(imageRatio - targetRatio) > 0.01f) 
            {
                if (imageRatio > targetRatio)
                {
                    cropW = Mathf.RoundToInt(tex.height * targetRatio);
                    x = (tex.width - cropW) / 2;
                }
                else
                {
                    cropH = Mathf.RoundToInt(tex.width / targetRatio);
                    y = (tex.height - cropH) / 2;
                }

                Color[] pixels = tex.GetPixels(x, y, cropW, cropH);
                Texture2D croppedTex = new Texture2D(cropW, cropH, tex.format, false);
                croppedTex.wrapMode = TextureWrapMode.Clamp;
                croppedTex.SetPixels(pixels);
                croppedTex.Apply();

                messageImage.type = Image.Type.Simple;
                messageImage.sprite = Sprite.Create(croppedTex, new Rect(0, 0, cropW, cropH), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                
                fullScreenSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
            else
            {
                messageImage.type = Image.Type.Simple;
                messageImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                fullScreenSprite = messageImage.sprite;
            }

            messageImage.preserveAspect = false; 
        }

        StartCoroutine(ForceRebuildRoutine());
    }

    void OnVisualClicked(MessageViewModel vm)
    {
        if (ScrollClickBlocker.IsBlocking) return;
        
        if (vm.type == MessageType.Video)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool isLinkExpired = vm.expireTime > 0 && vm.expireTime < now;
            bool isCachedLocally = MediaCacheManager.Instance.IsImageCached(vm.videoUrl);

            if (!string.IsNullOrEmpty(vm.videoUrl) && (!isLinkExpired || isCachedLocally))
            {
                if (VideoController.Instance != null)
                {
                    if (isCachedLocally)
                    {
                        string localPath = MediaCacheManager.Instance.GetFilePathFromUrl(vm.videoUrl);
                        VideoController.Instance.PlayVideo(localPath, vm.aspectRatio);
                    }
                    else
                    {
                        VideoController.Instance.PlayVideo(vm.videoUrl, vm.aspectRatio);
                    }
                }
            }
            else 
            {
                // --- THE FIX: Bypass the bulky download UI! ---
                // Fetch the new URL quietly and play it instantly.
                StartCoroutine(RefreshAndPlayVideo(vm)); 
            }
        }
        else if (vm.type == MessageType.Image)
        {
            Sprite targetSprite = fullScreenSprite != null ? fullScreenSprite : messageImage.sprite;
            
            if (targetSprite != null && PhotoViewer.Instance != null)
                PhotoViewer.Instance.ShowImage(targetSprite);
        }
    }
    IEnumerator RefreshAndPlayVideo(MessageViewModel vm)
    {
        // 1. Hide the Play Icon and show the Spinner perfectly centered OVER the thumbnail
        if (playOverlay != null) playOverlay.SetActive(false);
        
        if (loadingSpinner != null)
        {
            loadingSpinner.SetActive(true);
            loadingSpinner.transform.SetParent(messageImage.transform, false);
            loadingSpinner.transform.SetAsLastSibling();

            var rt = loadingSpinner.GetComponent<RectTransform>();
            if (rt)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }

            var le = loadingSpinner.GetComponent<LayoutElement>();
            if (!le) le = loadingSpinner.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        bool apiSuccess = false;
        string fetchedUrl = "";
        bool apiDone = false;

        // 2. Ask Wappi for the fresh, unexpired URL
        ChatManager.Instance.DownloadMediaForMessage(vm.messageId, 
            (url) => { apiSuccess = true; fetchedUrl = url; apiDone = true; },
            () => { apiSuccess = false; apiDone = true; }
        );

        while (!apiDone) yield return null;

        // 3. Restore the UI back to normal
        if (loadingSpinner != null) loadingSpinner.SetActive(false);
        if (playOverlay != null) playOverlay.SetActive(true);

        // 4. If we got the new URL, update the memory and immediately launch the video!
        if (apiSuccess)
        {
            vm.expireTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400; // Good for another 24 hours
            vm.videoUrl = fetchedUrl;

            if (VideoController.Instance != null)
            {
                VideoController.Instance.PlayVideo(fetchedUrl, vm.aspectRatio);
            }
        }
        else
        {
            Debug.LogWarning("Failed to refresh expired video link from Wappi.");
        }
    }
    
    void UpdateBubbleVisuals()
    {
        if (bubbleBackground == null || currentVm == null) return;

        bool isDownloadActive = downloadButton != null && downloadButton.gameObject.activeSelf;
        bool isExpiredActive = expiredPlaceholder != null && expiredPlaceholder.activeSelf;
        bool isPlaceholderActive = isDownloadActive || isExpiredActive;

        // Bubble keeps its incoming/outgoing colour in every state — both the
        // download button and the expired placeholder sit on top as cards instead
        // of swapping the bubble to a dark fill. Stickers normally go transparent
        // (the sticker IS the content), but while the placeholder card is showing
        // the bubble has to be visible so the card has a colour to contrast with.
        bool isTransparent = (currentVm.isSticker && !isPlaceholderActive) || hideBubble;

        if (isTransparent)
        {
            bubbleBackground.color = Color.clear;
        }
        else
        {
            bubbleBackground.color = currentVm.isIncoming ? incomingColor : outgoingColor;
        }
        
        if (isTransparent)
        {
            outline.SetActive(false);
        }
        else
        {
            // outline.SetActive(true);
        }

        var bubbleRounded = bubbleBackground.GetComponent<ImageWithRoundedCorners>();
        var bubbleOutlineRounded = outline.GetComponent<Image>().GetComponent<ImageWithRoundedCorners>();
        if (isTransparent)
        {
            if (bubbleRounded) bubbleRounded.enabled = false;
            bubbleBackground.material = null; 
            if (bubbleOutlineRounded) bubbleOutlineRounded.enabled = false;
            outline.GetComponent<Image>().material = null; 
        }
        else
        {
            if (!bubbleRounded) bubbleRounded = bubbleBackground.gameObject.AddComponent<ImageWithRoundedCorners>();
            if (!bubbleOutlineRounded) bubbleOutlineRounded = outline.AddComponent<ImageWithRoundedCorners>();

            bubbleRounded.enabled = true;
            bubbleOutlineRounded.enabled = true;

            bubbleRounded.radius = 28f;
            bubbleOutlineRounded.radius = 29; 

            bubbleRounded.Validate();
            bubbleOutlineRounded.Validate();
            bubbleRounded.Refresh();
            bubbleOutlineRounded.Refresh();
            
        }

        if (bubbleTail != null)
        {
            bubbleTail.SetActive(currentShowTail && !isTransparent);
            bubbleTail.transform.GetChild(0).GetComponent<Image>().color = bubbleBackground.color;
        }
    }
    
// --- THE FIX: Assemble everything invisibly, then POP it on screen! ---
// --- THE FIX: Assemble everything invisibly, then POP it on screen! ---
    private IEnumerator ProcessLinkPreviewSilently(string url, MessageViewModel vm)
    {
        // --- THE RACE CONDITION FIX ---
        // Force the cache to wait exactly one frame. This ensures the main Bind() method 
        // completely finishes setting up the default text BEFORE we try to erase it!
        yield return null;

        string scrapedTitle = null;
        string scrapedDesc = null;
        string scrapedImage = null;
        bool scrapeDone = false;

        LinkScraper.Instance.FetchPreview(url, (title, desc, imageUrl) => {
            scrapedTitle = title; scrapedDesc = desc; scrapedImage = imageUrl; scrapeDone = true;
        });

        while (!scrapeDone) yield return null;

        bool hasTitle = !string.IsNullOrWhiteSpace(scrapedTitle);
        bool hasDesc = !string.IsNullOrWhiteSpace(scrapedDesc);

        // No useful preview content — leave the inline text link as-is.
        if (!hasTitle && !hasDesc) yield break;

        Texture2D downloadedTex = null;
        
        // Try to load the image IF one was provided
        if (!string.IsNullOrEmpty(scrapedImage))
        {
            // 1. CHECK CACHE FIRST: Do we already have this image saved on the phone?
            downloadedTex = MediaCacheManager.Instance.LoadImageFromCache(scrapedImage);

            // 2. ONLY DOWNLOAD IF MISSING: If it's not on the hard drive, fetch it from the web!
            if (downloadedTex == null)
            {
                using UnityWebRequest www = UnityWebRequest.Get(scrapedImage);
                yield return www.SendWebRequest();
                
                if (www.result == UnityWebRequest.Result.Success)
                {
                    byte[] imageBytes = www.downloadHandler.data;
                    downloadedTex = new Texture2D(2, 2);
                    
                    if (downloadedTex.LoadImage(imageBytes))
                    {
                        // 3. SAVE FOR LATER: Write the bytes to the hard drive so we never have to download it again!
                        MediaCacheManager.Instance.SaveImageToCache(scrapedImage, imageBytes);
                    }
                    else
                    {
                        downloadedTex = null;
                    }
                }
            }
        }

        if (this == null || !gameObject.activeInHierarchy) yield break;

        if (hasTitle)
        {
            linkPreviewTitle.overflowMode = TextOverflowModes.Ellipsis;
            linkPreviewTitle.maxVisibleLines = 3;
            linkPreviewTitle.text = scrapedTitle;
            linkPreviewTitle.gameObject.SetActive(true);
        }
        else
        {
            linkPreviewTitle.gameObject.SetActive(false);
        }

        linkPreviewDomain.overflowMode = TextOverflowModes.Ellipsis;
        linkPreviewDomain.maxVisibleLines = 1;
        linkPreviewDomain.text = new Uri(url).Host.Replace("www.", "");

        if (linkPreviewDescription != null)
        {
            if (hasDesc)
            {
                linkPreviewDescription.overflowMode = TextOverflowModes.Ellipsis;
                linkPreviewDescription.maxVisibleLines = 2;
                linkPreviewDescription.text = scrapedDesc;
                linkPreviewDescription.gameObject.SetActive(true);
            }
            else
            {
                linkPreviewDescription.gameObject.SetActive(false);
            }
        }

        // 2. Apply your Display Rules based on the Image!
        if (downloadedTex != null)
        {
            // IMAGE FOUND: Show the image, hide the text link!
            linkPreviewImage.sprite = Sprite.Create(downloadedTex, new Rect(0, 0, downloadedTex.width, downloadedTex.height), new Vector2(0.5f, 0.5f));
            linkPreviewImage.color = Color.white;
            linkPreviewImage.gameObject.SetActive(true);

// --- THE UPDATED ERASER: Safely handle leftover text ---
            if (messageText != null)
            {
                // Use Regex to obliterate the URL AND any invisible spaces/newlines touching it!
                string pattern = @"\s*" + System.Text.RegularExpressions.Regex.Escape(url) + @"\s*";
                string textWithoutUrl = System.Text.RegularExpressions.Regex.Replace(vm.text ?? "", pattern, "").Trim();

                if (string.IsNullOrWhiteSpace(textWithoutUrl))
                {
                    messageText.text = ""; 
                    messageText.gameObject.SetActive(false); // Hide the text box completely!
                    
                    var textLe = messageText.GetComponent<LayoutElement>();
                    if (textLe != null) textLe.preferredWidth = 0; 
                }
                else
                {
                    // If the user typed a message with the link, keep their message visible!
                    _mainMessageOriginalText = textWithoutUrl;
                    messageText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(textWithoutUrl, out bool hasMissingUrl);
                    if (hasMissingUrl) SubscribeToEmojiReady();
                    messageText.gameObject.SetActive(true);
                }
            }
        }
        else
        {
            // NO IMAGE FOUND: Hide the image space, leave the text link ACTIVE!
            linkPreviewImage.gameObject.SetActive(false);
            
            if (messageText != null)
            {
                string originalText = FormatTextWithWrappableLinks(vm.text ?? "");
                _mainMessageOriginalText = originalText;
                messageText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(originalText, out bool hasMissingLink);
                if (hasMissingLink) SubscribeToEmojiReady();
                messageText.gameObject.SetActive(true);
            }
        }

        // Swap Hierarchy Order so Card is above the text
        if (messageText != null && messageText.gameObject.activeSelf)
        {
            int currentTextIndex = messageText.transform.GetSiblingIndex();
            linkPreviewCard.transform.SetSiblingIndex(currentTextIndex);
            messageText.transform.SetSiblingIndex(currentTextIndex + 1);
        }

        // 3. Turn on the card
        linkPreviewCard.SetActive(true);

        // if (messageText != null && messageText.gameObject.activeSelf)
        // {
        //     messageText.margin = new Vector4(16, 8, 16, 0); 
        // }

        ApplyDynamicLayout(vm.type);

        // 4. Recalculate and trigger the Magic Toggle for the corners!
        AdjustTextBubbleSize();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        if (transform.parent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(transform.parent as RectTransform);

        gameObject.SetActive(false);
        gameObject.SetActive(true);

        StartCoroutine(ForceRebuildRoutine());
    }
private Color GetSenderColor(string name)
    {
        if (string.IsNullOrEmpty(name)) return Color.gray;

        // WhatsApp-style Name Colors
        Color[] groupColors = new Color[]
        {
            new Color(0.9f, 0.35f, 0.4f), // Red/Pink
            new Color(0.3f, 0.65f, 0.9f), // Light Blue
            new Color(0.2f, 0.75f, 0.4f), // Green
            new Color(0.6f, 0.4f, 0.8f),  // Purple
            new Color(0.9f, 0.55f, 0.2f), // Orange
            new Color(0.1f, 0.65f, 0.6f), // Teal
            new Color(0.85f, 0.3f, 0.55f) // Magenta
        };

        // Create a stable hash based on the letters in their name
        int hash = 0;
        foreach (char c in name) hash += c;
        
        return groupColors[hash % groupColors.Length];
    }

// --- THE FIX: Inject Zero-Width Spaces so long URLs wrap perfectly! ---
    private string FormatTextWithWrappableLinks(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        return System.Text.RegularExpressions.Regex.Replace(text, @"(https?://[^\s]+)", match => 
        {
            string rawUrl = match.Groups[1].Value;
            
            // Insert \u200B after common URL symbols so TextMeshPro knows it can break the line there
            string displayUrl = rawUrl.Replace("/", "/\u200B")
                .Replace("?", "?\u200B")
                .Replace("=", "=\u200B")
                .Replace("&", "&\u200B")
                .Replace("-", "-\u200B")
                .Replace("_", "_\u200B");
                                      
            // The raw URL stays in the <link> tag for clicking, but the displayUrl wraps beautifully!
            return $"<link=\"{rawUrl}\"><color=#2b88d9><u>{displayUrl}</u></color></link>";
        });
    }
    
    void HandleAudioMedia(MessageViewModel vm)
    {
        audioPanel.SetActive(true);
        // messageText.gameObject.SetActive(false); 
        
        if (timeText != null) timeText.margin = new Vector4(0, 0, 0, 0);
        
        if (audioSlider != null)
        {
            audioSlider.gameObject.SetActive(true);
            audioSlider.minValue = 0f;
            audioSlider.maxValue = vm.duration > 0 ? vm.duration : 1f;
            audioSlider.value = 0f;
        }

        TimeSpan t = TimeSpan.FromSeconds(vm.duration);
        if (audioDurationText) audioDurationText.text = string.Format("{0:D1}:{1:D2}", t.Minutes, t.Seconds);

        if (audioPlayButton)
        {
            audioPlayButton.onClick.RemoveAllListeners();
            audioPlayButton.onClick.AddListener(() => 
            {
                if (ScrollClickBlocker.IsBlocking) return;
                AudioController.Instance.ToggleAudio(vm.mediaUrl);
            });
        }
    }
    
    void HandleAudioStarted(string playingUrl) { if (currentVm != null && currentVm.mediaUrl == playingUrl && audioButtonIcon) audioButtonIcon.sprite = stopIcon; else if(audioButtonIcon) audioButtonIcon.sprite = playIcon; }
    void HandleAudioStopped(string stoppedUrl) 
    { 
        if (currentVm != null && currentVm.mediaUrl == stoppedUrl) 
        {
            if (audioButtonIcon) audioButtonIcon.sprite = playIcon;
            if (audioSlider != null) audioSlider.value = 0f; 
        } 
    }    
    void HandleAudioProgress(string url, float pos, float dur) 
    { 
        if (currentVm == null || currentVm.mediaUrl != url || isDragging) return; 
        
        if (audioSlider != null)
        {
            audioSlider.maxValue = dur > 0 ? dur : 1f; 
            audioSlider.value = pos; 
        }
    }
    
private string SplitLongWord(string text, TextMeshProUGUI textComp, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;

        float safeMaxWidth = maxWidth - 5f; 

        string[] words = text.Split(' ');
        
        System.Text.StringBuilder result = new System.Text.StringBuilder();

        foreach (string word in words)
        {
            if (textComp.GetPreferredValues(word, Mathf.Infinity, Mathf.Infinity).x <= safeMaxWidth)
            {
                result.Append(word).Append(" ");
                continue;
            }

            string remainingWord = word;
            
            while (textComp.GetPreferredValues(remainingWord, Mathf.Infinity, Mathf.Infinity).x > safeMaxWidth)
            {
                int low = 0;
                int high = remainingWord.Length;
                int bestFitIndex = 0;

                while (low <= high)
                {
                    int mid = low + (high - low) / 2;
                    float width = textComp.GetPreferredValues(remainingWord.Substring(0, mid), Mathf.Infinity, Mathf.Infinity).x;

                    if (width <= safeMaxWidth)
                    {
                        bestFitIndex = mid; 
                        low = mid + 1;      
                    }
                    else
                    {
                        high = mid - 1;     
                    }
                }

                result.Append(remainingWord.Substring(0, bestFitIndex)).Append("\n");
                
                remainingWord = remainingWord.Substring(bestFitIndex);
            }
            
            if (!string.IsNullOrEmpty(remainingWord))
            {
                result.Append(remainingWord).Append(" ");
            }
        }

        return result.ToString().TrimEnd();
    }    
    void SetupDocumentView(MessageViewModel vm, string decodedName)
    {
        if (documentPanel) documentPanel.SetActive(true);
        if (downloadButton) downloadButton.gameObject.SetActive(false);
        // if (messageText) messageText.gameObject.SetActive(false);

        if (documentNameText) documentNameText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(decodedName);
        
        string ext = System.IO.Path.GetExtension(decodedName).Replace(".", "").ToUpper();
        if (string.IsNullOrEmpty(ext)) ext = "FILE";
        
        string infoStr = ext;
        
        if (vm.fileSize > 0)
        {
            if (vm.pageCount > 0)
                infoStr = $"{vm.pageCount} pages • {FormatBytes(vm.fileSize)} • {ext}";
            else
                infoStr = $"{FormatBytes(vm.fileSize)} • {ext}";
        }
        
        if (documentInfoText) documentInfoText.text = infoStr;
        
        if (documentPanel != null)
        {
            var rounded = documentPanel.GetComponent<ImageWithRoundedCorners>();
            if (!rounded) rounded = documentPanel.gameObject.AddComponent<ImageWithRoundedCorners>();
            rounded.enabled = true;
            rounded.radius = 23;
            rounded.Validate();
            rounded.Refresh();
        }

        if (documentPanel)
        {
            Button docBtn = documentPanel.GetComponent<Button>();
            if (!docBtn) docBtn = documentPanel.gameObject.AddComponent<Button>();
            docBtn.onClick.RemoveAllListeners();
            docBtn.onClick.AddListener(() => 
            {
                if (ScrollClickBlocker.IsBlocking) return;
                StartCoroutine(DownloadAndOpenDocumentLocal(vm, decodedName, true));
            });
        }
    }
    private string GetLocalDocumentPath(string messageId, string decodedName)
    {
        string realFileName = decodedName;
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) realFileName = realFileName.Replace(c, '_');
        string dirPath = System.IO.Path.Combine(Application.temporaryCachePath, messageId);
        return System.IO.Path.Combine(dirPath, realFileName);
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        if (order <= 1)
        {
            return string.Format("{0:0} {1}", len, sizes[order]);
        }
        else
        {
            return string.Format("{0:0.#} {1}", len, sizes[order]);
        }
    }

    // --- Task 5: delivery-status re-render cluster ---

    // Reconfigures timeText to be absolutely positioned at bottom-right of Bubble
    // so it no longer reserves vertical space in the bubble's VerticalLayoutGroup.
    // Runs once per bubble instance; position offsets are applied later by
    // PositionFloatingTime() inside ApplyDynamicLayout.
    private void ConfigureFloatingTime()
    {
        if (timeText == null) return;
        if (floatingTimeConfigured) return;

        var le = timeText.GetComponent<LayoutElement>();
        if (le == null) le = timeText.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        var rt = timeText.rectTransform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);

        floatingTimeConfigured = true;
    }

    // Sets timeText.rectTransform.anchoredPosition relative to the
    // bottom-right corner of Bubble. rightInset is typically positive
    // (12 = 12px inset from the right edge); negative values push the
    // time outside the bubble edge. bottomInset is positive = up from
    // the bottom anchor.
    private void PositionFloatingTime(float rightInset, float bottomInset)
    {
        if (timeText == null) return;
        var rt = timeText.rectTransform;
        var pos = new Vector2(-rightInset, bottomInset);
        rt.anchoredPosition = pos;

    }

    // Sizes timeText's RectTransform to match its rendered content. Required
    // because the rect's pivot/anchor are bottom-right and LayoutElement.ignoreLayout
    // is true; with a zero sizeDelta, TMP overflow would draw the text out from
    // a single point and overlap surrounding bubble content.
    private void SyncFloatingTimeSize()
    {
        if (timeText == null) return;
        if (string.IsNullOrEmpty(timeText.text)) return;
        Vector2 size = timeText.GetPreferredValues(timeText.text, Mathf.Infinity, Mathf.Infinity);
        timeText.rectTransform.sizeDelta = size;
    }

    // Returns the pixel width that needs to be reserved at the end of a
    // wrappable text so timeText fits inline. Includes an 8px visual gap
    // between the trailing word and the time.
    private float MeasureTimeWidth()
    {
        if (timeText == null) return 0f;
        if (string.IsNullOrEmpty(timeText.text)) return 0f;
        float w = timeText.GetPreferredValues(timeText.text, Mathf.Infinity, Mathf.Infinity).x;
        return w + 8f;
    }

    // Removes a single trailing TMP <space=...> tag if present. Used to
    // scrub the previous reservation before appending a fresh one, so the
    // text doesn't accumulate stacked space tags across re-binds and
    // status-change re-renders.
    private static string StripTrailingReservation(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        int openIdx = input.LastIndexOf("<space=", System.StringComparison.Ordinal);
        if (openIdx < 0) return input;
        return input.Substring(0, openIdx);
    }

    // Appends a TMP <space={width}px> tag to the end of target.text so the
    // last line reserves horizontal room for the inline timestamp. TMP's
    // wrap logic treats the space as a regular glyph, so a full last line
    // pushes the space to a new line (and timeText, anchored at bottom-right,
    // follows visually).
    private void ApplyInlineTimeReservation(TextMeshProUGUI target)
    {
        if (target == null) return;
        if (!target.gameObject.activeSelf) return;
        if (string.IsNullOrEmpty(target.text)) return;
        if (isJumboEmoji) return;

        float w = MeasureTimeWidth();
        if (w <= 0f) return;

        string baseText = StripTrailingReservation(target.text);
        target.text = $"{baseText}<space={w:0.##}px><alpha=#00>.";
    }

    private void RefreshTimeAndTick()
    {
        if (timeText == null || currentVm == null) return;
        DateTime localTime = DateTimeOffset.FromUnixTimeSeconds(currentVm.timestamp).LocalDateTime;
        string formattedTime = localTime.ToString("HH:mm");
        string tickTag = currentVm.isIncoming ? null : DeliveryTickFormatter.GetSprite(currentVm.deliveryStatus);
        timeText.text = tickTag != null ? $"{formattedTime} {tickTag}" : formattedTime;
        SyncFloatingTimeSize();
    }

    private void SetDeliveryStatus(DeliveryStatus newStatus)
    {
        if (currentVm == null || currentVm.isIncoming) return;
        currentVm.deliveryStatus = newStatus;
        RefreshTimeAndTick();
        UpdateRetryButton(newStatus == DeliveryStatus.Failed);

        // Tick width may have changed — recompute the reserved space and
        // ask the layout to redraw so the bubble width re-snaps if needed.
        ApplyInlineTimeReservation(messageText);
        if (rectTransform != null) LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
    }

    private void HandleStatusChanged(string oldMessageId, string newMessageId, DeliveryStatus status)
    {
        if (currentVm == null || currentVm.isIncoming) return;
        // Match against EITHER the pre-swap id or the post-swap id. The optimistic
        // send path fires before any external mutation (currentVm.messageId is
        // still oldMessageId at fire time), but the ghost-send recovery path in
        // SyncLatestMessages writes the new id to the shared VM reference before
        // firing — so we must accept either form to be robust to call-site order.
        if (currentVm.messageId != oldMessageId && currentVm.messageId != newMessageId) return;
        if (newMessageId != oldMessageId) currentVm.messageId = newMessageId;
        SetDeliveryStatus(status);
    }

    private void HandleMediaRefreshed(MessageViewModel refreshed)
    {
        if (currentVm == null || refreshed == null) return;
        if (currentVm.messageId != refreshed.messageId) return;

        // ChatManager mutates the cached VM in place, so currentVm already
        // holds the new URL by the time this fires. Re-binding restarts the
        // media fetch under the fresh URL — the old MD5-keyed on-disk entry
        // is orphaned (the new URL hashes differently) and the fresh bytes
        // overwrite the stale render.
        bool showSenderName = senderNameText != null && senderNameText.gameObject.activeSelf;
        Bind(currentVm, currentShowTail, true, showSenderName);
        FinalizeCustomVisuals();
    }

    private void UpdateRetryButton(bool enableRetry)
    {
        if (timeText == null) return;

        if (enableRetry)
        {
            // raycastTarget must be true on EVERY enable, not just the first
            // lazy-create — the else branch sets it to false on Pending/Sent,
            // and the bubble can cycle Failed → Pending → Failed during a retry.
            timeText.raycastTarget = true;

            // Lazily create the Button on first failure for this bubble.
            if (retryButton == null)
            {
                retryButton = timeText.GetComponent<Button>() ?? timeText.gameObject.AddComponent<Button>();
            }
            retryButton.transition = Selectable.Transition.None;

            retryButton.onClick.RemoveAllListeners();
            string capturedMessageId = currentVm.messageId;
            retryButton.onClick.AddListener(() =>
            {
                if (ScrollClickBlocker.IsBlocking) return;
                if (ChatManager.Instance != null) ChatManager.Instance.RetryOutboxMessage(capturedMessageId);
            });
            retryButton.interactable = true;
        }
        else if (retryButton != null)
        {
            retryButton.onClick.RemoveAllListeners();
            retryButton.interactable = false;
            // Symmetric restore — pairs with timeText.raycastTarget = true in the
            // lazy-create branch. Keeps the time area non-blocking when not Failed.
            if (timeText != null) timeText.raycastTarget = false;
        }
    }
}